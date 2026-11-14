using Busynet.GitHub;
using Busynet.GitHub.Actions;

namespace Busynet.Cli.Features.GitHubWorkflow;

/// <summary>
/// What is watched, resolved once at startup. The whole <see cref="Workflow"/> is kept so the id that is polled and
/// the name that is shown come from the same lookup.
/// </summary>
public sealed record WorkflowTarget(GitHubRepo Repo, string Branch, Workflow Workflow);

/// <summary>One poll's result: the latest run on the branch (null if none) and its open pull request (null if none).</summary>
public sealed record WorkflowSnapshot(WorkflowRun? Run, int? PullRequest);
