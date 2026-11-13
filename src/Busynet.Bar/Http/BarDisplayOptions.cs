namespace Busynet.Bar.Http;

/// <summary>Bounds the whole bar request, headers and error body; HttpClient.Timeout only covers the headers.</summary>
public sealed record BarDisplayOptions(TimeSpan ResponseTimeout)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    public static BarDisplayOptions Default => new(DefaultTimeout);
}
