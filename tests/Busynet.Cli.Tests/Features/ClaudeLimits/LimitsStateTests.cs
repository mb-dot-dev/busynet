using Busynet.Claude.Usage;
using Busynet.Cli.Features.ClaudeLimits;

namespace Busynet.Cli.Tests.Features.ClaudeLimits;

public class LimitsStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static int? Marker(LimitKind kind, DateTimeOffset? resetsAt)
    {
        var window = new UsageWindow(40, resetsAt);
        return new LimitsState(new Usage(window, window), kind).Render(Now)!.MarkerOffset;
    }

    [Fact]
    public void Session_with_2h_left_is_60_percent_elapsed() =>
        Assert.Equal(42, Marker(LimitKind.Session, Now + TimeSpan.FromHours(2)));

    [Fact]
    public void Session_just_reset_has_the_marker_at_the_start() =>
        Assert.Equal(0, Marker(LimitKind.Session, Now + TimeSpan.FromHours(5)));

    [Fact]
    public void Session_resetting_now_has_the_marker_on_the_last_pixel() =>
        Assert.Equal(69, Marker(LimitKind.Session, Now));

    [Fact]
    public void An_overdue_reset_clamps_to_the_last_pixel() =>
        Assert.Equal(69, Marker(LimitKind.Session, Now - TimeSpan.FromHours(1)));

    [Fact]
    public void A_reset_more_than_a_window_away_clamps_to_the_start() =>
        Assert.Equal(0, Marker(LimitKind.Session, Now + TimeSpan.FromHours(6)));

    [Fact]
    public void Weekly_uses_a_7_day_window() =>
        Assert.Equal(35, Marker(LimitKind.Weekly, Now + TimeSpan.FromHours(84)));

    [Fact]
    public void Without_a_reset_time_there_is_no_marker()
    {
        var window = new UsageWindow(40, null);
        var gauge = new LimitsState(new Usage(window, window), LimitKind.Weekly).Render(Now)!;

        Assert.Null(gauge.MarkerOffset);
        Assert.Equal("wk 40%", gauge.Label);
    }
}
