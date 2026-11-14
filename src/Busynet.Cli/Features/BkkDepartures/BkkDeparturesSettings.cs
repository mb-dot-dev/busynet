using Busynet.Cli.Infrastructure;

namespace Busynet.Cli.Features.BkkDepartures;

/// <param name="StopId">The BKK stop (platform) id, e.g. <c>BKK_F04808</c>.</param>
/// <param name="Interval">Seconds between API polls.</param>
/// <param name="Rotate">Seconds each departure is shown.</param>
public sealed record BkkDeparturesSettings(
    string? Host,
    string? Token,
    bool Verbose,
    string StopId,
    int Interval,
    int Rotate)
    : BarSettings(Host, Token, Verbose)
{
    /// <summary>Kötő utca, Budaörs, the platform towards Budapest.</summary>
    public const string DefaultStopId = "BKK_F04808";
    public const int DefaultInterval = 30;
    public const int DefaultRotate = 5;
    public const string ApiKeyVariable = "BKK_API_KEY";
}
