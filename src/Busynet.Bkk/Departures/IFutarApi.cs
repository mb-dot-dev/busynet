using Refit;

namespace Busynet.Bkk.Departures;

/// <summary>The one FUTÁR endpoint used. The key travels in the query string, so the URL itself is secret.</summary>
public interface IFutarApi
{
    [Get("/api/query/v1/ws/otp/api/where/arrivals-and-departures-for-stop.json")]
    Task<HttpResponseMessage> GetArrivalsAndDeparturesForStopAsync(
        [AliasAs("key")] string key,
        [AliasAs("stopId")] string stopId,
        [AliasAs("minutesBefore")] int minutesBefore,
        [AliasAs("minutesAfter")] int minutesAfter,
        [AliasAs("limit")] int limit,
        CancellationToken ct);
}
