using Busynet.Bar.Layouts;
using Busynet.Bkk;
using Busynet.Bkk.Departures;
using Busynet.Cli.Features.BkkDepartures;
using Busynet.Cli.Tests.Fakes;
using Busynet.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

namespace Busynet.Cli.Tests.Features.BkkDepartures;

public sealed class BkkDeparturesWatchTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Rotate = TimeSpan.FromSeconds(5);
    private static readonly TimeZoneInfo Zone = DepartureLabelsTests.Zone;

    // TestScheduler's clock starts at DateTimeOffset.MinValue.
    private static readonly DateTimeOffset Start = new(0, TimeSpan.Zero);

    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    private static TimeSpan M(double m) => TimeSpan.FromMinutes(m);

    // One second past the minute, so the virtual clock's tick lag never lands on a minute boundary.
    private static Departure Live(TimeSpan at, string line) =>
        new(line, "Astoria M ► Budakeszi", Start + at + S(1), Start + at + S(1));

    private static StopDepartures Stop(params Departure[] departures) => new("Kötő utca", departures);

    private static readonly StopDepartures Three = Stop(Live(M(10), "922"), Live(M(20), "40"), Live(M(30), "88"));

    private sealed class Harness : IDisposable
    {
        public TestScheduler Scheduler { get; } = new();
        public FakeDisplay<DepartureFrame> Display { get; } = new();
        public CancellationTokenSource Cts { get; } = new();
        public ScriptedDepartures Departures { get; private set; } = null!;
        public Task Task { get; private set; } = Task.CompletedTask;

        public IEnumerable<string> Headers => Display.Draws.Select(d => d.Header);

        public Harness Run(TimeSpan interval, params Func<StopDepartures>[] polls)
        {
            Departures = new ScriptedDepartures(polls);
            Task = BkkDeparturesWatch.RunAsync(
                ct => Departures.GetDeparturesAsync("key", "BKK_F04808", ct),
                Display, interval, Rotate, Zone, Scheduler, NullLogger.Instance, Cts.Token);
            return this;
        }

        public Harness Run(params Func<StopDepartures>[] polls) => Run(Interval, polls);

        // Slack absorbs the one-tick-per-timer lag of the virtual clock.
        public void RunTo(TimeSpan t) => Scheduler.AdvanceTo(t.Ticks + 10);

        public void Dispose()
        {
            Cts.Cancel();
            Cts.Dispose();
        }
    }

    [Fact]
    public void Polls_at_once_and_draws_the_first_departure()
    {
        using var h = new Harness();
        h.Run(() => Three);

        h.RunTo(S(0));

        Assert.Equal(1, h.Departures.Polls);
        Assert.Equal(
            [FakeDisplay<DepartureFrame>.Clear, new DepartureFrame("1. 922 10'", "Astoria M > Budakeszi")],
            h.Display.Calls);
    }

    [Fact]
    public void Rotates_every_rotate_interval()
    {
        using var h = new Harness();
        h.Run(() => Three);

        h.RunTo(S(15));

        Assert.Equal(1, h.Departures.Polls);
        Assert.Equal(["1. 922 10'", "2. 40 19'", "3. 88 29'", "1. 922 9'"], h.Headers);
    }

    [Fact]
    public void Polls_every_interval_without_restarting_rotation()
    {
        using var h = new Harness();
        h.Run(() => Three, () => Stop(Live(M(10), "922"), Live(M(20), "40"), Live(M(25), "988")));

        h.RunTo(S(35));

        Assert.Equal(2, h.Departures.Polls);
        // Tick 6 (30 s) shows position 1, tick 7 (35 s) position 2: the counter was not reset by the poll.
        Assert.Equal(["1. 922 9'", "2. 40 19'"], h.Headers.TakeLast(2));
        Assert.Equal(8, h.Headers.Count());
    }

    [Fact]
    public void Minutes_count_down_between_polls()
    {
        using var h = new Harness();
        h.Run(TimeSpan.FromHours(1), () => Stop(Live(M(10), "922")));

        h.RunTo(M(5));

        Assert.Equal(1, h.Departures.Polls);
        Assert.Equal("1. 922 10'", h.Headers.First());
        Assert.Equal("1. 922 5'", h.Headers.Last());
    }

    [Fact]
    public void Transient_failure_keeps_rotating_the_last_list()
    {
        using var h = new Harness();
        h.Run(() => Three, () => throw new BkkTransientException("BKK API returned HTTP 502"));

        h.RunTo(S(40));

        Assert.Equal(2, h.Departures.Polls);
        Assert.Equal("3. 88 29'", h.Headers.Last());
        Assert.False(h.Task.IsCompleted);
    }

    [Fact]
    public async Task Fatal_failure_ends_the_watch_after_clearing()
    {
        using var h = new Harness();
        h.Run(() => throw new BkkException("Unknown BKK stop id: X."));

        h.RunTo(S(0));

        var e = await Assert.ThrowsAsync<BkkException>(() => h.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal("Unknown BKK stop id: X.", e.Message);
        Assert.Equal(FakeDisplay<DepartureFrame>.Clear, h.Display.Calls[^1]);
    }
}
