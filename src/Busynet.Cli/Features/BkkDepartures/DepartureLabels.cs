using System.Globalization;
using Busynet.Bkk.Departures;

namespace Busynet.Cli.Features.BkkDepartures;

/// <summary>The header row of a departure frame: <c>1. 922 17'</c>, <c>2. 940 ~9'</c>, <c>3. 988 00:04</c>.</summary>
public static class DepartureLabels
{
    private const int ClockFromMinutes = 60;

    /// <summary>
    /// Whole minutes until <see cref="Departure.When"/> (<c>17'</c>, <c>0'</c> when due), or its <c>HH:mm</c> in
    /// <paramref name="zone"/> once it is an hour or more away. A departure without a live prediction gets a
    /// <c>~</c> prefix.
    /// </summary>
    public static string Time(Departure departure, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(departure);
        ArgumentNullException.ThrowIfNull(zone);

        var minutes = (int)Math.Floor((departure.When - now).TotalMinutes);
        var time = minutes < ClockFromMinutes
            ? $"{Math.Max(minutes, 0)}'"
            : TimeZoneInfo.ConvertTime(departure.When, zone).ToString("HH:mm", CultureInfo.InvariantCulture);
        return departure.IsLive ? time : $"~{time}";
    }

    public static string Header(int position, Departure departure, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(departure);

        return $"{position}. {departure.Line} {Time(departure, now, zone)}";
    }
}
