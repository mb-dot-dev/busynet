namespace Busynet.Bar.Layouts;

/// <summary>
/// One limit as drawn: compared by value, so equal gauges mean equal payloads. <c>MarkerOffset</c> is the
/// pixel within the track where the elapsed share of the time window has reached (null: no marker).
/// </summary>
public sealed record LimitGauge(string Label, int FillWidth, bool High, int? MarkerOffset = null)
{
    public const int TrackWidth = 70;
    public const double HighThreshold = 85;

    /// <param name="timeProgress">The elapsed share of the limit's time window; clamped to 0-1.</param>
    public static LimitGauge From(string label, double percent, double? timeProgress = null) =>
        new(
            label,
            Math.Clamp((int)Math.Round(percent / 100 * TrackWidth), 0, TrackWidth),
            percent > HighThreshold,
            timeProgress is { } progress
                ? Math.Clamp((int)Math.Round(progress * TrackWidth), 0, TrackWidth - 1)
                : null);
}
