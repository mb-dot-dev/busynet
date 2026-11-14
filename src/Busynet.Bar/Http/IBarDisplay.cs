using Busynet.Bar.Display;

namespace Busynet.Bar.Http;

public interface IBarDisplay
{
    Task DrawAsync(DisplayElements payload, CancellationToken ct);

    /// <summary>Clears only what <see cref="BarApplication.Name"/> drew.</summary>
    Task ClearAsync(CancellationToken ct);

    /// <summary>
    /// Stores <paramref name="content"/> as <paramref name="file"/> in <see cref="BarApplication.Name"/>'s assets,
    /// replacing any file of that name, so an image element can show it.
    /// </summary>
    Task UploadAssetAsync(string file, ReadOnlyMemory<byte> content, CancellationToken ct);
}
