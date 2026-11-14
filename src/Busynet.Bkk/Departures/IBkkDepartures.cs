namespace Busynet.Bkk.Departures;

public interface IBkkDepartures
{
    /// <summary>Fetches the departures from <paramref name="stopId"/> over the next few hours.</summary>
    /// <exception cref="BkkException">The key was rejected, the stop is unknown, or another client error.</exception>
    /// <exception cref="BkkTransientException">Worth retrying on the next poll.</exception>
    Task<StopDepartures> GetDeparturesAsync(string apiKey, string stopId, CancellationToken ct);
}
