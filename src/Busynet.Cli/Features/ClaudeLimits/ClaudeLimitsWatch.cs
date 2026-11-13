using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Busynet.Bar.Buttons;
using Busynet.Bar.Layouts;
using Busynet.Claude.Usage;
using Busynet.Core.Watching;
using Microsoft.Extensions.Logging;

namespace Busynet.Cli.Features.ClaudeLimits;

public static class ClaudeLimitsWatch
{
    /// <summary>
    /// Shows <paramref name="seed"/>'s 5-hour limit at once, then refetches every <paramref name="interval"/>
    /// and redraws when the view changes. A Start press toggles between the 5-hour and weekly limits and redraws
    /// from the last fetched usage without fetching, so it never shortens or restarts a poll wait. Transient
    /// failures keep the display; a <see cref="Busynet.Claude.ClaudeAuthException"/> ends the watch (rethrown
    /// after clearing). Returns normally when <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    public static Task RunAsync(
        Usage seed,
        IClaudeUsage usage,
        IObservable<ButtonEvent> buttons,
        IDisplay<LimitGauge> display,
        TimeSpan interval,
        IScheduler scheduler,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(buttons);
        ArgumentNullException.ThrowIfNull(scheduler);

        var fetched = Poller.Poll(usage.GetUsageAsync, interval, initialDelay: interval, scheduler, logger)
            .Select(u => (Func<LimitsState, LimitsState>)(state => state.WithUsage(u)));

        var toggles = buttons
            .Where(e => e.Button == BarButton.Start && e.IsPress)
            .Select(_ => (Func<LimitsState, LimitsState>)(state => state.Toggle()));

        return DisplayWatch.RunAsync(
            new LimitsState(seed, LimitKind.Session),
            fetched.Merge(toggles),
            state => state.Render(scheduler.Now),
            display,
            logger,
            cancellationToken);
    }
}
