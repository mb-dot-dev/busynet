using Busynet.Bar.Http;
using Busynet.Bar.Layouts;
using Busynet.Core.Watching;

namespace Busynet.Cli.Features.GitHubWorkflow;

/// <summary>Draws one workflow status on the bar.</summary>
public sealed class WorkflowDisplay(IBarDisplay bar) : IDisplay<WorkflowStatus>
{
    public Task DrawAsync(WorkflowStatus view, CancellationToken cancellationToken) =>
        bar.DrawAsync(WorkflowLayout.Build(view), cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken) =>
        bar.ClearAsync(cancellationToken);

    /// <summary>
    /// Uploads every status icon. Unconditional: the bar cannot list an application's assets, and six tiny uploads
    /// at startup cost less than tracking what is already there.
    /// </summary>
    public async Task UploadIconsAsync(CancellationToken cancellationToken)
    {
        foreach (var icon in WorkflowIcons.All)
            await bar.UploadAssetAsync(WorkflowIcons.FileName(icon), WorkflowIcons.Read(icon), cancellationToken);
    }
}
