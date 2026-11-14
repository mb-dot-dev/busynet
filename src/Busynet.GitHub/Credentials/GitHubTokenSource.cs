using Busynet.Core.Processes;

namespace Busynet.GitHub.Credentials;

/// <summary>Reads (never caches or writes) a GitHub token: the <c>gh</c> CLI first, then the environment.</summary>
public sealed class GitHubTokenSource(IProcessRunner runner, Func<string, string?> environment) : IGitHubTokenSource
{
    public const string TokenVariable = "GITHUB_TOKEN";

    public const string NoToken =
        "No GitHub token available. Install the gh CLI and run `gh auth login`, or set GITHUB_TOKEN.";

    private static readonly TimeSpan GhTimeout = TimeSpan.FromSeconds(10);

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        var token = await FromGhAsync(cancellationToken) ?? environment(TokenVariable)?.Trim();
        return string.IsNullOrEmpty(token) ? throw new GitHubAuthException(NoToken) : token;
    }

    // gh missing, not logged in or hanging must not block the GITHUB_TOKEN fallback. Its stderr (a diagnostic
    // about the login state) is discarded by the runner, so no token can reach a message.
    private async Task<string?> FromGhAsync(CancellationToken ct)
    {
        try
        {
            var outcome = await runner.RunAsync("gh", ["auth", "token"], GhTimeout, ct);
            var token = outcome.StandardOutput.Trim();
            return outcome.ExitCode == 0 && token.Length > 0 ? token : null;
        }
        catch (ProcessRunFailedException)
        {
            return null;
        }
    }
}
