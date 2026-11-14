using Busynet.Bar.Http;
using Busynet.Bar.Layouts;
using Busynet.Core.Watching;

namespace Busynet.Cli.Features.BkkDepartures;

/// <summary>Draws one departure frame on the bar.</summary>
public sealed class DeparturesDisplay(IBarDisplay bar) : IDisplay<DepartureFrame>
{
    public Task DrawAsync(DepartureFrame view, CancellationToken cancellationToken) =>
        bar.DrawAsync(DeparturesLayout.Build(view), cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken) =>
        bar.ClearAsync(cancellationToken);
}
