using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Busynet.Bar.Layouts;
using Busynet.Bkk.Departures;
using Busynet.Core.Watching;
using Microsoft.Extensions.Logging;

namespace Busynet.Cli.Features.BkkDepartures;

public static class BkkDeparturesWatch
{
    /// <summary>
    /// Polls at once and then every <paramref name="interval"/>, and moves to the next departure every
    /// <paramref name="rotate"/>. A poll replaces the list without resetting the rotation. Every frame is rendered from
    /// the scheduler's clock, so minutes count down between polls; an unchanged frame is not redrawn. Transient
    /// failures keep rotating the last list; a fatal one (rejected key, unknown stop) ends the watch, rethrown after
    /// clearing. Returns normally when <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    // Every parameter is a seam the tests drive (scripted fetch, fake display, virtual clock, fixed zone).
#pragma warning disable S107
    public static Task RunAsync(
        Func<CancellationToken, Task<StopDepartures>> fetch,
        IDisplay<DepartureFrame> display,
        TimeSpan interval,
        TimeSpan rotate,
        TimeZoneInfo zone,
        IScheduler scheduler,
        ILogger logger,
        CancellationToken cancellationToken)
#pragma warning restore S107
    {
        ArgumentNullException.ThrowIfNull(fetch);
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(scheduler);

        var polled = Poller.Poll(fetch, interval, initialDelay: TimeSpan.Zero, scheduler, logger)
            .Select(snapshot => (Func<DeparturesState, DeparturesState>)(state => state.WithSnapshot(snapshot)));

        var rotations = Observable.Interval(rotate, scheduler)
            .Select(_ => (Func<DeparturesState, DeparturesState>)(state => state.Advance()));

        return DisplayWatch.RunAsync(
            new DeparturesState(null, 0),
            polled.Merge(rotations),
            state => state.Render(scheduler.Now, zone),
            display,
            logger,
            cancellationToken);
    }
}
