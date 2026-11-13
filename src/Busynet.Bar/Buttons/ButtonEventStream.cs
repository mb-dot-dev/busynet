using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Microsoft.Extensions.Logging;

namespace Busynet.Bar.Buttons;

public static class ButtonEventStream
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Button events decoded from the frames of a (re)connecting source. Lazy: nothing connects until the
    /// first subscription. Shared: all subscribers use one connection, cancelled when the last one disposes.
    /// Never errors or completes: when <paramref name="frames"/> ends or throws, it is reconnected after a
    /// back-off on <paramref name="scheduler"/> of 1 s, doubling to at most 30 s, reset by any frame.
    /// </summary>
    public static IObservable<ButtonEvent> Create(
        Func<CancellationToken, IAsyncEnumerable<byte[]>> frames,
        IScheduler scheduler,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(logger);

        return Observable.Create<ButtonEvent>(observer =>
            {
                var cts = new CancellationTokenSource();
                var token = cts.Token;
                var pending = new SerialDisposable();
                var delay = InitialDelay;

                void Connect() => _ = ListenAsync();

                async Task ListenAsync()
                {
                    string errorType;
                    try
                    {
                        await foreach (var frame in frames(token).WithCancellation(token).ConfigureAwait(false))
                        {
                            delay = InitialDelay;
                            foreach (var e in ButtonFrameDecoder.Decode(frame))
                            {
                                observer.OnNext(e);
                            }
                        }

                        errorType = "Closed";
                    }
                    catch (Exception) when (token.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        // Type name only: a WebSocketException's message can carry the URL, and with it the token.
                        errorType = ex.GetType().Name;
                    }

                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    var wait = delay;
                    delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxDelay.Ticks));
                    logger.LogDebug("Button stream dropped, reconnecting in {Delay}: {ErrorType}", wait, errorType);
                    pending.Disposable = scheduler.Schedule(wait, Connect);
                }

                pending.Disposable = scheduler.Schedule(Connect);

                // The source is cancelled, not disposed: a listener still unwinding may yet register on its token.
                // It has no timer or linked parent, so there is nothing for Dispose to release.
                return Disposable.Create(() =>
                {
                    cts.Cancel();
                    pending.Dispose();
                });
            })
            .Publish()
            .RefCount();
    }
}
