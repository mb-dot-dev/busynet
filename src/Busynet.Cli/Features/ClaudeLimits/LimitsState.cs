using Busynet.Bar.Layouts;
using Busynet.Claude.Usage;

namespace Busynet.Cli.Features.ClaudeLimits;

/// <summary>The last fetched usage and which of its limits is shown.</summary>
public sealed record LimitsState(Usage? Usage, LimitKind Kind)
{
    public LimitsState Toggle() =>
        this with { Kind = Kind == LimitKind.Session ? LimitKind.Weekly : LimitKind.Session };

    public LimitsState WithUsage(Usage usage) => this with { Usage = usage };

    /// <summary>The selected limit as drawn, or <c>null</c> before any usage is known.</summary>
    public LimitGauge? Render(DateTimeOffset now)
    {
        if (Usage is null)
            return null;

        var window = Kind == LimitKind.Session ? Usage.Session : Usage.Weekly;
        return LimitGauge.From(LimitLabels.Format(Kind, window, now), window.Utilization);
    }
}
