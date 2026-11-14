namespace Busynet.Bar.Layouts;

/// <summary>The status icons of the workflow layout, matching GitHub's own run icons.</summary>
public enum WorkflowIcon
{
    Pending,
    InProgress,
    Success,
    Failure,
    Cancelled,
    Skipped,
}

/// <summary>
/// The 12x12 PNG behind each <see cref="WorkflowIcon"/>, embedded in this assembly. Redrawn from GitHub's Octicons in
/// its dark-mode palette, which reads better on an LED matrix; the same files busyboy ships.
/// </summary>
public static class WorkflowIcons
{
    public static IReadOnlyList<WorkflowIcon> All { get; } = Enum.GetValues<WorkflowIcon>();

    /// <summary>The asset file name the icon is uploaded as and drawn from.</summary>
    public static string FileName(WorkflowIcon icon) => icon switch
    {
        WorkflowIcon.Pending => "pending.png",
        WorkflowIcon.InProgress => "in_progress.png",
        WorkflowIcon.Success => "success.png",
        WorkflowIcon.Failure => "failure.png",
        WorkflowIcon.Cancelled => "cancelled.png",
        WorkflowIcon.Skipped => "skipped.png",
        _ => throw new ArgumentOutOfRangeException(nameof(icon), icon, null),
    };

    /// <summary>The icon's PNG bytes.</summary>
    public static byte[] Read(WorkflowIcon icon)
    {
        var name = "Busynet.Bar.Assets." + FileName(icon);
        using var stream = typeof(WorkflowIcons).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded icon {name}.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
