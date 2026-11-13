namespace Busynet.Core.Watching;

/// <summary>A target that shows one view at a time, e.g. the bar's display for this application.</summary>
public interface IDisplay<in TView>
{
    Task DrawAsync(TView view, CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}
