using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Refit;

namespace Busynet.Bkk.Departures;

/// <summary>
/// Classifies every failure as fatal (rejected key, unknown stop, other 4xx) or transient. No message ever includes
/// the key, the request URL or the body; transport failures are reported by exception type only, because their
/// messages can quote the URL.
/// </summary>
internal sealed class BkkDeparturesClient(IFutarApi api, FutarOptions options) : IBkkDepartures
{
    private const string Rejected = "BKK rejected the API key in BKK_API_KEY.";
    private const string Unexpected = "BKK API returned an unexpected response";
    private const double MaxRetryAfterSeconds = 3600;

    public async Task<StopDepartures> GetDeparturesAsync(string apiKey, string stopId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        ArgumentNullException.ThrowIfNull(stopId);

        // Refit returns once the headers arrive, so HttpClient.Timeout does not bound the body read.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(options.ResponseTimeout);
        try
        {
            using var response = await api.GetArrivalsAndDeparturesForStopAsync(
                apiKey, stopId, 0, options.MinutesAfter, options.Limit, deadline.Token).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (status is 401 or 403)
                throw new BkkException(Rejected);
            if (status == 404)
                throw new BkkException($"Unknown BKK stop id: {stopId}.");
            if (status == 429)
                throw new BkkTransientException("BKK API rate limited the request", ParseRetryAfter(response));
            if (status >= 500)
                throw new BkkTransientException($"BKK API returned HTTP {status}");
            if (status >= 400)
                throw new BkkException($"BKK API returned HTTP {status}");

            var body = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);
            return Parse(body, stopId);
        }
        catch (InvalidOperationException)
        {
            // Thrown by ReadAsStringAsync for an invalid charset; the message is not forwarded.
            throw new BkkTransientException(Unexpected);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or ApiRequestException)
        {
            // Refit wraps transport failures and timeouts in ApiRequestException: report the inner type.
            ct.ThrowIfCancellationRequested();
            var cause = e is ApiRequestException { InnerException: { } inner } ? inner : e;
            throw new BkkTransientException($"BKK request failed: {cause.GetType().Name}");
        }
    }

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Retry-After", out var values))
            return null;
        var raw = values.FirstOrDefault()?.Trim();
        if (!long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var seconds))
            return null;
        return TimeSpan.FromSeconds(Math.Clamp(seconds, 0, MaxRetryAfterSeconds));
    }

    private static StopDepartures Parse(string body, string stopId)
    {
        try
        {
            if (JsonNode.Parse(body) is JsonObject root
                && root["data"] is JsonObject data
                && data["entry"] is JsonObject entry
                && entry["stopTimes"] is JsonArray stopTimes
                && data["references"] is JsonObject references)
            {
                var departures = stopTimes
                    .Select(st => ReadDeparture(st as JsonObject, references))
                    .OfType<Departure>()
                    .OrderBy(d => d.When)
                    .ToList();
                var name = Text(references["stops"]?[stopId]?["name"]);
                return new StopDepartures(string.IsNullOrWhiteSpace(name) ? stopId : name, departures);
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            // Deliberately not forwarded: the message may quote the body.
        }

        throw new BkkTransientException(Unexpected);
    }

    /// <summary>One stop time, or <c>null</c> when it is cancelled or any needed piece is missing.</summary>
    private static Departure? ReadDeparture(JsonObject? stopTime, JsonObject references)
    {
        if (stopTime is null || stopTime["canceled"] is JsonValue c && c.GetValueKind() == JsonValueKind.True)
            return null;

        var trip = Text(stopTime["tripId"]) is { } tripId ? references["trips"]?[tripId] as JsonObject : null;
        var route = trip is not null && Text(trip["routeId"]) is { } routeId ? references["routes"]?[routeId] as JsonObject : null;
        var line = route is null ? null : Text(route["shortName"]);
        var scheduled = Seconds(stopTime["departureTime"]) ?? Seconds(stopTime["arrivalTime"]);
        if (string.IsNullOrWhiteSpace(line) || scheduled is not { } at)
            return null;

        var headsign = Text(stopTime["stopHeadsign"]) ?? Text(trip!["tripHeadsign"]) ?? "";
        var predicted = Seconds(stopTime["predictedDepartureTime"]) ?? Seconds(stopTime["predictedArrivalTime"]);
        return new Departure(line, headsign, at, predicted);
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    private static DateTimeOffset? Seconds(JsonNode? node) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<long>(out var s)
            ? DateTimeOffset.FromUnixTimeSeconds(s)
            : null;
}
