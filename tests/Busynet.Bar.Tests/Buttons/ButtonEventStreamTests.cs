using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using Busynet.Bar.Buttons;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;
using Proto = Busynet.Bar.Buttons.Proto;

namespace Busynet.Bar.Tests.Buttons;

public sealed class ButtonEventStreamTests
{
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    private static byte[] Frame(params (Proto.Button Button, Proto.ButtonAction Action)[] events)
    {
        var state = new Proto.State();
        foreach (var (button, action) in events)
        {
            state.Updates.Add(new Proto.StateUpdate
            {
                Input = new Proto.InputEvent { ButtonEvent = new Proto.ButtonEvent { Button = button, Action = action } },
            });
        }

        return state.ToByteArray();
    }

    /// <summary>One scripted connection: some frames, then an end, a throw, or a hang until cancelled.</summary>
    private sealed record Connection(byte[][] Frames, Exception? Error = null, bool Hang = false)
    {
        public static Connection Ends(params byte[][] frames) => new(frames);
        public static Connection Throws(Exception error, params byte[][] frames) => new(frames, error);
        public static Connection Hangs(params byte[][] frames) => new(frames, Hang: true);
    }

    private sealed class FakeBar(TestScheduler scheduler, params Connection[] script)
    {
        private readonly Queue<Connection> _script = new(script);
        public List<TimeSpan> Attempts { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];

        public IAsyncEnumerable<byte[]> Frames(CancellationToken ct)
        {
            // TestScheduler runs zero-delay work one tick late; truncate to whole seconds.
            Attempts.Add(S(scheduler.Clock / TimeSpan.TicksPerSecond));
            Tokens.Add(ct);
            return Run(_script.Count > 0 ? _script.Dequeue() : new Connection([], Hang: true), ct);
        }

        private static async IAsyncEnumerable<byte[]> Run(Connection connection, [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var frame in connection.Frames)
            {
                yield return frame;
            }

            if (connection.Error is { } error)
            {
                throw error;
            }

            if (connection.Hang)
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Lines.Add($"{logLevel}: {formatter(state, exception)} {exception}");
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                Lines.AddRange(values.Select(kv => $"{kv.Key}={kv.Value}"));
            }
        }
    }

    private static readonly byte[] StartPress = Frame((Proto.Button.Start, Proto.ButtonAction.Press));

    // Slack absorbs the one-tick-per-timer lag of the virtual clock.
    private static void RunTo(TestScheduler scheduler, TimeSpan t) => scheduler.AdvanceTo(t.Ticks + 10);

    [Fact]
    public void Emits_decoded_events_from_frames()
    {
        var scheduler = new TestScheduler();
        var bar = new FakeBar(scheduler, Connection.Hangs(
            StartPress,
            Frame((Proto.Button.Start, Proto.ButtonAction.Release), (Proto.Button.Back, Proto.ButtonAction.Press))));
        var events = new List<ButtonEvent>();

        using var _ = ButtonEventStream.Create(bar.Frames, scheduler, NullLogger.Instance).Subscribe(events.Add);
        RunTo(scheduler, S(0));

        Assert.Equal(
            [
                new ButtonEvent(BarButton.Start, ButtonAction.Press),
                new ButtonEvent(BarButton.Start, ButtonAction.Release),
                new ButtonEvent(BarButton.Back, ButtonAction.Press),
            ],
            events);
    }

    [Fact]
    public void Does_not_connect_until_subscribed()
    {
        var scheduler = new TestScheduler();
        var bar = new FakeBar(scheduler);

        var stream = ButtonEventStream.Create(bar.Frames, scheduler, NullLogger.Instance);
        RunTo(scheduler, S(60));
        Assert.Empty(bar.Attempts);

        using var _ = stream.Subscribe(_ => { });
        RunTo(scheduler, S(61));
        Assert.Single(bar.Attempts);
    }

    [Fact]
    public void Two_subscribers_share_one_connection()
    {
        var scheduler = new TestScheduler();
        var bar = new FakeBar(scheduler);
        var stream = ButtonEventStream.Create(bar.Frames, scheduler, NullLogger.Instance);

        using var a = stream.Subscribe(_ => { });
        using var b = stream.Subscribe(_ => { });
        RunTo(scheduler, S(10));

        Assert.Single(bar.Attempts);
    }

    [Fact]
    public void Reconnects_with_exponential_backoff_capped_at_30s()
    {
        var scheduler = new TestScheduler();
        var bar = new FakeBar(
            scheduler,
            Connection.Throws(new WebSocketException()),
            Connection.Ends(),
            Connection.Throws(new IOException()),
            Connection.Throws(new WebSocketException()),
            Connection.Ends(),
            Connection.Throws(new InvalidOperationException()));

        using var _ = ButtonEventStream.Create(bar.Frames, scheduler, NullLogger.Instance).Subscribe(_ => { });
        RunTo(scheduler, S(200));

        Assert.Equal([S(0), S(1), S(3), S(7), S(15), S(31), S(61)], bar.Attempts);
    }

    [Fact]
    public void Backoff_resets_after_a_frame_arrives()
    {
        var scheduler = new TestScheduler();
        var bar = new FakeBar(
            scheduler,
            Connection.Ends(),                   // 0 s, next in 1 s
            Connection.Ends(),                   // 1 s, next in 2 s
            Connection.Ends(),                   // 3 s, next in 4 s
            Connection.Ends(StartPress),         // 7 s, a frame: next in 1 s
            Connection.Ends());                  // 8 s, next in 2 s

        using var _ = ButtonEventStream.Create(bar.Frames, scheduler, NullLogger.Instance).Subscribe(_ => { });
        RunTo(scheduler, S(100));

        Assert.Equal([S(0), S(1), S(3), S(7), S(8), S(10)], bar.Attempts);
    }

    [Fact]
    public void Never_errors_or_completes()
    {
        var scheduler = new TestScheduler();
        var bar = new FakeBar(
            scheduler,
            Connection.Throws(new InvalidOperationException("boom"), StartPress),
            Connection.Ends(),
            Connection.Hangs(StartPress));
        var events = new List<ButtonEvent>();
        var errors = new List<Exception>();
        var completed = false;

        using var _ = ButtonEventStream.Create(bar.Frames, scheduler, NullLogger.Instance)
            .Subscribe(events.Add, errors.Add, () => completed = true);
        RunTo(scheduler, S(100));

        Assert.Empty(errors);
        Assert.False(completed);
        Assert.Equal(2, events.Count);
        Assert.Equal(3, bar.Attempts.Count);
    }

    [Fact]
    public void Disposing_last_subscriber_cancels_the_connection()
    {
        var scheduler = new TestScheduler();
        var bar = new FakeBar(scheduler);
        var stream = ButtonEventStream.Create(bar.Frames, scheduler, NullLogger.Instance);

        var a = stream.Subscribe(_ => { });
        var b = stream.Subscribe(_ => { });
        RunTo(scheduler, S(0));
        var token = Assert.Single(bar.Tokens);

        a.Dispose();
        Assert.False(token.IsCancellationRequested);

        b.Dispose();
        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public void Disposing_during_backoff_stops_reconnecting()
    {
        var scheduler = new TestScheduler();
        var bar = new FakeBar(scheduler, Connection.Ends());
        var subscription = ButtonEventStream.Create(bar.Frames, scheduler, NullLogger.Instance).Subscribe(_ => { });
        RunTo(scheduler, S(0.5));

        subscription.Dispose();
        RunTo(scheduler, S(100));

        Assert.Single(bar.Attempts);
    }

    [Fact]
    public void Drops_are_logged_at_debug_with_the_delay_and_error_type()
    {
        var scheduler = new TestScheduler();
        var logger = new CapturingLogger();
        var bar = new FakeBar(scheduler, Connection.Throws(new WebSocketException()), Connection.Ends());

        using var _ = ButtonEventStream.Create(bar.Frames, scheduler, logger).Subscribe(_ => { });
        RunTo(scheduler, S(2));

        var messages = logger.Lines.Where(l => l.StartsWith("Debug:", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, messages.Count);
        Assert.Contains("Button stream dropped, reconnecting in 00:00:01: WebSocketException", messages[0], StringComparison.Ordinal);
        Assert.Contains("Button stream dropped, reconnecting in 00:00:02: Closed", messages[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Log_messages_never_contain_the_connection_exception_message()
    {
        var scheduler = new TestScheduler();
        var logger = new CapturingLogger();
        var bar = new FakeBar(scheduler, Connection.Throws(new WebSocketException("ws://h?x-api-token=SECRET")));

        using var _ = ButtonEventStream.Create(bar.Frames, scheduler, logger).Subscribe(_ => { });
        RunTo(scheduler, S(5));

        Assert.NotEmpty(logger.Lines);
        Assert.DoesNotContain(logger.Lines, l => l.Contains("SECRET", StringComparison.Ordinal));
    }
}
