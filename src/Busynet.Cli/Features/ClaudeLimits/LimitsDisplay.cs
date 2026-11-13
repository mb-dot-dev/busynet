using Busynet.Bar;
using Busynet.Bar.Display;
using Busynet.Bar.Http;
using Busynet.Bar.Layouts;
using Busynet.Core.Watching;

namespace Busynet.Cli.Features.ClaudeLimits;

/// <summary>Draws one limit gauge on the bar.</summary>
public sealed class LimitsDisplay(IBarDisplay bar) : IDisplay<LimitGauge>
{
    public Task DrawAsync(LimitGauge view, CancellationToken cancellationToken) =>
        bar.DrawAsync(LimitsLayout.Build(view), cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken) =>
        bar.ClearAsync(cancellationToken);
}
