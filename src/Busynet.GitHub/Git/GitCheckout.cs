using System.Text.RegularExpressions;
using Busynet.Core.Processes;

namespace Busynet.GitHub.Git;

public sealed partial class GitCheckout(IProcessRunner runner) : IGitCheckout
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(10);

    public async Task<string> GetCurrentBranchAsync(CancellationToken cancellationToken)
    {
        var branch = await RunAsync(["rev-parse", "--abbrev-ref", "HEAD"], cancellationToken);
        return branch == "HEAD" ? throw new GitException("HEAD is detached; pass --branch explicitly.") : branch;
    }

    public async Task<GitHubRepo> GetOriginRepoAsync(CancellationToken cancellationToken) =>
        ParseRemoteUrl(await RunAsync(["remote", "get-url", "origin"], cancellationToken));

    /// <summary>
    /// The trailing <c>owner/name</c> of any remote URL form git accepts: <c>git@host:owner/name.git</c>,
    /// <c>https://host/owner/name</c>, <c>ssh://git@host/owner/name.git</c>.
    /// </summary>
    public static GitHubRepo ParseRemoteUrl(string url)
    {
        ArgumentNullException.ThrowIfNull(url);

        var match = Remote().Match(url.Trim());
        return match.Success
            ? new GitHubRepo(match.Groups["owner"].Value, match.Groups["name"].Value)
            : throw new GitException($"Cannot read an owner/name out of the remote URL: {UserInfo().Replace(url, "//<redacted>@")}");
    }

    private async Task<string> RunAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var command = "git " + string.Join(' ', arguments);
        ProcessOutcome outcome;
        try
        {
            outcome = await runner.RunAsync("git", arguments, GitTimeout, ct);
        }
        catch (ProcessRunFailedException e)
        {
            throw new GitException($"{command} failed: {e.Message}");
        }

        return outcome.ExitCode == 0
            ? outcome.StandardOutput.Trim()
            : throw new GitException($"{command} failed (exit {outcome.ExitCode}). Run it inside a git checkout with an origin remote, or pass --repo and --branch.");
    }

    [GeneratedRegex(@"[:/](?<owner>[^/:]+)/(?<name>[^/:]+?)(?:\.git)?/?$")]
    private static partial Regex Remote();

    // The userinfo of a URL (`https://user:token@host/...`): redacted before it can reach a message.
    [GeneratedRegex(@"//[^/@]*@")]
    private static partial Regex UserInfo();
}
