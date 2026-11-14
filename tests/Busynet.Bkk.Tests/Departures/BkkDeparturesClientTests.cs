using System.Net;
using System.Text;
using Busynet.Bkk.Departures;
using Busynet.Core;
using Busynet.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Busynet.Bkk.Tests.Departures;

public sealed class BkkDeparturesClientTests
{
    private const string Path = "/api/query/v1/ws/otp/api/where/arrivals-and-departures-for-stop.json";
    private const string Key = "SECRET-KEY";
    private const string Stop = "BKK_F04808";

    // Shaped like a real response (2026-11-14), trimmed to the fields that are read plus a few that are not.
    private const string Payload = """
        {
          "currentTime": 1794696472596, "version": 2, "status": "OK", "code": 200, "text": "OK",
          "data": {
            "limitExceeded": false,
            "entry": {
              "stopId": "BKK_F04808",
              "routeIds": ["BKK_9220"],
              "stopTimes": [
                { "stopId": "BKK_F04808", "stopHeadsign": "Kelenföld vasútállomás M", "tripId": "T40E",
                  "departureTime": 1794699000, "predictedDepartureTime": 1794699060, "canceled": false },
                { "stopId": "BKK_F04808", "stopHeadsign": "Astoria M ► Budakeszi", "tripId": "T922",
                  "arrivalTime": 1794697400, "departureTime": 1794697440,
                  "predictedArrivalTime": 1794697480, "predictedDepartureTime": 1794697500, "zoneId": "1" },
                { "stopId": "BKK_F04808", "tripId": "T940", "arrivalTime": 1794700000 },
                { "stopId": "BKK_F04808", "stopHeadsign": "Astoria M ► Budakeszi", "tripId": "T988",
                  "departureTime": 1794698000, "canceled": true }
              ]
            },
            "references": {
              "stops": { "BKK_F04808": { "id": "BKK_F04808", "name": "Kötő utca", "direction": "106" } },
              "trips": {
                "T40E": { "id": "T40E", "routeId": "BKK_040E", "tripHeadsign": "Kelenföld vasútállomás M" },
                "T922": { "id": "T922", "routeId": "BKK_9220", "tripHeadsign": "Astoria M" },
                "T940": { "id": "T940", "routeId": "BKK_9400", "tripHeadsign": "Kamaraerdő" },
                "T988": { "id": "T988", "routeId": "BKK_9880", "tripHeadsign": "Astoria M" }
              },
              "routes": {
                "BKK_040E": { "id": "BKK_040E", "shortName": "40E", "type": "BUS" },
                "BKK_9220": { "id": "BKK_9220", "shortName": "922", "type": "BUS" },
                "BKK_9400": { "id": "BKK_9400", "shortName": "940", "type": "BUS" },
                "BKK_9880": { "id": "BKK_9880", "shortName": "988", "type": "BUS" }
              }
            }
          }
        }
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IBkkDepartures Create(FakeHttpHandler fake)
    {
        var services = new ServiceCollection();
        services.AddBkk();
        services.AddHttpClient("bkk").ConfigurePrimaryHttpMessageHandler(() => fake);
        return services.BuildServiceProvider().GetRequiredService<IBkkDepartures>();
    }

    private static FakeHttpHandler Responding(HttpStatusCode status, string? json = null, IDictionary<string, string>? headers = null)
    {
        var fake = new FakeHttpHandler();
        fake.Add(HttpMethod.Get, Path, status, json, headers);
        return fake;
    }

    private static FakeHttpHandler RespondingText(HttpStatusCode status, string text)
    {
        var fake = new FakeHttpHandler();
        fake.Add(HttpMethod.Get, Path, _ => new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "text/plain") });
        return fake;
    }

    private static DateTimeOffset At(long seconds) => DateTimeOffset.FromUnixTimeSeconds(seconds);

    [Fact]
    public async Task Parses_departures_in_time_order()
    {
        var result = await Create(Responding(HttpStatusCode.OK, Payload)).GetDeparturesAsync(Key, Stop, Ct);

        Assert.Equal("Kötő utca", result.StopName);
        Assert.Equal(["922", "40E", "940"], result.Departures.Select(d => d.Line));

        var bus922 = result.Departures[0];
        Assert.Equal("Astoria M ► Budakeszi", bus922.Headsign);
        Assert.Equal(At(1794697440), bus922.Scheduled);
        Assert.Equal(At(1794697500), bus922.Predicted);
        Assert.True(bus922.IsLive);
        Assert.Equal(At(1794697500), bus922.When);

        var bus940 = result.Departures[2];
        Assert.Equal("Kamaraerdő", bus940.Headsign);
        Assert.Equal(At(1794700000), bus940.Scheduled);
        Assert.Null(bus940.Predicted);
        Assert.False(bus940.IsLive);
    }

    [Fact]
    public async Task Sends_key_stop_and_window()
    {
        var fake = Responding(HttpStatusCode.OK, Payload);

        await Create(fake).GetDeparturesAsync(Key, Stop, Ct);

        var call = Assert.Single(fake.Calls);
        Assert.Equal("futar.bkk.hu", call.Uri.Host);
        var query = call.Uri.Query.TrimStart('?').Split('&');
        Assert.Contains($"key={Key}", query);
        Assert.Contains($"stopId={Stop}", query);
        Assert.Contains("minutesBefore=0", query);
        Assert.Contains("minutesAfter=180", query);
        Assert.Contains("limit=10", query);
    }

    [Fact]
    public async Task Skips_entries_it_cannot_read()
    {
        var payload = Payload.Replace(
            "\"stopTimes\": [",
            """
            "stopTimes": [
                { "stopId": "BKK_F04808", "stopHeadsign": "Nowhere", "tripId": "MISSING", "departureTime": 1794697000 },
                { "stopId": "BKK_F04808", "stopHeadsign": "Timeless", "tripId": "T922" },
                { "stopId": "BKK_F04808", "tripId": "T922", "departureTime": "soon" },
            """,
            StringComparison.Ordinal);

        var result = await Create(Responding(HttpStatusCode.OK, payload)).GetDeparturesAsync(Key, Stop, Ct);

        Assert.Equal(["922", "40E", "940"], result.Departures.Select(d => d.Line));
    }

    [Fact]
    public async Task Falls_back_to_the_stop_id_without_a_stop_reference()
    {
        var payload = Payload.Replace("\"name\": \"Kötő utca\", ", "", StringComparison.Ordinal);

        var result = await Create(Responding(HttpStatusCode.OK, payload)).GetDeparturesAsync(Key, Stop, Ct);

        Assert.Equal(Stop, result.StopName);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Rejected_key_is_fatal(HttpStatusCode status)
    {
        var fake = RespondingText(status, "Invalid API key. Please register at https://opendata.bkk.hu");

        var e = await Assert.ThrowsAnyAsync<BkkException>(() => Create(fake).GetDeparturesAsync(Key, Stop, Ct));

        Assert.IsNotAssignableFrom<ITransientFailure>(e);
        Assert.Equal("BKK rejected the API key in BKK_API_KEY.", e.Message);
    }

    [Fact]
    public async Task Unknown_stop_is_fatal()
    {
        var fake = Responding(HttpStatusCode.NotFound,
            """{"currentTime":1794696782210,"version":2,"status":"NOT_FOUND","code":404,"text":"Unknown stopId."}""");

        var e = await Assert.ThrowsAnyAsync<BkkException>(() => Create(fake).GetDeparturesAsync(Key, "BKK_NOPE", Ct));

        Assert.IsNotAssignableFrom<ITransientFailure>(e);
        Assert.Equal("Unknown BKK stop id: BKK_NOPE.", e.Message);
    }

    [Fact]
    public async Task Other_client_errors_are_fatal()
    {
        var e = await Assert.ThrowsAnyAsync<BkkException>(() => Create(Responding(HttpStatusCode.BadRequest)).GetDeparturesAsync(Key, Stop, Ct));

        Assert.IsNotAssignableFrom<ITransientFailure>(e);
        Assert.Equal("BKK API returned HTTP 400", e.Message);
    }

    [Fact]
    public async Task Rate_limit_is_transient_with_retry_after()
    {
        var fake = Responding(HttpStatusCode.TooManyRequests, headers: new Dictionary<string, string> { ["Retry-After"] = "20" });

        var e = await Assert.ThrowsAsync<BkkTransientException>(() => Create(fake).GetDeparturesAsync(Key, Stop, Ct));

        Assert.Equal(TimeSpan.FromSeconds(20), e.RetryAfter);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, null)]
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.OK, """{"status":"OK"}""")]
    [InlineData(HttpStatusCode.OK, """{"status":"OK","data":{"entry":{"stopId":"BKK_F04808"}}}""")]
    public async Task Server_error_and_bad_body_are_transient(HttpStatusCode status, string? body)
    {
        var fake = body is null ? Responding(status) : RespondingText(status, body);

        await Assert.ThrowsAsync<BkkTransientException>(() => Create(fake).GetDeparturesAsync(Key, Stop, Ct));
    }

    [Fact]
    public async Task Transport_failure_is_transient()
    {
        var fake = new FakeHttpHandler();
        fake.Throw(HttpMethod.Get, Path, new HttpRequestException("connection refused"));

        var e = await Assert.ThrowsAsync<BkkTransientException>(() => Create(fake).GetDeparturesAsync(Key, Stop, Ct));

        Assert.Equal("BKK request failed: HttpRequestException", e.Message);
    }

    public static TheoryData<string> Failures => ["401", "404", "500", "transport"];

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Failure_messages_never_contain_the_key(string failure)
    {
        var fake = new FakeHttpHandler();
        switch (failure)
        {
            case "transport":
                fake.Throw(HttpMethod.Get, Path,
                    new HttpRequestException($"GET https://futar.bkk.hu{Path}?key={Key} failed", new IOException($"key={Key}")));
                break;
            default:
                fake.Add(HttpMethod.Get, Path, (HttpStatusCode)int.Parse(failure, System.Globalization.CultureInfo.InvariantCulture),
                    $$"""{"text":"bad key {{Key}}"}""");
                break;
        }

        var e = await Assert.ThrowsAnyAsync<BkkException>(() => Create(fake).GetDeparturesAsync(Key, Stop, Ct));

        Assert.DoesNotContain(Key, e.ToString(), StringComparison.Ordinal);
    }
}
