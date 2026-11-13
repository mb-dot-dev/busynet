using System.Net;
using Busynet.Claude.Credentials;
using Busynet.Claude.Usage;
using Busynet.Core;
using Busynet.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Busynet.Claude.Tests.Usage;

public sealed class ClaudeUsageClientTests
{
    private const string Path = "/api/oauth/usage";
    private const string Secret = "sk-ant-SECRET-token";

    private const string Payload = """
        {
          "five_hour": {"utilization": 3.0, "resets_at": "2026-11-12T20:29:59.606009+00:00", "limit_dollars": null, "locked_reason": null},
          "seven_day": {"utilization": 26.0, "resets_at": "2026-11-15T01:59:59.606027+00:00"},
          "tangelo": null,
          "limits": [{"kind": "session", "percent": 3}]
        }
        """;

    private sealed class CountingTokens(string token) : IClaudeTokenSource
    {
        public int Calls { get; private set; }

        public Task<string> GetTokenAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(token);
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IClaudeUsage Create(FakeHttpHandler fake, IClaudeTokenSource? tokens = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(tokens ?? new CountingTokens(Secret));
        services.AddClaude();
        services.AddHttpClient("claude").ConfigurePrimaryHttpMessageHandler(() => fake);
        return services.BuildServiceProvider().GetRequiredService<IClaudeUsage>();
    }

    private static FakeHttpHandler Responding(HttpStatusCode status, string? json = null, IDictionary<string, string>? headers = null)
    {
        var fake = new FakeHttpHandler();
        fake.Add(HttpMethod.Get, Path, status, json, headers);
        return fake;
    }

    [Fact]
    public async Task Parses_the_real_payload_shape()
    {
        var usage = await Create(Responding(HttpStatusCode.OK, Payload)).GetUsageAsync(Ct);

        Assert.Equal(3.0, usage.Session.Utilization);
        Assert.Equal(26.0, usage.Weekly.Utilization);
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 20, 29, 59, TimeSpan.Zero).AddTicks(6060090), usage.Session.ResetsAt);
        Assert.NotNull(usage.Weekly.ResetsAt);
    }

    [Fact]
    public async Task Sends_required_headers()
    {
        var fake = Responding(HttpStatusCode.OK, Payload);

        await Create(fake).GetUsageAsync(Ct);

        var call = Assert.Single(fake.Calls);
        Assert.Equal("api.anthropic.com", call.Uri.Host);
        Assert.Equal($"Bearer {Secret}", call.Headers["Authorization"]);
        Assert.Equal("oauth-2025-04-20", call.Headers["anthropic-beta"]);
        Assert.StartsWith("claude-code/", call.Headers["User-Agent"]);
    }

    [Fact]
    public async Task Null_resets_at_is_allowed()
    {
        var json = """{"five_hour":{"utilization":1,"resets_at":null},"seven_day":{"utilization":2}}""";

        var usage = await Create(Responding(HttpStatusCode.OK, json)).GetUsageAsync(Ct);

        Assert.Null(usage.Session.ResetsAt);
        Assert.Null(usage.Weekly.ResetsAt);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Rejected_token_is_auth_error(HttpStatusCode status)
    {
        var e = await Assert.ThrowsAsync<ClaudeAuthException>(() => Create(Responding(status)).GetUsageAsync(Ct));

        Assert.Equal("Anthropic rejected the Claude Code token. Run `claude` to log in again.", e.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "Claude usage API returned HTTP 500")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Claude usage API returned HTTP 503")]
    [InlineData(HttpStatusCode.TooManyRequests, "Claude usage API rate limited the request")]
    public async Task Server_trouble_and_rate_limits_are_transient(HttpStatusCode status, string message)
    {
        var e = await Assert.ThrowsAsync<ClaudeTransientException>(() => Create(Responding(status)).GetUsageAsync(Ct));

        Assert.Equal(message, e.Message);
        Assert.Null(e.RetryAfter);
    }

    [Fact]
    public async Task Rate_limit_honours_retry_after()
    {
        var fake = Responding(HttpStatusCode.TooManyRequests, null, new Dictionary<string, string> { ["Retry-After"] = "90" });

        var e = await Assert.ThrowsAsync<ClaudeTransientException>(() => Create(fake).GetUsageAsync(Ct));

        Assert.Equal(TimeSpan.FromSeconds(90), e.RetryAfter);
    }

    [Theory]
    [InlineData("999999999", 3600)]
    [InlineData("-5", 0)]
    [InlineData("0", 0)]
    [InlineData("90", 90)]
    public async Task Retry_after_is_clamped(string header, double seconds)
    {
        var fake = Responding(HttpStatusCode.TooManyRequests, null, new Dictionary<string, string> { ["Retry-After"] = header });

        var e = await Assert.ThrowsAsync<ClaudeTransientException>(() => Create(fake).GetUsageAsync(Ct));

        Assert.Equal(TimeSpan.FromSeconds(seconds), e.RetryAfter);
    }

    [Theory]
    [InlineData("soon")]
    [InlineData("Wed, 21 Oct 2026 07:28:00 GMT")]
    [InlineData("1.5")]
    public async Task Non_integer_retry_after_is_ignored(string header)
    {
        var fake = Responding(HttpStatusCode.TooManyRequests, null, new Dictionary<string, string> { ["Retry-After"] = header });

        var e = await Assert.ThrowsAsync<ClaudeTransientException>(() => Create(fake).GetUsageAsync(Ct));

        Assert.Null(e.RetryAfter);
    }

    [Fact]
    public async Task Other_client_errors_are_fatal_but_neither_subclass()
    {
        var e = await Assert.ThrowsAnyAsync<ClaudeException>(() => Create(Responding(HttpStatusCode.NotFound)).GetUsageAsync(Ct));

        Assert.IsNotType<ClaudeAuthException>(e);
        Assert.IsNotType<ClaudeTransientException>(e);
        Assert.IsNotAssignableFrom<ITransientFailure>(e);
        Assert.Equal("Claude usage API returned HTTP 404", e.Message);
    }

    [Fact]
    public async Task Connection_error_is_transient()
    {
        var fake = new FakeHttpHandler();
        fake.Throw(HttpMethod.Get, Path, new HttpRequestException(Secret));

        var e = await Assert.ThrowsAsync<ClaudeTransientException>(() => Create(fake).GetUsageAsync(Ct));

        Assert.Equal("Claude usage request failed: HttpRequestException", e.Message);
    }

    [Fact]
    public async Task Timeout_is_transient()
    {
        var fake = new FakeHttpHandler();
        fake.Throw(HttpMethod.Get, Path, new TaskCanceledException());

        var e = await Assert.ThrowsAsync<ClaudeTransientException>(() => Create(fake).GetUsageAsync(Ct));

        Assert.StartsWith("Claude usage request failed:", e.Message);
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Create(Responding(HttpStatusCode.OK, Payload)).GetUsageAsync(cts.Token));
    }

    [Fact]
    public async Task Non_json_body_is_transient()
    {
        var e = await Assert.ThrowsAsync<ClaudeTransientException>(
            () => Create(Responding(HttpStatusCode.OK, "<html>nope</html>")).GetUsageAsync(Ct));

        Assert.Equal("Claude usage API returned an unexpected response", e.Message);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("""{"five_hour":{}}""")]
    [InlineData("""{"five_hour":{"utilization":1},"seven_day":null}""")]
    [InlineData("""{"five_hour":{"utilization":"x"},"seven_day":{"utilization":2}}""")]
    [InlineData("""{"five_hour":{"utilization":true},"seven_day":{"utilization":2}}""")]
    [InlineData("""{"five_hour":{"utilization":1,"resets_at":"2026-11-12T20:29:59"},"seven_day":{"utilization":2}}""")]
    [InlineData("""{"five_hour":{"utilization":1,"resets_at":5},"seven_day":{"utilization":2}}""")]
    [InlineData("""{"five_hour":{"utilization":1,"resets_at":"garbage"},"seven_day":{"utilization":2}}""")]
    public async Task Malformed_shape_is_transient(string json)
    {
        var e = await Assert.ThrowsAsync<ClaudeTransientException>(
            () => Create(Responding(HttpStatusCode.OK, json)).GetUsageAsync(Ct));

        Assert.Equal("Claude usage API returned an unexpected response", e.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.OK)]
    public async Task Errors_never_contain_the_token(HttpStatusCode status)
    {
        var fake = Responding(status, $$"""{"leak":"{{Secret}}"}""");

        var e = await Assert.ThrowsAnyAsync<ClaudeException>(() => Create(fake).GetUsageAsync(Ct));

        Assert.DoesNotContain(Secret, e.ToString());
    }

    [Fact]
    public async Task Token_is_resolved_on_every_call()
    {
        var tokens = new CountingTokens(Secret);
        var usage = Create(Responding(HttpStatusCode.OK, Payload), tokens);

        await usage.GetUsageAsync(Ct);
        await usage.GetUsageAsync(Ct);

        Assert.Equal(2, tokens.Calls);
    }
}
