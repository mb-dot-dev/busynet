using Busynet.Bar.Display;
using Busynet.Bar.Layouts;

namespace Busynet.Cli.Features.GitHubWorkflow;

/// <summary>The watched target and the last snapshot polled for it (null before the first poll succeeds).</summary>
public sealed record WorkflowState(WorkflowTarget Target, WorkflowSnapshot? Snapshot)
{
    public WorkflowState WithSnapshot(WorkflowSnapshot snapshot) => this with { Snapshot = snapshot };

    /// <summary>
    /// The status as drawn, or <c>null</c> before anything was polled. The bottom row is <c>#12 CI</c> with an open pull
    /// request, else <c>main CI</c>. The workflow name is stripped of characters the bar cannot render (<c>🚀 Deploy</c>
    /// shows as <c>Deploy</c>) and left out when nothing survives; the ref keeps the row non-empty.
    /// </summary>
    public WorkflowStatus? Render()
    {
        if (Snapshot is null)
            return null;

        var reference = Snapshot.PullRequest is { } number ? $"#{number}" : Target.Branch;
        var name = DisplayText.StripUndisplayable(Target.Workflow.Name);
        return new WorkflowStatus(
            Target.Repo.Slug,
            name.Length > 0 ? $"{reference} {name}" : reference,
            RunIcons.For(Snapshot.Run));
    }
}
