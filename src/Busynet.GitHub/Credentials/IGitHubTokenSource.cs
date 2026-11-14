namespace Busynet.GitHub.Credentials;

public interface IGitHubTokenSource
{
    /// <summary>The <c>gh</c> CLI's token (<c>gh auth token</c>), else <c>$GITHUB_TOKEN</c>.</summary>
    /// <exception cref="GitHubAuthException">Neither is available.</exception>
    Task<string> GetTokenAsync(CancellationToken cancellationToken);
}
