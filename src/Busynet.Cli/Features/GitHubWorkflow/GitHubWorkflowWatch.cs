using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Busynet.Bar.Layouts;
using Busynet.Core.Watching;
using Busynet.GitHub.Actions;
using Microsoft.Extensions.Logging;

namespace Busynet.Cli.Features.GitHubWorkflow;

public static class GitHubWorkflowWatch
{
    /// <summary>
    /// Polls the latest run and open pull request of <paramref name="target"/> at once and then every
    /// <paramref name="interval"/>, redrawing only when the view changes (a redraw restarts the repo row's scroll).
    /// Transient failures keep the display and back off by Retry-After; a fatal GitHub failure (a rejected token)
    /// ends the watch, rethrown after clearing. Returns normally when <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    public static Task RunAsync(
        WorkflowTarget target,
        IGitHubActions actions,
        IDisplay<WorkflowStatus> display,
        TimeSpan interval,
        IScheduler scheduler,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(actions);

        var polled = Poller.Poll(ct => FetchAsync(target, actions, ct), interval, initialDelay: TimeSpan.Zero, scheduler, logger)
            .Select(snapshot => (Func<WorkflowState, WorkflowState>)(state => state.WithSnapshot(snapshot)));

        return DisplayWatch.RunAsync(
            new WorkflowState(target, null),
            polled,
            state => state.Render(),
            display,
            logger,
            cancellationToken);
    }

    private static async Task<WorkflowSnapshot> FetchAsync(WorkflowTarget target, IGitHubActions actions, CancellationToken ct)
    {
        var run = await actions.GetLatestRunAsync(target.Repo, target.Workflow.Id, target.Branch, ct);
        var pullRequest = await actions.GetPullRequestNumberAsync(target.Repo, target.Branch, ct);
        return new WorkflowSnapshot(run, pullRequest);
    }
}
