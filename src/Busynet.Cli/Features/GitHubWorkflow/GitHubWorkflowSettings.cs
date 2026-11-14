using Busynet.Cli.Infrastructure;
using Busynet.GitHub;

namespace Busynet.Cli.Features.GitHubWorkflow;

/// <param name="Workflow">A workflow id, file name or display name.</param>
/// <param name="Branch">The branch to watch; null for the current checkout's.</param>
/// <param name="Repo">The repository; null for the origin remote's.</param>
public sealed record GitHubWorkflowSettings(
    string? Host,
    string? Token,
    bool Verbose,
    string Workflow,
    string? Branch,
    GitHubRepo? Repo,
    int Interval)
    : BarSettings(Host, Token, Verbose)
{
    public const int DefaultInterval = 10;
}
