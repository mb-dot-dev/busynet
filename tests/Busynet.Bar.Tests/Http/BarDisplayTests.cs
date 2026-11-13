using System.Net;
using System.Text.Json.Nodes;
using Busynet.Bar.Layouts;
using Busynet.Core;
using Busynet.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Busynet.Bar.Tests.Http;

public sealed class BarDisplayTests
{
    private const string DrawPath = "/api/display/draw";

    private static readonly DisplayElements Payload = LimitsLayout.Build(LimitGauge.From("5h 85% 3h49m", 85));

    private static BarConnection Connection(string? token = null) =>
        new(new Uri("http://bar.test/"), new Uri("ws://bar.test/api/status/ws"), token);

    private static IBarDisplay Create(FakeHttpHandler fake, string? token = null)
    {
        var services = new ServiceCollection().AddBusyBar();
        services.AddHttpClient("busybar").ConfigurePrimaryHttpMessageHandler(() => fake);
        return services.BuildServiceProvider().GetRequiredService<IBarFactory>().CreateDisplay(Connection(token));
    }

    [Fact]
    public async Task Draw_posts_payload_to_draw_endpoint()
    {
        var fake = new FakeHttpHandler();
        fake.Add(HttpMethod.Post, DrawPath, HttpStatusCode.OK, "{}");

        await Create(fake).DrawAsync(Payload, TestContext.Current.CancellationToken);

        var call = Assert.Single(fake.Calls);
        Assert.Equal(DrawPath, call.Uri.AbsolutePath);
        Assert.Equal("busynet", JsonNode.Parse(call.Body!)!["application_name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Clear_deletes_scoped_to_application()
    {
        var fake = new FakeHttpHandler();
        fake.Add(HttpMethod.Delete, DrawPath, HttpStatusCode.OK, "{}");

        await Create(fake).ClearAsync(TestContext.Current.CancellationToken);

        var call = Assert.Single(fake.Calls);
        Assert.Equal(HttpMethod.Delete, call.Method);
        Assert.Contains("application_name=busynet", call.Uri.Query);
    }

    [Fact]
    public async Task Token_is_sent_as_header_when_set()
    {
        var fake = new FakeHttpHandler();
        fake.Add(HttpMethod.Post, DrawPath, HttpStatusCode.OK, "{}");

        await Create(fake, "tok").DrawAsync(Payload, TestContext.Current.CancellationToken);

        Assert.Equal("tok", Assert.Single(fake.Calls).Headers["X-API-Token"]);
    }

    [Fact]
    public async Task No_token_header_when_unset()
    {
        var fake = new FakeHttpHandler();
        fake.Add(HttpMethod.Post, DrawPath, HttpStatusCode.OK, "{}");

        await Create(fake).DrawAsync(Payload, TestContext.Current.CancellationToken);

        Assert.False(Assert.Single(fake.Calls).Headers.ContainsKey("X-API-Token"));
    }

    [Fact]
    public async Task Error_response_maps_to_BarApiException_with_one_line_message()
    {
        var fake = new FakeHttpHandler();
        fake.Add(HttpMethod.Post, DrawPath, HttpStatusCode.Conflict,
            """{"error":"Requested priority level is below that of currently active app","error_code":"priority"}""");

        var ex = await Assert.ThrowsAsync<BarApiException>(
            () => Create(fake).DrawAsync(Payload, TestContext.Current.CancellationToken));

        Assert.Equal(
            "HTTP 409 | POST /api/display/draw | Requested priority level is below that of currently active app | code=priority",
            ex.Message);
        Assert.IsAssignableFrom<ITransientFailure>(ex);
        Assert.IsAssignableFrom<BusynetException>(ex);
    }

    [Fact]
    public async Task Non_error_schema_body_falls_back_to_reason_phrase()
    {
        var fake = new FakeHttpHandler();
        fake.Add(HttpMethod.Delete, DrawPath, HttpStatusCode.InternalServerError, "<html>secret body</html>");

        var ex = await Assert.ThrowsAsync<BarApiException>(
            () => Create(fake).ClearAsync(TestContext.Current.CancellationToken));

        Assert.Equal("HTTP 500 | DELETE /api/display/draw | Internal Server Error", ex.Message);
    }

    [Fact]
    public async Task Transport_failure_maps_to_BarRequestException()
    {
        var fake = new FakeHttpHandler();
        fake.Throw(HttpMethod.Post, DrawPath, new HttpRequestException("boom http://x?token=SECRET"));

        var ex = await Assert.ThrowsAsync<BarRequestException>(
            () => Create(fake).DrawAsync(Payload, TestContext.Current.CancellationToken));

        Assert.Equal("request error | POST /api/display/draw | HttpRequestException", ex.Message);
        Assert.DoesNotContain("SECRET", ex.Message);
        Assert.IsAssignableFrom<ITransientFailure>(ex);
    }

    [Fact]
    public async Task Timeout_maps_to_BarRequestException()
    {
        var fake = new FakeHttpHandler();
        fake.Throw(HttpMethod.Post, DrawPath, new TaskCanceledException("timed out"));

        var ex = await Assert.ThrowsAsync<BarRequestException>(
            () => Create(fake).DrawAsync(Payload, TestContext.Current.CancellationToken));

        Assert.Equal("request error | POST /api/display/draw | TaskCanceledException", ex.Message);
    }

    [Fact]
    public async Task Caller_cancellation_is_rethrown()
    {
        var fake = new FakeHttpHandler();
        fake.Add(HttpMethod.Post, DrawPath, HttpStatusCode.OK, "{}");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(fake).DrawAsync(Payload, cts.Token));
    }
}
