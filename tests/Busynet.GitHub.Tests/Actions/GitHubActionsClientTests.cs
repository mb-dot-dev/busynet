using System.Net;
using Busynet.Core;
using Busynet.GitHub.Actions;
using Busynet.GitHub.Credentials;
using Busynet.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Busynet.GitHub.Tests.Actions;

public sealed class GitHubActionsClientTests
{
    private const string Secret = "gho_SECRET";
    private const string WorkflowsPath = "/repos/mb-dot-dev/busynet/actions/workflows";
    private const string RunsPath = "/repos/mb-dot-dev/busynet/actions/workflows/161335/runs";
    private const string PullsPath = "/repos/mb-dot-dev/busynet/pulls";

    private static readonly GitHubRepo Repo = new("mb-dot-dev", "busynet");

    private const string Workflows = """
        {"total_count": 2, "workflows": [
          {"id": 161335, "node_id": "x", "name": "CI", "path": ".github/workflows/ci.yaml", "state": "active"},
          {"id": 161336, "name": "Release", "path": ".github/workflows/release.yaml"}
        ]}
        """;

    private sealed class CountingTokens(string token) : IGitHubTokenSource
    {
        public int Calls { get; private set; }

        public Task<string> GetTokenAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(token);
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IGitHubActions Create(FakeHttpHandler fake, IGitHubTokenSource? tokens = null, TimeSpan? timeout = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(tokens ?? new CountingTokens(Secret));
        if (timeout is { } t)
            services.AddSingleton(new GitHubActionsOptions(t));
        services.AddGitHub();
        services.AddHttpClient("github").ConfigurePrimaryHttpMessageHandler(() => fake);
        return services.BuildServiceProvider().GetRequiredService<IGitHubActions>();
    }

    private static FakeHttpHandler Responding(string path, HttpStatusCode status, string? json = null,
        IDictionary<string, string>? headers = null)
    {
        var fake = new FakeHttpHandler();
        fake.Add(HttpMethod.Get, path, status, json, headers);
        return fake;
    }

    [Theory]
    [InlineData("161335")]
    [InlineData("ci.yaml")]
    [InlineData("CI")]
    public async Task Resolves_a_workflow_by_id_file_name_or_display_name(string reference)
    {
        var workflow = await Create(Responding(WorkflowsPath, HttpStatusCode.OK, Workflows))
            .ResolveWorkflowAsync(Repo, reference, Ct);

        Assert.Equal(new Workflow(161335, "CI", ".github/workflows/ci.yaml"), workflow);
    }

    [Fact]
    public async Task Id_and_file_name_win_over_a_display_name()
    {
        const string json = """
            {"workflows": [
              {"id": 1, "name": "release.yaml", "path": ".github/workflows/a.yaml"},
              {"id": 2, "name": "Release", "path": ".github/workflows/release.yaml"}
            ]}
            """;

        var workflow = await Create(Responding(WorkflowsPath, HttpStatusCode.OK, json)).ResolveWorkflowAsync(Repo, "release.yaml", Ct);

        Assert.Equal(2, workflow.Id);
    }

    [Fact]
    public async Task No_match_lists_the_available_workflows()
    {
        var e = await Assert.ThrowsAsync<GitHubException>(() =>
            Create(Responding(WorkflowsPath, HttpStatusCode.OK, Workflows)).ResolveWorkflowAsync(Repo, "nightly", Ct));

        Assert.Equal("No workflow matches 'nightly' in mb-dot-dev/busynet. Available: CI, Release", e.Message);
        Assert.IsNotAssignableFrom<ITransientFailure>(e);
    }

    [Fact]
    public async Task No_workflows_at_all_says_none()
    {
        var e = await Assert.ThrowsAsync<GitHubException>(() =>
            Create(Responding(WorkflowsPath, HttpStatusCode.OK, """{"workflows":[]}""")).ResolveWorkflowAsync(Repo, "CI", Ct));

        Assert.EndsWith("Available: none", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sends_the_api_headers_and_bearer_token()
    {
        var fake = Responding(WorkflowsPath, HttpStatusCode.OK, Workflows);

        await Create(fake).ResolveWorkflowAsync(Repo, "CI", Ct);

        var call = Assert.Single(fake.Calls);
        Assert.Equal("api.github.com", call.Uri.Host);
        Assert.Contains("per_page=100", call.Uri.Query, StringComparison.Ordinal);
        Assert.Equal($"Bearer {Secret}", call.Headers["Authorization"]);
        Assert.Equal("application/vnd.github+json", call.Headers["Accept"]);
        Assert.Equal("2022-11-28", call.Headers["X-GitHub-Api-Version"]);
        Assert.Equal("busynet", call.Headers["User-Agent"]);
    }

    [Fact]
    public async Task Latest_run_queries_the_branch()
    {
        var fake = Responding(RunsPath, HttpStatusCode.OK,
            """{"workflow_runs":[{"id":7,"status":"completed","conclusion":"success","head_branch":"feature/x"}]}""");

        var run = await Create(fake).GetLatestRunAsync(Repo, 161335, "feature/x", Ct);

        Assert.Equal(new WorkflowRun(7, "completed", "success"), run);
        var query = Uri.UnescapeDataString(Assert.Single(fake.Calls).Uri.Query);
        Assert.Contains("branch=feature/x", query, StringComparison.Ordinal);
        Assert.Contains("per_page=1", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Running_run_has_no_conclusion()
    {
        var run = await Create(Responding(RunsPath, HttpStatusCode.OK,
                """{"workflow_runs":[{"id":7,"status":"in_progress","conclusion":null}]}"""))
            .GetLatestRunAsync(Repo, 161335, "main", Ct);

        Assert.Equal(new WorkflowRun(7, "in_progress", null), run);
    }

    [Fact]
    public async Task No_runs_is_null()
    {
        var run = await Create(Responding(RunsPath, HttpStatusCode.OK, """{"total_count":0,"workflow_runs":[]}"""))
            .GetLatestRunAsync(Repo, 161335, "main", Ct);

        Assert.Null(run);
    }

    [Fact]
    public async Task Pull_request_is_looked_up_by_owner_scoped_head()
    {
        var fake = Responding(PullsPath, HttpStatusCode.OK, """[{"number":12,"state":"open"}]""");

        var number = await Create(fake).GetPullRequestNumberAsync(Repo, "feature/x", Ct);

        Assert.Equal(12, number);
        var query = Uri.UnescapeDataString(Assert.Single(fake.Calls).Uri.Query);
        Assert.Contains("head=mb-dot-dev:feature/x", query, StringComparison.Ordinal);
        Assert.Contains("state=open", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_pull_request_is_null()
    {
        Assert.Null(await Create(Responding(PullsPath, HttpStatusCode.OK, "[]")).GetPullRequestNumberAsync(Repo, "main", Ct));
    }

    [Fact]
    public async Task Token_is_resolved_once()
    {
        var tokens = new CountingTokens(Secret);
        var fake = new FakeHttpHandler();
        fake.Add(HttpMethod.Get, RunsPath, HttpStatusCode.OK, """{"workflow_runs":[]}""");
        fake.Add(HttpMethod.Get, PullsPath, HttpStatusCode.OK, "[]");
        var actions = Create(fake, tokens);

        await actions.GetLatestRunAsync(Repo, 161335, "main", Ct);
        await actions.GetPullRequestNumberAsync(Repo, "main", Ct);
        await actions.GetLatestRunAsync(Repo, 161335, "main", Ct);

        Assert.Equal(1, tokens.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Rejected_token_is_an_auth_error(HttpStatusCode status)
    {
        var e = await Assert.ThrowsAsync<GitHubAuthException>(() =>
            Create(Responding(RunsPath, status)).GetLatestRunAsync(Repo, 161335, "main", Ct));

        Assert.StartsWith($"GitHub rejected the token (HTTP {(int)status}).", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Forbidden_with_no_requests_left_is_a_transient_rate_limit()
    {
        var fake = Responding(RunsPath, HttpStatusCode.Forbidden, null,
            new Dictionary<string, string> { ["x-ratelimit-remaining"] = "0" });

        var e = await Assert.ThrowsAsync<GitHubTransientException>(() => Create(fake).GetLatestRunAsync(Repo, 161335, "main", Ct));

        Assert.Null(e.RetryAfter);
        Assert.Contains("rate-limited", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Rate_limit_honours_retry_after(HttpStatusCode status)
    {
        var fake = Responding(RunsPath, status, null, new Dictionary<string, string> { ["Retry-After"] = "60" });

        var e = await Assert.ThrowsAsync<GitHubTransientException>(() => Create(fake).GetLatestRunAsync(Repo, 161335, "main", Ct));

        Assert.Equal(TimeSpan.FromSeconds(60), e.RetryAfter);
    }

    [Theory]
    [InlineData("999999999", 3600)]
    [InlineData("-5", 0)]
    public async Task Retry_after_is_clamped(string header, double seconds)
    {
        var fake = Responding(RunsPath, HttpStatusCode.TooManyRequests, null, new Dictionary<string, string> { ["Retry-After"] = header });

        var e = await Assert.ThrowsAsync<GitHubTransientException>(() => Create(fake).GetLatestRunAsync(Repo, 161335, "main", Ct));

        Assert.Equal(TimeSpan.FromSeconds(seconds), e.RetryAfter);
    }

    [Fact]
    public async Task Unparseable_retry_after_is_ignored()
    {
        var fake = Responding(RunsPath, HttpStatusCode.TooManyRequests, null,
            new Dictionary<string, string> { ["Retry-After"] = "Wed, 21 Oct 2026 07:28:00 GMT" });

        var e = await Assert.ThrowsAsync<GitHubTransientException>(() => Create(fake).GetLatestRunAsync(Repo, 161335, "main", Ct));

        Assert.Null(e.RetryAfter);
    }

    [Fact]
    public async Task Server_error_is_transient()
    {
        var e = await Assert.ThrowsAsync<GitHubTransientException>(() =>
            Create(Responding(RunsPath, HttpStatusCode.BadGateway)).GetLatestRunAsync(Repo, 161335, "main", Ct));

        Assert.Equal("GitHub returned HTTP 502 for the runs of mb-dot-dev/busynet", e.Message);
    }

    [Fact]
    public async Task Missing_repository_is_fatal()
    {
        var e = await Assert.ThrowsAnyAsync<GitHubException>(() =>
            Create(Responding(WorkflowsPath, HttpStatusCode.NotFound)).ResolveWorkflowAsync(Repo, "CI", Ct));

        Assert.IsNotAssignableFrom<ITransientFailure>(e);
        Assert.IsNotType<GitHubAuthException>(e);
        Assert.Equal("GitHub returned HTTP 404 for the workflows of mb-dot-dev/busynet", e.Message);
    }

    [Fact]
    public async Task Connection_error_is_transient_and_names_only_the_type()
    {
        var fake = new FakeHttpHandler();
        fake.Throw(HttpMethod.Get, RunsPath, new HttpRequestException(Secret));

        var e = await Assert.ThrowsAsync<GitHubTransientException>(() => Create(fake).GetLatestRunAsync(Repo, 161335, "main", Ct));

        Assert.Equal("GitHub runs request failed: HttpRequestException", e.Message);
    }

    [Fact]
    public async Task Body_that_never_arrives_times_out_as_transient()
    {
        var fake = new FakeHttpHandler();
        fake.Add(HttpMethod.Get, RunsPath, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = HangingContent.Create() });

        var e = await Assert.ThrowsAsync<GitHubTransientException>(() =>
            Create(fake, timeout: TimeSpan.FromMilliseconds(200)).GetLatestRunAsync(Repo, 161335, "main", Ct));

        Assert.Equal("GitHub runs request failed: TaskCanceledException", e.Message);
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Create(Responding(RunsPath, HttpStatusCode.OK, """{"workflow_runs":[]}""")).GetLatestRunAsync(Repo, 161335, "main", cts.Token));
    }

    [Theory]
    [InlineData("<html>proxy error</html>")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"workflow_runs":{}}""")]
    [InlineData("""{"workflow_runs":[{"id":"7","status":"queued"}]}""")]
    [InlineData("""{"workflow_runs":[{"id":7}]}""")]
    [InlineData("""{"workflow_runs":[{"id":7,"status":"completed","conclusion":5}]}""")]
    [InlineData("""{"workflow_runs":[null]}""")]
    public async Task Malformed_runs_are_transient(string json)
    {
        await Assert.ThrowsAsync<GitHubTransientException>(() =>
            Create(Responding(RunsPath, HttpStatusCode.OK, json)).GetLatestRunAsync(Repo, 161335, "main", Ct));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""[{"number":"12"}]""")]
    [InlineData("""[{"number":-1}]""")]
    [InlineData("[1]")]
    public async Task Malformed_pulls_are_transient(string json)
    {
        await Assert.ThrowsAsync<GitHubTransientException>(() =>
            Create(Responding(PullsPath, HttpStatusCode.OK, json)).GetPullRequestNumberAsync(Repo, "main", Ct));
    }

    [Theory]
    [InlineData("""{"workflows":null}""")]
    [InlineData("""{"workflows":[{"id":1,"name":"CI"}]}""")]
    public async Task Malformed_workflows_are_transient(string json)
    {
        await Assert.ThrowsAsync<GitHubTransientException>(() =>
            Create(Responding(WorkflowsPath, HttpStatusCode.OK, json)).ResolveWorkflowAsync(Repo, "CI", Ct));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.OK)]
    public async Task Errors_never_contain_the_token(HttpStatusCode status)
    {
        var fake = Responding(RunsPath, status, $$"""{"leak":"{{Secret}}"}""");

        var e = await Assert.ThrowsAnyAsync<GitHubException>(() => Create(fake).GetLatestRunAsync(Repo, 161335, "main", Ct));

        Assert.DoesNotContain(Secret, e.ToString(), StringComparison.Ordinal);
    }
}
