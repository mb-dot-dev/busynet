using System.Reactive.Subjects;
using Busynet.Bar;
using Busynet.Bar.Display;
using Busynet.Bar.Http;
using Busynet.Bar.Buttons;

namespace Busynet.Cli.Tests.Fakes;

/// <summary>Hands out one <see cref="FakeBarDisplay"/> and one button subject, recording each connection.</summary>
public sealed class FakeBarFactory : IBarFactory
{
    private readonly Lock _gate = new();
    private readonly List<BarConnection> _connections = [];

    public FakeBarDisplay Display { get; } = new();

    public Subject<ButtonEvent> Buttons { get; } = new();

    public IReadOnlyList<BarConnection> Connections
    {
        get
        {
            lock (_gate)
            {
                return [.. _connections];
            }
        }
    }

    public IBarDisplay CreateDisplay(BarConnection connection)
    {
        lock (_gate)
        {
            _connections.Add(connection);
        }

        return Display;
    }

    public IObservable<ButtonEvent> CreateButtonEvents(BarConnection connection) => Buttons;
}

/// <summary>
/// Records every draw payload and clear (<see cref="Clear"/>) in <see cref="Calls"/>, in order. Failures can
/// be queued; <see cref="OnDraw"/> runs inside each draw after it is recorded and before a queued failure.
/// </summary>
public sealed class FakeBarDisplay : IBarDisplay
{
    public const string Clear = "clear";

    private readonly Lock _gate = new();
    private readonly List<object> _calls = [];
    private readonly Queue<Exception> _drawFailures = new();
    private readonly Queue<Exception> _clearFailures = new();

    public Action<DisplayElements>? OnDraw { get; set; }

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

    public void FailNextDraw(Exception exception)
    {
        lock (_gate)
        {
            _drawFailures.Enqueue(exception);
        }
    }

    public void FailNextClear(Exception exception)
    {
        lock (_gate)
        {
            _clearFailures.Enqueue(exception);
        }
    }

    public Task DrawAsync(DisplayElements payload, CancellationToken ct)
    {
        Exception? failure;
        lock (_gate)
        {
            _calls.Add(payload);
            _drawFailures.TryDequeue(out failure);
        }

        OnDraw?.Invoke(payload);
        return failure is null ? Task.CompletedTask : Task.FromException(failure);
    }

    public Task ClearAsync(CancellationToken ct)
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
