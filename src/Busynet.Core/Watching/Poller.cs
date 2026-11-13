using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Microsoft.Extensions.Logging;

namespace Busynet.Core.Watching;

public static class Poller
{
    /// <summary>Retry-After comes from an untrusted server.</summary>
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(1);

    /// <summary>
    /// Waits <paramref name="initialDelay"/>, fetches, emits, waits <paramref name="interval"/>, repeats.
    /// Fetches never overlap. A transient failure emits nothing and the next wait becomes
    /// max(interval, RetryAfter); any other exception terminates the sequence. Never completes.
    /// </summary>
    public static IObservable<T> Poll<T>(
        Func<CancellationToken, Task<T>> fetch,
        TimeSpan interval,
        TimeSpan initialDelay,
        IScheduler scheduler,
        ILogger logger)
    {
        return Observable.Defer(() =>
        {
            // Per-subscription state: the wait before the next cycle. Read inside Defer, which
            // Repeat re-subscribes only after the previous cycle has completed.
            var nextWait = initialDelay;

            return Observable.Defer(() =>
                {
                    var wait = nextWait;
                    nextWait = interval;

                    return Observable.Timer(wait, scheduler)
                        .SelectMany(_ => Observable.FromAsync(fetch))
                        .Catch<T, Exception>(e =>
                        {
                            if (!TransientFailure.IsTransient(e, out var retryAfter))
                            {
                                return Observable.Throw<T>(e);
                            }

                            logger.LogDebug("Poll failed, keeping the display as it is: {Error}", e.Message);
                            var requested = TimeSpan.FromTicks(Math.Clamp((retryAfter ?? TimeSpan.Zero).Ticks, 0, MaxRetryAfter.Ticks));
                            nextWait = requested > interval ? requested : interval;
                            return Observable.Empty<T>();
                        });
                })
                .Repeat();
        });
    }
}
