namespace Busynet.GitHub.Git;

/// <summary>The git checkout in the current directory.</summary>
public interface IGitCheckout
{
    /// <summary>The branch HEAD is on.</summary>
    /// <exception cref="GitException">Not a repository, git missing, or HEAD detached.</exception>
    Task<string> GetCurrentBranchAsync(CancellationToken cancellationToken);

    /// <summary>The GitHub repository the <c>origin</c> remote points at.</summary>
    /// <exception cref="GitException">Not a repository, git missing, no origin, or an unreadable URL.</exception>
    Task<GitHubRepo> GetOriginRepoAsync(CancellationToken cancellationToken);
}
