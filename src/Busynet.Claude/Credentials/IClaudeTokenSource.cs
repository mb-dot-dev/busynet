namespace Busynet.Claude.Credentials;

public interface IClaudeTokenSource
{
    /// <summary>Reads (never refreshes or writes) the Claude Code OAuth access token.</summary>
    /// <exception cref="ClaudeAuthException">No usable credentials; the user must log in.</exception>
    /// <exception cref="ClaudeTransientException">The keychain could not be read.</exception>
    Task<string> GetTokenAsync(CancellationToken cancellationToken);
}
