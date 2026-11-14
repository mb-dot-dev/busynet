using Busynet.Core;

namespace Busynet.GitHub;

/// <summary>A failure talking to GitHub or finding a token. Messages never contain a token.</summary>
public class GitHubException(string message) : BusynetException(message);

/// <summary>No token is available, or GitHub rejected it; never self-heals.</summary>
public sealed class GitHubAuthException(string message) : GitHubException(message);

/// <summary>A failure worth retrying: 5xx, rate limiting, transport failures and malformed bodies.</summary>
public sealed class GitHubTransientException(string message, TimeSpan? retryAfter = null)
    : GitHubException(message), ITransientFailure
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>Inspecting the local git checkout failed (not a repository, no origin, detached HEAD...).</summary>
public sealed class GitException(string message) : BusynetException(message);
