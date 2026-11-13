using Busynet.Bar.Display;
namespace Busynet.Bar.Layouts;

public static class LimitsLayout
{
    private const string TrackColor = "#1F1F1FFF";
    private const string NormalColor = "#CD6E58FF";
    private const string HighColor = "#B3261EFF";
    private const string LabelColor = "#E6E6E6FF";

    public static DisplayElements Build(LimitGauge gauge)
    {
        ArgumentNullException.ThrowIfNull(gauge);

        var fillColor = gauge.FillWidth == 0 ? TrackColor : gauge.High ? HighColor : NormalColor;

        return new DisplayElements(
            BarApplication.Name,
            [
                new RectangleElement("limit-track", 1, 8, LimitGauge.TrackWidth, 8, [BarColor.Normalize(TrackColor)]),
                new RectangleElement("limit-fill", 1, 8, Math.Max(gauge.FillWidth, 1), 8, [BarColor.Normalize(fillColor)]),
                new TextElement("limit-label", DisplayText.ToDisplayableAscii(gauge.Label), "condensed", 1, -2, LabelColor),
            ]);
    }
}
