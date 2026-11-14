namespace Busynet.GitHub.Actions;

/// <summary>Bounds each whole GitHub request, headers and body; HttpClient.Timeout only covers the headers.</summary>
public sealed record GitHubActionsOptions(TimeSpan ResponseTimeout)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    public static GitHubActionsOptions Default => new(DefaultTimeout);
}
