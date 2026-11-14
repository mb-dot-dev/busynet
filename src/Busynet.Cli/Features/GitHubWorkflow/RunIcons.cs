using Busynet.Bar.Layouts;
using Busynet.GitHub.Actions;

namespace Busynet.Cli.Features.GitHubWorkflow;

/// <summary>Translates GitHub's run vocabulary into the bar's status icons, ported from busyboy's <c>watch.py</c>.</summary>
public static class RunIcons
{
    /// <summary>
    /// A run reports <c>status</c> until it completes, then <c>conclusion</c> carries the outcome. Unknown values (GitHub
    /// adds some over time) and a missing run show <see cref="WorkflowIcon.Pending"/> rather than ending the watch.
    /// </summary>
    public static WorkflowIcon For(WorkflowRun? run) => run switch
    {
        null => WorkflowIcon.Pending,
        { Status: "completed" } => run.Conclusion switch
        {
            "success" => WorkflowIcon.Success,
            "failure" or "timed_out" or "startup_failure" => WorkflowIcon.Failure,
            "cancelled" => WorkflowIcon.Cancelled,
            "skipped" or "neutral" => WorkflowIcon.Skipped,
            _ => WorkflowIcon.Pending,
        },
        { Status: "in_progress" } => WorkflowIcon.InProgress,
        _ => WorkflowIcon.Pending,
    };
}
