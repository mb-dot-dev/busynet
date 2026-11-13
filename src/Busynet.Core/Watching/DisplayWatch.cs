using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Busynet.Core.Watching;

public static class DisplayWatch
{
    private static readonly TimeSpan ExitClearTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Clears the display, then folds <paramref name="updates"/> into state starting from
    /// <paramref name="initial"/>, renders each state and draws it. Draws are sequential; a view equal to the
    /// last <em>successfully</em> drawn one is skipped (redrawing restarts animations on the device), while a
    /// transient draw failure is logged and retried on the next update. A <c>null</c> render draws nothing.
    /// Runs until <paramref name="cancellationToken"/> is cancelled or <paramref name="updates"/> completes
    /// (returns normally) or errors / a draw fails fatally (rethrows). Always clears on the way out; a failure
    /// of that clear is logged and never masks the original outcome.
    /// </summary>
    public static async Task RunAsync<TState, TView>(
        TState initial,
        IObservable<Func<TState, TState>> updates,
        Func<TState, TView?> render,
        IDisplay<TView> display,
        ILogger logger,
        CancellationToken cancellationToken)
        where TView : class, IEquatable<TView>
    {
        await display.ClearAsync(cancellationToken);

        // Only touched from inside the Concat'd draws, which never overlap.
        TView? lastDrawn = null;

        // Held around every draw. Disposing the subscription (cancellation, an error from updates) cancels a
        // pending draw's token but does not wait for it, so the exit clear takes this gate first - and keeps
        // it - so that no draw can land after the clear. Not disposed: nothing allocates its wait handle.
        var drawGate = new SemaphoreSlim(1, 1);

        try
        {
            await updates
                .StartWith(s => s)
                .Scan(initial, (state, update) => update(state))
                .Select(render)
                .Where(view => view is not null)
                .Select(view => Observable.FromAsync(async token =>
                {
                    if (view!.Equals(lastDrawn))
                    {
                        return;
                    }

                    await drawGate.WaitAsync(token);
                    try
                    {
                        await display.DrawAsync(view, token);
                        lastDrawn = view;
                    }
                    catch (Exception e) when (TransientFailure.IsTransient(e, out _))
                    {
                        logger.LogDebug("Draw failed, will retry on the next update: {Error}", e.Message);
                    }
                    finally
                    {
                        drawGate.Release();
                    }
                }))
                .Concat()
                .LastOrDefaultAsync()
                .ToTask(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation is the normal way to stop watching.
        }
        finally
        {
            await ClearOnExitAsync(display, drawGate, logger);
        }
    }

    private static async Task ClearOnExitAsync<TView>(IDisplay<TView> display, SemaphoreSlim drawGate, ILogger logger)
    {
        using var timeout = new CancellationTokenSource(ExitClearTimeout);
        try
        {
            // Wait out any in-flight draw; never released, so a late draw cannot follow the clear.
            await drawGate.WaitAsync(timeout.Token);
            await display.ClearAsync(timeout.Token).WaitAsync(timeout.Token);
        }
        catch (Exception e)
        {
            logger.LogDebug("Clearing the display on exit failed: {Error}", e.Message);
        }
    }
}
