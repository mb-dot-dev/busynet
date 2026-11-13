using System.Reactive.Subjects;
using Busynet.Bar;
using Busynet.Bar.Buttons;
using Busynet.Bar.Layouts;
using Busynet.Claude;
using Busynet.Claude.Usage;
using Busynet.Cli.Features.ClaudeLimits;
using Busynet.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

namespace Busynet.Cli.Tests.Features.ClaudeLimits;

public class ClaudeLimitsWatchTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(180);
    private static readonly ButtonEvent StartPress = new(BarButton.Start, ButtonAction.Press);

    /// <summary>Scripted results, in order; the last one repeats. Records the virtual time of each fetch.</summary>
    private sealed class FakeUsage(TestScheduler scheduler, long startTicks, Func<Usage>[] results) : IClaudeUsage
    {
        /// <summary>Time since the watch started, per fetch.</summary>
        public List<TimeSpan> Calls { get; } = [];

        public Task<Usage> GetUsageAsync(CancellationToken ct)
        {
            // TestScheduler runs zero-delay work one tick late; truncate to whole seconds.
            Calls.Add(TimeSpan.FromSeconds((scheduler.Clock - startTicks) / TimeSpan.TicksPerSecond));
            var result = results[Math.Min(Calls.Count, results.Length) - 1];
            try
            {
                return Task.FromResult(result());
            }
            catch (Exception e)
            {
                return Task.FromException<Usage>(e);
            }
        }
    }

    private sealed class Harness : IDisposable
    {
        public TestScheduler Scheduler { get; } = new();
        public DateTimeOffset Start { get; }
        public Subject<ButtonEvent> Buttons { get; } = new();
        public FakeDisplay<LimitGauge> Display { get; } = new();
        public CancellationTokenSource Cts { get; } = new();
        public FakeUsage Usage { get; private set; } = null!;
        public Task Task { get; private set; } = Task.CompletedTask;

        public Harness()
        {
            // Start the virtual clock somewhere realistic so reset times are ordinary dates.
            Scheduler.AdvanceTo(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero).Ticks);
            Start = Scheduler.Now;
        }

        public Harness Run(Usage seed, params Func<Usage>[] fetches)
        {
            Usage = new FakeUsage(Scheduler, Start.Ticks, fetches.Length == 0 ? [() => seed] : fetches);
            Task = ClaudeLimitsWatch.RunAsync(
                seed, Usage, Buttons, Display, Interval, Scheduler, NullLogger.Instance, Cts.Token);
            return this;
        }

        // Slack absorbs the one-tick-per-timer lag of the virtual clock.
        public void RunTo(TimeSpan t) => Scheduler.AdvanceTo(Start.Ticks + t.Ticks + 10);

        public void Press() => Buttons.OnNext(StartPress);

        public async Task StopAsync()
        {
            await Cts.CancelAsync();
            await Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public void Dispose()
        {
            Cts.Cancel();
            Cts.Dispose();
            Buttons.Dispose();
        }
    }

    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    private static Usage UsageAt(DateTimeOffset now, double session = 85, double weekly = 40) => new(
        new UsageWindow(session, now + TimeSpan.FromMinutes(3 * 60 + 49)),
        new UsageWindow(weekly, now + TimeSpan.FromHours(48) + TimeSpan.FromMinutes(10)));

    private static IEnumerable<string> Labels(Harness h) => h.Display.Draws.Select(g => g.Label);

    [Fact]
    public void Draws_seed_session_view_immediately_without_fetching()
    {
        using var h = new Harness();
        h.Run(UsageAt(h.Start));

        Assert.Equal([FakeDisplay<LimitGauge>.Clear, LimitGauge.From("5h 85% 3h49m", 85)], h.Display.Calls);
        Assert.Empty(h.Usage.Calls);
    }

    [Fact]
    public void Polls_every_interval_and_redraws_on_visible_change()
    {
        using var h = new Harness();
        var start = h.Start;
        h.Run(UsageAt(start), () => UsageAt(start, session: 90), () => UsageAt(start, session: 95));

        h.RunTo(S(179));
        Assert.Empty(h.Usage.Calls);

        h.RunTo(S(360));
        Assert.Equal([S(180), S(360)], h.Usage.Calls);
        Assert.Equal(["5h 85% 3h49m", "5h 90% 3h46m", "5h 95% 3h43m"], Labels(h));
    }

    [Fact]
    public void Does_not_redraw_when_only_invisible_values_changed()
    {
        using var h = new Harness();
        // No reset times, so the label has no countdown that could tick.
        var seed = new Usage(new UsageWindow(50.0, null), new UsageWindow(40, null));
        h.Run(seed, () => seed with { Session = new UsageWindow(50.2, null) });

        h.RunTo(S(360));

        Assert.Equal(2, h.Usage.Calls.Count);
        Assert.Single(h.Display.Draws);
    }

    [Fact]
    public void Start_press_switches_to_weekly_without_fetching()
    {
        using var h = new Harness();
        h.Run(UsageAt(h.Start));

        h.Press();

        Assert.Equal(["5h 85% 3h49m", "wk 40% 2d"], Labels(h));
        Assert.Empty(h.Usage.Calls);
    }

    [Fact]
    public void Second_press_returns_to_session()
    {
        using var h = new Harness();
        h.Run(UsageAt(h.Start));

        h.Press();
        h.Press();

        Assert.Equal(["5h 85% 3h49m", "wk 40% 2d", "5h 85% 3h49m"], Labels(h));
        Assert.Empty(h.Usage.Calls);
    }

    [Fact]
    public void Release_and_other_buttons_are_ignored()
    {
        using var h = new Harness();
        h.Run(UsageAt(h.Start));

        h.Buttons.OnNext(new ButtonEvent(BarButton.Start, ButtonAction.Release));
        h.Buttons.OnNext(new ButtonEvent(BarButton.Ok, ButtonAction.Press));
        h.Buttons.OnNext(new ButtonEvent(BarButton.Back, ButtonAction.Press));

        Assert.Equal(["5h 85% 3h49m"], Labels(h));
    }

    [Fact]
    public void Press_during_retry_after_wait_redraws_and_keeps_waiting()
    {
        using var h = new Harness();
        var start = h.Start;
        h.Run(
            UsageAt(start),
            () => throw new ClaudeTransientException("rate limited", S(600)),
            () => UsageAt(start));

        h.RunTo(S(180));
        Assert.Equal([S(180)], h.Usage.Calls);

        h.RunTo(S(190));
        h.Press();
        Assert.Equal(["5h 85% 3h49m", "wk 40% 2d"], Labels(h));

        h.RunTo(S(779));
        Assert.Equal([S(180)], h.Usage.Calls);
        h.RunTo(S(780));
        Assert.Equal([S(180), S(780)], h.Usage.Calls);
    }

    [Fact]
    public void Press_uses_current_time_for_label()
    {
        using var h = new Harness();
        var start = h.Start;
        h.Run(UsageAt(start), () => throw new ClaudeTransientException("down"));

        h.RunTo(TimeSpan.FromMinutes(30));
        h.Press();

        Assert.Equal(["5h 85% 3h49m", "wk 40% 47h"], Labels(h));
    }

    [Fact]
    public void Transient_fetch_failure_keeps_display()
    {
        using var h = new Harness();
        h.Run(UsageAt(h.Start), () => throw new ClaudeTransientException("HTTP 503"));

        h.RunTo(S(540));

        Assert.Equal(3, h.Usage.Calls.Count);
        Assert.Equal([FakeDisplay<LimitGauge>.Clear, LimitGauge.From("5h 85% 3h49m", 85)], h.Display.Calls);
        Assert.False(h.Task.IsCompleted);
    }

    [Fact]
    public async Task Auth_failure_ends_watch_after_clearing()
    {
        using var h = new Harness();
        h.Run(UsageAt(h.Start), () => throw new ClaudeAuthException("log in again"));

        h.RunTo(S(180));

        await Assert.ThrowsAsync<ClaudeAuthException>(() => h.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(FakeDisplay<LimitGauge>.Clear, h.Display.Calls[^1]);
        Assert.False(h.Buttons.HasObservers);
    }

    [Fact]
    public void Two_presses_around_a_failed_redraw_leave_display_consistent()
    {
        using var h = new Harness();
        h.Run(UsageAt(h.Start));
        h.Display.FailNextDraws(1, new BarApiException(409, "POST", "/api/display/draw", "busy", null));

        h.Press();
        h.Press();

        // The weekly redraw was refused, so the session view is still on the bar: press two flips back to
        // exactly that, and nothing more is drawn (same as busyboy).
        Assert.Equal(["5h 85% 3h49m", "wk 40% 2d"], Labels(h));
        Assert.False(h.Task.IsCompleted);

        // The display is not stuck: the next press draws weekly again.
        h.Press();
        Assert.Equal(["5h 85% 3h49m", "wk 40% 2d", "wk 40% 2d"], Labels(h));
    }

    [Fact]
    public async Task Button_subscription_is_disposed_on_exit()
    {
        using var h = new Harness();
        h.Run(UsageAt(h.Start));
        Assert.True(h.Buttons.HasObservers);

        await h.StopAsync();

        Assert.False(h.Buttons.HasObservers);
        Assert.Equal(FakeDisplay<LimitGauge>.Clear, h.Display.Calls[^1]);
    }
}
