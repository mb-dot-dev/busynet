using Busynet.Cli.Infrastructure;

namespace Busynet.Cli.Features.BkkDepartures;

/// <param name="StopId">The BKK stop (platform) id, e.g. <c>BKK_F04720</c>.</param>
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
    /// <summary>Kisfaludy utca, Budaörs, the stop nearest to 47.460243, 18.954274.</summary>
    public const string DefaultStopId = "BKK_F04720";
    public const int DefaultInterval = 30;
    public const int DefaultRotate = 5;
    public const string ApiKeyVariable = "BKK_API_KEY";
}
