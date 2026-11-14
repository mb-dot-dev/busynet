namespace Busynet.GitHub.Actions;

/// <summary>
/// Workflow queries. The token is resolved on the first call and reused: it does not change under a running watch.
/// Every failure is a <see cref="GitHubException"/>: <see cref="GitHubAuthException"/> and other 4xx are fatal,
/// <see cref="GitHubTransientException"/> is worth retrying.
/// </summary>
public interface IGitHubActions
{
    /// <summary>Finds a workflow by numeric id, file name (<c>ci.yaml</c>) or display name (<c>CI</c>).</summary>
    /// <exception cref="GitHubException">No workflow matches; the message lists the available ones.</exception>
    Task<Workflow> ResolveWorkflowAsync(GitHubRepo repo, string reference, CancellationToken ct);

    /// <summary>The most recent run of the workflow on the branch, or null when it never ran there.</summary>
    Task<WorkflowRun?> GetLatestRunAsync(GitHubRepo repo, long workflowId, string branch, CancellationToken ct);

    /// <summary>
    /// The number of the open pull request from <paramref name="branch"/>, or null. Scoped to the repository owner, so
    /// a pull request from a fork is not matched.
    /// </summary>
    Task<int?> GetPullRequestNumberAsync(GitHubRepo repo, string branch, CancellationToken ct);
}
