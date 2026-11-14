using Busynet.Bar.Layouts;
using Busynet.Cli.Features.GitHubWorkflow;
using Busynet.Cli.Tests.Fakes;

namespace Busynet.Cli.Tests.Features.GitHubWorkflow;

public class WorkflowDisplayTests
{
    [Fact]
    public async Task Draw_sends_the_workflow_layout_and_clear_clears()
    {
        var bar = new FakeBarDisplay();
        var display = new WorkflowDisplay(bar);
        var status = new WorkflowStatus("mb-dot-dev/busynet", "#12 CI", WorkflowIcon.Success);

        await display.DrawAsync(status, TestContext.Current.CancellationToken);
        await display.ClearAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, bar.Calls.Count);
        Assert.Equivalent(WorkflowLayout.Build(status), bar.Calls[0], strict: true);
        Assert.Equal(FakeBarDisplay.Clear, bar.Calls[1]);
    }

    [Fact]
    public async Task Uploads_every_icon()
    {
        var bar = new FakeBarDisplay();

        await new WorkflowDisplay(bar).UploadIconsAsync(TestContext.Current.CancellationToken);

        var uploads = bar.Calls.Cast<FakeBarDisplay.Upload>().ToList();
        Assert.Equal(
            ["pending.png", "in_progress.png", "success.png", "failure.png", "cancelled.png", "skipped.png"],
            uploads.Select(u => u.File));
        Assert.All(uploads, u => Assert.True(u.Length > 0));
    }
}
