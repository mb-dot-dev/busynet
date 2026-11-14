using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Busynet.GitHub.Credentials;
using Refit;

namespace Busynet.GitHub.Actions;

/// <summary>Classifies every failure as fatal or transient; no message ever includes the token or a body.</summary>
internal sealed class GitHubActionsClient(IGitHubTokenSource tokens, IGitHubApi api, GitHubActionsOptions options)
    : IGitHubActions
{
    private const double MaxRetryAfterSeconds = 3600;

    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private string? _token;

    public async Task<Workflow> ResolveWorkflowAsync(GitHubRepo repo, string reference, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(reference);

        var root = await GetAsync("workflows", repo, (token, ct) => api.GetWorkflowsAsync(token, repo.Owner, repo.Name, ct), ct);
        if (root is not JsonObject { } obj || obj["workflows"] is not JsonArray items)
            throw Unexpected("workflows", repo);
        var workflows = items.Select(item => ParseWorkflow(item) ?? throw Unexpected("workflows", repo)).ToList();

        // GitHub's own runs endpoint accepts only an id or a file name; the display name is resolved here too.
        return workflows.FirstOrDefault(w =>
                   reference == w.Id.ToString(CultureInfo.InvariantCulture) || reference == FileName(w.Path))
               ?? workflows.FirstOrDefault(w => reference == w.Name)
               ?? throw new GitHubException(
                   $"No workflow matches '{reference}' in {repo.Slug}. Available: {Available(workflows)}");
    }

    public async Task<WorkflowRun?> GetLatestRunAsync(GitHubRepo repo, long workflowId, string branch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(branch);

        var root = await GetAsync("runs", repo,
            (token, ct) => api.GetRunsAsync(token, repo.Owner, repo.Name, workflowId, branch, ct), ct);
        if (root is not JsonObject { } obj || obj["workflow_runs"] is not JsonArray runs)
            throw Unexpected("runs", repo);
        return runs.Count == 0 ? null : ParseRun(runs[0]) ?? throw Unexpected("runs", repo);
    }

    public async Task<int?> GetPullRequestNumberAsync(GitHubRepo repo, string branch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(branch);

        var root = await GetAsync("pulls", repo,
            (token, ct) => api.GetPullRequestsAsync(token, repo.Owner, repo.Name, $"{repo.Owner}:{branch}", ct), ct);
        if (root is not JsonArray pulls)
            throw Unexpected("pulls", repo);
        if (pulls.Count == 0)
            return null;
        return pulls[0] is JsonObject pull && Number(pull["number"]) is { } number and >= 0 and <= int.MaxValue
            ? (int)number
            : throw Unexpected("pulls", repo);
    }

    private static Workflow? ParseWorkflow(JsonNode? node) =>
        node is JsonObject obj && Number(obj["id"]) is { } id && Text(obj["name"]) is { } name && Text(obj["path"]) is { } path
            ? new Workflow(id, name, path)
            : null;

    // `conclusion` is null until the run completes.
    private static WorkflowRun? ParseRun(JsonNode? node) =>
        node is JsonObject obj && Number(obj["id"]) is { } id && Text(obj["status"]) is { } status
        && (obj["conclusion"] is null || Text(obj["conclusion"]) is not null)
            ? new WorkflowRun(id, status, Text(obj["conclusion"]))
            : null;

    private static long? Number(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<long>(out var n) ? n : null;

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

    private static string FileName(string path) => path[(path.LastIndexOf('/') + 1)..];

    private static string Available(IEnumerable<Workflow> workflows)
    {
        var names = workflows.Select(w => w.Name).Order(StringComparer.Ordinal).ToList();
        return names.Count == 0 ? "none" : string.Join(", ", names);
    }

    // A body of the wrong shape (schema drift, a proxy's error page) is more often an outage artifact than permanent.
    private static GitHubTransientException Unexpected(string what, GitHubRepo repo) =>
        new($"GitHub returned a {what} payload of an unexpected shape for {repo.Slug}");

    private async Task<JsonNode?> GetAsync(
        string what,
        GitHubRepo repo,
        Func<string, CancellationToken, Task<HttpResponseMessage>> call,
        CancellationToken ct)
    {
        var token = await GetTokenAsync(ct).ConfigureAwait(false);
        // Refit returns once the headers arrive, so HttpClient.Timeout does not bound the body read.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(options.ResponseTimeout);
        try
        {
            using var response = await call(token, deadline.Token).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            // 403 means both "rejected" and "rate limited" on GitHub; only rate-limit evidence makes it transient.
            if (status == 429 || (status == 403 && IsRateLimited(response)))
                throw new GitHubTransientException(
                    $"GitHub rate-limited the {what} request for {repo.Slug} (HTTP {status})", ParseRetryAfter(response));
            if (status is 401 or 403)
                throw new GitHubAuthException(
                    $"GitHub rejected the token (HTTP {status}). Run `gh auth login`, or check GITHUB_TOKEN's scopes.");
            if (status >= 500)
                throw new GitHubTransientException($"GitHub returned HTTP {status} for the {what} of {repo.Slug}");
            if (status >= 400)
                throw new GitHubException($"GitHub returned HTTP {status} for the {what} of {repo.Slug}");

            var body = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);
            return JsonNode.Parse(body);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            // Not JSON, or an invalid charset; the message is not forwarded.
            throw new GitHubTransientException($"GitHub returned an unreadable {what} response for {repo.Slug}");
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or ApiRequestException)
        {
            // Refit wraps transport failures and timeouts in ApiRequestException: report the inner type only.
            ct.ThrowIfCancellationRequested();
            var cause = e is ApiRequestException { InnerException: { } inner } ? inner : e;
            throw new GitHubTransientException($"GitHub {what} request failed: {cause.GetType().Name}");
        }
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _token) is { } cached)
            return cached;

        await _tokenGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _token ??= await tokens.GetTokenAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    private static bool IsRateLimited(HttpResponseMessage response) =>
        response.Headers.Contains("Retry-After")
        || (response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) && remaining.FirstOrDefault() == "0");

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Retry-After", out var values))
            return null;
        var raw = values.FirstOrDefault()?.Trim();
        if (!long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var seconds))
            return null;
        return TimeSpan.FromSeconds(Math.Clamp(seconds, 0, MaxRetryAfterSeconds));
    }
}
