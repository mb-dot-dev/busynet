using Refit;

namespace Busynet.GitHub.Actions;

/// <summary>The GitHub REST endpoints the workflow watch reads. Responses are parsed by <see cref="GitHubActionsClient"/>.</summary>
[Headers("Accept: application/vnd.github+json", "X-GitHub-Api-Version: 2022-11-28", "User-Agent: busynet")]
public interface IGitHubApi
{
    [Get("/repos/{owner}/{repo}/actions/workflows?per_page=100")]
    Task<HttpResponseMessage> GetWorkflowsAsync(
        [Authorize("Bearer")] string token, string owner, string repo, CancellationToken ct);

    [Get("/repos/{owner}/{repo}/actions/workflows/{workflowId}/runs?per_page=1")]
    Task<HttpResponseMessage> GetRunsAsync(
        [Authorize("Bearer")] string token, string owner, string repo, long workflowId, string branch, CancellationToken ct);

    [Get("/repos/{owner}/{repo}/pulls?state=open&per_page=1")]
    Task<HttpResponseMessage> GetPullRequestsAsync(
        [Authorize("Bearer")] string token, string owner, string repo, string head, CancellationToken ct);
}
