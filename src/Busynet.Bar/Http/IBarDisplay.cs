using Busynet.Bar.Display;

namespace Busynet.Bar.Http;

public interface IBarDisplay
{
    Task DrawAsync(DisplayElements payload, CancellationToken ct);

    /// <summary>Clears only what <see cref="BarApplication.Name"/> drew.</summary>
    Task ClearAsync(CancellationToken ct);
}
