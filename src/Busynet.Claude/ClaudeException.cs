using Busynet.Core;

namespace Busynet.Claude;

/// <summary>A failure talking to Claude or reading its credentials. Messages never contain a token.</summary>
public class ClaudeException(string message) : BusynetException(message);

/// <summary>The user must log in to Claude Code again.</summary>
public sealed class ClaudeAuthException(string message) : ClaudeException(message);

/// <summary>A failure worth retrying.</summary>
public sealed class ClaudeTransientException(string message, TimeSpan? retryAfter = null)
    : ClaudeException(message), ITransientFailure
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
