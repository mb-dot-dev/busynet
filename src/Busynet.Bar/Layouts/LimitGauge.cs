namespace Busynet.Bar.Layouts;

/// <summary>One limit as drawn: compared by value, so equal gauges mean equal payloads.</summary>
public sealed record LimitGauge(string Label, int FillWidth, bool High)
{
    public const int TrackWidth = 70;
    public const double HighThreshold = 85;

    public static LimitGauge From(string label, double percent) =>
        new(label, Math.Clamp((int)Math.Round(percent / 100 * TrackWidth), 0, TrackWidth), percent > HighThreshold);
}
