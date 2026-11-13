namespace Busynet.Claude.Usage;

/// <summary>Bounds the whole usage request, headers and body; HttpClient.Timeout only covers the headers.</summary>
public sealed record ClaudeUsageOptions(TimeSpan ResponseTimeout)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    public static ClaudeUsageOptions Default => new(DefaultTimeout);
}
