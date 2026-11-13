using Busynet.Core.Watching;

namespace Busynet.Testing;

/// <summary>
/// Records every draw attempt (the view) and clear (<see cref="Clear"/>) in <see cref="Calls"/>, in order.
/// Failures can be queued for the next N draws or clears; <see cref="OnDraw"/> runs inside each draw
/// attempt, after it is recorded, and its task is awaited (use a TaskCompletionSource to hold a draw open).
/// </summary>
public sealed class FakeDisplay<TView> : IDisplay<TView>
{
    public const string Clear = "clear";

    private readonly Lock _gate = new();
    private readonly List<object> _calls = [];
    private readonly Queue<Exception> _drawFailures = new();
    private readonly Queue<Exception> _clearFailures = new();

    public Func<TView, Task>? OnDraw { get; set; }

    public IReadOnlyList<object> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    public IReadOnlyList<TView> Draws => [.. Calls.OfType<TView>()];

    /// <summary>Makes the next <paramref name="count"/> draws throw <paramref name="exception"/>.</summary>
    public void FailNextDraws(int count, Exception exception)
    {
        lock (_gate)
        {
            for (var i = 0; i < count; i++)
            {
                _drawFailures.Enqueue(exception);
            }
        }
    }

    /// <summary>Makes the next <paramref name="count"/> clears throw <paramref name="exception"/>.</summary>
    public void FailNextClears(int count, Exception exception)
    {
        lock (_gate)
        {
            for (var i = 0; i < count; i++)
            {
                _clearFailures.Enqueue(exception);
            }
        }
    }

    public async Task DrawAsync(TView view, CancellationToken cancellationToken)
    {
        Exception? failure;
        lock (_gate)
        {
            _calls.Add(view!);
            _drawFailures.TryDequeue(out failure);
        }

        if (OnDraw is { } onDraw)
        {
            await onDraw(view);
        }

        if (failure is not null)
        {
            throw failure;
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        Exception? failure;
        lock (_gate)
        {
            _calls.Add(Clear);
            _clearFailures.TryDequeue(out failure);
        }

        return failure is null ? Task.CompletedTask : Task.FromException(failure);
    }
}
