using Busynet.Bar.Display;
using Busynet.Bar.Layouts;
using Busynet.Bkk.Departures;

namespace Busynet.Cli.Features.BkkDepartures;

/// <summary>
/// The last polled departures (null before the first poll succeeds) and the rotation counter. The counter keeps
/// running across polls; it is taken modulo the number of departures shown at render time.
/// </summary>
public sealed record DeparturesState(StopDepartures? Snapshot, int Index)
{
    /// <summary>How many upcoming departures the rotation cycles through.</summary>
    public const int Shown = 3;

    private const string NoDepartures = "no departures";

    public DeparturesState WithSnapshot(StopDepartures snapshot) => this with { Snapshot = snapshot };

    public DeparturesState Advance() => this with { Index = Index + 1 };

    /// <summary>
    /// The current frame, or <c>null</c> before anything was polled. Departures that already left by
    /// <paramref name="now"/> drop out at once, so the numbering always starts at the next bus.
    /// </summary>
    public DepartureFrame? Render(DateTimeOffset now, TimeZoneInfo zone)
    {
        if (Snapshot is null)
            return null;

        var upcoming = Snapshot.Departures.Where(d => d.When >= now).Take(Shown).ToList();
        if (upcoming.Count == 0)
            return new DepartureFrame(NoDepartures, DisplayText.Transliterate(Snapshot.StopName));

        var i = Index % upcoming.Count;
        return new DepartureFrame(
            DepartureLabels.Header(i + 1, upcoming[i], now, zone),
            DisplayText.Transliterate(upcoming[i].Headsign));
    }
}
