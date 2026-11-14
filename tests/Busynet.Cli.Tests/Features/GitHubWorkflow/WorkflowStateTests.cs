using Busynet.Bar.Layouts;
using Busynet.Cli.Features.GitHubWorkflow;
using Busynet.GitHub;
using Busynet.GitHub.Actions;

namespace Busynet.Cli.Tests.Features.GitHubWorkflow;

public sealed class WorkflowStateTests
{
    private static readonly GitHubRepo Repo = new("mb-dot-dev", "busynet");

    private static WorkflowState State(string workflowName = "CI", string branch = "main") =>
        new(new WorkflowTarget(Repo, branch, new Workflow(1, workflowName, ".github/workflows/ci.yaml")), null);

    private static WorkflowRun Run(string status, string? conclusion = null) => new(7, status, conclusion);

    [Fact]
    public void Renders_nothing_before_the_first_poll() => Assert.Null(State().Render());

    [Fact]
    public void Shows_the_pull_request_and_workflow_name()
    {
        var status = State().WithSnapshot(new WorkflowSnapshot(Run("in_progress"), 12)).Render();

        Assert.Equal(new WorkflowStatus("mb-dot-dev/busynet", "#12 CI", WorkflowIcon.InProgress), status);
    }

    [Fact]
    public void Falls_back_to_the_branch_without_a_pull_request()
    {
        var status = State(branch: "feature/x").WithSnapshot(new WorkflowSnapshot(null, null)).Render();

        Assert.Equal(new WorkflowStatus("mb-dot-dev/busynet", "feature/x CI", WorkflowIcon.Pending), status);
    }

    [Theory]
    [InlineData("🚀 Deploy", "#7 Deploy")]
    [InlineData("CI 🚀 Build", "#7 CI Build")]
    [InlineData("🚀", "#7")]
    public void Strips_what_the_bar_cannot_render_from_the_name(string name, string expected)
    {
        var status = State(name).WithSnapshot(new WorkflowSnapshot(null, 7)).Render();

        Assert.Equal(expected, status!.RefLabel);
    }

    [Theory]
    [InlineData("queued", null, WorkflowIcon.Pending)]
    [InlineData("waiting", null, WorkflowIcon.Pending)]
    [InlineData("pending", null, WorkflowIcon.Pending)]
    [InlineData("requested", null, WorkflowIcon.Pending)]
    [InlineData("in_progress", null, WorkflowIcon.InProgress)]
    [InlineData("completed", "success", WorkflowIcon.Success)]
    [InlineData("completed", "failure", WorkflowIcon.Failure)]
    [InlineData("completed", "timed_out", WorkflowIcon.Failure)]
    [InlineData("completed", "startup_failure", WorkflowIcon.Failure)]
    [InlineData("completed", "cancelled", WorkflowIcon.Cancelled)]
    [InlineData("completed", "skipped", WorkflowIcon.Skipped)]
    [InlineData("completed", "neutral", WorkflowIcon.Skipped)]
    [InlineData("completed", "action_required", WorkflowIcon.Pending)]
    [InlineData("completed", "something_new", WorkflowIcon.Pending)]
    [InlineData("completed", null, WorkflowIcon.Pending)]
    [InlineData("something_new", null, WorkflowIcon.Pending)]
    public void Maps_github_status_to_icon(string status, string? conclusion, WorkflowIcon expected) =>
        Assert.Equal(expected, RunIcons.For(Run(status, conclusion)));

    [Fact]
    public void No_run_is_pending() => Assert.Equal(WorkflowIcon.Pending, RunIcons.For(null));
}
