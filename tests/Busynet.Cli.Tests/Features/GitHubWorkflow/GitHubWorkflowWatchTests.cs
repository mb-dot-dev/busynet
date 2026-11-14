using Busynet.Bar;
using Busynet.Bar.Layouts;
using Busynet.Cli.Features.GitHubWorkflow;
using Busynet.Cli.Tests.Fakes;
using Busynet.GitHub;
using Busynet.GitHub.Actions;
using Busynet.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

namespace Busynet.Cli.Tests.Features.GitHubWorkflow;

public sealed class GitHubWorkflowWatchTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);
    private static readonly GitHubRepo Repo = new("mb-dot-dev", "busynet");
    private static readonly WorkflowTarget Target = new(Repo, "feature/x", FakeGitHubActions.Ci);

    private static WorkflowRun Run(string status, string? conclusion = null) => new(7, status, conclusion);

    private static WorkflowStatus View(string reference, WorkflowIcon icon) => new("mb-dot-dev/busynet", reference, icon);

    private sealed class Harness : IDisposable
    {
        public TestScheduler Scheduler { get; } = new();
        public FakeDisplay<WorkflowStatus> Display { get; } = new();
        public CancellationTokenSource Cts { get; } = new();
        public FakeGitHubActions Actions { get; private set; } = null!;
        public Task Task { get; private set; } = Task.CompletedTask;

        public Harness Run(params Func<(WorkflowRun?, int?)>[] polls)
        {
            Actions = new FakeGitHubActions(polls);
            Task = GitHubWorkflowWatch.RunAsync(Target, Actions, Display, Interval, Scheduler, NullLogger.Instance, Cts.Token);
            return this;
        }

        // Slack absorbs the one-tick-per-timer lag of the virtual clock.
        public void RunTo(TimeSpan t) => Scheduler.AdvanceTo(t.Ticks + 10);

        public void Dispose()
        {
            Cts.Cancel();
            Cts.Dispose();
        }
    }

    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void Polls_at_once_and_draws_the_first_snapshot()
    {
        using var h = new Harness();
        h.Run(() => (Run("in_progress"), 12));

        h.RunTo(S(0));

        Assert.Equal(1, h.Actions.Polls);
        Assert.Equal(["runs mb-dot-dev/busynet 161335 feature/x", "pulls mb-dot-dev/busynet feature/x"], h.Actions.Calls);
        Assert.Equal([FakeDisplay<WorkflowStatus>.Clear, View("#12 CI", WorkflowIcon.InProgress)], h.Display.Calls);
    }

    [Fact]
    public void Polls_every_interval_and_redraws_only_on_change()
    {
        using var h = new Harness();
        h.Run(
            () => (Run("queued"), null),
            () => (Run("in_progress"), null),
            () => (Run("in_progress"), null),
            () => (Run("completed", "success"), 12));

        h.RunTo(S(9));
        Assert.Equal(1, h.Actions.Polls);

        h.RunTo(S(30));
        Assert.Equal(4, h.Actions.Polls);
        Assert.Equal(
            [
                View("feature/x CI", WorkflowIcon.Pending),
                View("feature/x CI", WorkflowIcon.InProgress),
                View("#12 CI", WorkflowIcon.Success),
            ],
            h.Display.Draws);
    }

    [Fact]
    public void Transient_failure_keeps_the_display_and_retries()
    {
        using var h = new Harness();
        h.Run(
            () => (Run("in_progress"), null),
            () => throw new GitHubTransientException("HTTP 502"),
            () => (Run("completed", "failure"), null));

        h.RunTo(S(20));

        Assert.Equal(3, h.Actions.Polls);
        Assert.Equal(
            [View("feature/x CI", WorkflowIcon.InProgress), View("feature/x CI", WorkflowIcon.Failure)],
            h.Display.Draws);
        Assert.False(h.Task.IsCompleted);
    }

    [Fact]
    public void Rate_limit_waits_for_retry_after()
    {
        using var h = new Harness();
        h.Run(
            () => (Run("in_progress"), null),
            () => throw new GitHubTransientException("rate limited", S(60)),
            () => (Run("in_progress"), null));

        h.RunTo(S(69));
        Assert.Equal(2, h.Actions.Polls);

        h.RunTo(S(70));
        Assert.Equal(3, h.Actions.Polls);
    }

    [Fact]
    public void Nothing_is_drawn_until_a_poll_succeeds()
    {
        using var h = new Harness();
        h.Run(() => throw new GitHubTransientException("down"));

        h.RunTo(S(20));

        Assert.Equal([FakeDisplay<WorkflowStatus>.Clear], h.Display.Calls);
    }

    [Fact]
    public async Task Auth_failure_ends_the_watch_after_clearing()
    {
        using var h = new Harness();
        h.Run(() => (Run("in_progress"), null), () => throw new GitHubAuthException("rejected"));

        h.RunTo(S(10));

        await Assert.ThrowsAsync<GitHubAuthException>(() => h.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(FakeDisplay<WorkflowStatus>.Clear, h.Display.Calls[^1]);
    }

    [Fact]
    public void Failed_draw_is_retried_on_the_next_poll()
    {
        using var h = new Harness();
        h.Display.FailNextDraws(1, new BarApiException(409, "POST", "/api/display/draw", "busy", null));
        h.Run(() => (Run("in_progress"), null));

        h.RunTo(S(10));

        Assert.Equal(
            [View("feature/x CI", WorkflowIcon.InProgress), View("feature/x CI", WorkflowIcon.InProgress)],
            h.Display.Draws);
    }

    [Fact]
    public async Task Cancel_clears_and_returns()
    {
        using var h = new Harness();
        h.Run(() => (Run("in_progress"), null));
        h.RunTo(S(0));

        await h.Cts.CancelAsync();
        await h.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(FakeDisplay<WorkflowStatus>.Clear, h.Display.Calls[^1]);
    }
}
