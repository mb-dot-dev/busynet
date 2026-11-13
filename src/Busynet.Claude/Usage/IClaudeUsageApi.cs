using Refit;

namespace Busynet.Claude.Usage;

/// <summary>The undocumented endpoint Claude Code itself calls, so its identity is mirrored.</summary>
public interface IClaudeUsageApi
{
    [Get("/api/oauth/usage")]
    [Headers("anthropic-beta: oauth-2025-04-20", "User-Agent: claude-code/2.1.220")]
    Task<HttpResponseMessage> GetUsageAsync([Authorize("Bearer")] string token, CancellationToken ct);
}
