using Busynet.Bar.Layouts;
using Busynet.Cli.Features.ClaudeLimits;
using Busynet.Cli.Tests.Fakes;

namespace Busynet.Cli.Tests.Features.ClaudeLimits;

public class LimitsDisplayTests
{
    [Fact]
    public async Task Draw_sends_the_limits_layout_and_clear_clears()
    {
        var bar = new FakeBarDisplay();
        var display = new LimitsDisplay(bar);
        var gauge = LimitGauge.From("5h 85% 3h49m", 85);

        await display.DrawAsync(gauge, TestContext.Current.CancellationToken);
        await display.ClearAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, bar.Calls.Count);
        Assert.Equivalent(LimitsLayout.Build(gauge), bar.Calls[0], strict: true);
        Assert.Equal(FakeBarDisplay.Clear, bar.Calls[1]);
    }
}
