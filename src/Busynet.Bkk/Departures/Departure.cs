namespace Busynet.Bkk.Departures;

/// <summary>
/// One upcoming departure: the line's short name (<c>922</c>, <c>40E</c>), where it is headed, its timetable time
/// and, for a tracked vehicle, the live prediction.
/// </summary>
public sealed record Departure(string Line, string Headsign, DateTimeOffset Scheduled, DateTimeOffset? Predicted)
{
    /// <summary>The best known departure time: the prediction when there is one.</summary>
    public DateTimeOffset When => Predicted ?? Scheduled;

    public bool IsLive => Predicted is not null;
}

/// <summary>A stop's name and its departures, sorted by <see cref="Departure.When"/>, cancelled ones removed.</summary>
public sealed record StopDepartures(string StopName, IReadOnlyList<Departure> Departures);
