namespace Busynet.Bkk.Departures;

/// <summary>
/// <paramref name="ResponseTimeout"/> bounds the whole request, headers and body. <paramref name="MinutesAfter"/>
/// is wide enough to find a few departures at night; <paramref name="Limit"/> caps the list the server returns.
/// </summary>
public sealed record FutarOptions(TimeSpan ResponseTimeout, int MinutesAfter, int Limit)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    public static FutarOptions Default => new(DefaultTimeout, 180, 10);
}
