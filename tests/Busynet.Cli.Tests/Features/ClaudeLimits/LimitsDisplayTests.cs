using Busynet.Bar;
using Busynet.Bar.Layouts;
using Busynet.Cli.Features.ClaudeLimits;

namespace Busynet.Cli.Tests.Features.ClaudeLimits;

public class LimitsDisplayTests
{
    private sealed class RecordingBar : IBarDisplay
    {
        public List<object> Calls { get; } = [];

        public Task DrawAsync(DisplayElements payload, CancellationToken ct)
        {
            Calls.Add(payload);
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken ct)
        {
            Calls.Add("clear");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Draw_sends_the_limits_layout_and_clear_clears()
    {
        var bar = new RecordingBar();
        var display = new LimitsDisplay(bar);
        var gauge = LimitGauge.From("5h 85% 3h49m", 85);

        await display.DrawAsync(gauge, TestContext.Current.CancellationToken);
        await display.ClearAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, bar.Calls.Count);
        Assert.Equivalent(LimitsLayout.Build(gauge), bar.Calls[0], strict: true);
        Assert.Equal("clear", bar.Calls[1]);
    }
}
