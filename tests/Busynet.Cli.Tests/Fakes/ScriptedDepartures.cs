using Busynet.Bkk.Departures;

namespace Busynet.Cli.Tests.Fakes;

/// <summary>
/// Scripted poll results in order, the last one repeating; a result may throw. Synchronous, so a virtual-clock
/// test sees each poll complete within the tick that started it. Records every (key, stop id) asked for.
/// </summary>
public sealed class ScriptedDepartures(params Func<StopDepartures>[] results) : IBkkDepartures
{
    private readonly Lock _gate = new();
    private readonly List<(string ApiKey, string StopId)> _calls = [];

    public int Polls
    {
        get
        {
            lock (_gate)
                return _calls.Count;
        }
    }

    public IReadOnlyList<(string ApiKey, string StopId)> Calls
    {
        get
        {
            lock (_gate)
                return [.. _calls];
        }
    }

    public Task<StopDepartures> GetDeparturesAsync(string apiKey, string stopId, CancellationToken ct)
    {
        int n;
        lock (_gate)
        {
            _calls.Add((apiKey, stopId));
            n = _calls.Count;
        }

        try
        {
            return Task.FromResult(results[Math.Min(n, results.Length) - 1]());
        }
        catch (Exception e)
        {
            return Task.FromException<StopDepartures>(e);
        }
    }
}
