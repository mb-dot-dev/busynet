namespace Busynet.Claude.Usage;

public interface IClaudeUsage
{
    /// <summary>Resolves the token afresh, then fetches the usage limits.</summary>
    /// <exception cref="ClaudeAuthException">The user must log in again.</exception>
    /// <exception cref="ClaudeTransientException">Worth retrying on the next poll.</exception>
    Task<Usage> GetUsageAsync(CancellationToken ct);
}
