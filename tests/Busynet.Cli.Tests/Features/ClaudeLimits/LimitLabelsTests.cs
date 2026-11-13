using Busynet.Bar.Layouts;
using Busynet.Claude.Usage;
using Busynet.Cli.Features.ClaudeLimits;

namespace Busynet.Cli.Tests.Features.ClaudeLimits;

public class LimitLabelsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(3 * 60 + 59, "3h59m")]
    [InlineData(3 * 60 + 5, "3h05m")]
    [InlineData(60, "1h00m")]
    [InlineData(59, "59m")]
    [InlineData(0, "0m")]
    [InlineData(-5, "0m")]
    public void Session_countdown(int minutes, string expected) =>
        Assert.Equal(expected, LimitLabels.SessionCountdown(TimeSpan.FromMinutes(minutes)));

    [Fact]
    public void Session_countdown_floors_partial_minutes() =>
        Assert.Equal("59m", LimitLabels.SessionCountdown(TimeSpan.FromSeconds(59 * 60 + 59)));

    [Theory]
    [InlineData(LimitKind.Session, 85, 3 * 60 + 49, "5h 85% 3h49m")]
    [InlineData(LimitKind.Weekly, 40, (6 * 24 + 2) * 60, "wk 40% 6d")]
    [InlineData(LimitKind.Weekly, 40, 29 * 60, "wk 40% 29h")]
    [InlineData(LimitKind.Weekly, 100, 47 * 60 + 59, "wk 100% 47h")]
    [InlineData(LimitKind.Weekly, 40, 48 * 60, "wk 40% 2d")]
    [InlineData(LimitKind.Session, 99.6, 60, "5h 100% 1h00m")]
    [InlineData(LimitKind.Session, 150, 60, "5h 100% 1h00m")]
    [InlineData(LimitKind.Session, -3, 60, "5h 0% 1h00m")]
    [InlineData(LimitKind.Session, 0.4, 60, "5h 0% 1h00m")]
    [InlineData(LimitKind.Session, 85, null, "5h 85%")]
    [InlineData(LimitKind.Session, 85, -60, "5h 85% 0m")]
    [InlineData(LimitKind.Weekly, 100, 23 * 60 + 59, "wk 100% 23h")]
    [InlineData(LimitKind.Weekly, 40, 5 * 60 + 5, "wk 40% 5h")]
    [InlineData(LimitKind.Weekly, 40, 59, "wk 40% 59m")]
    [InlineData(LimitKind.Weekly, 40, -60, "wk 40% 0m")]
    public void Format(LimitKind kind, double utilization, int? minutesToReset, string expected)
    {
        var window = new UsageWindow(utilization, minutesToReset is { } m ? Now.AddMinutes(m) : null);
        Assert.Equal(expected, LimitLabels.Format(kind, window, Now));
    }

    [Theory]
    [InlineData(0.5, "0%")]
    [InlineData(1.5, "2%")]
    [InlineData(2.5, "2%")]
    public void Format_rounds_half_to_even(double utilization, string expectedPercent) =>
        Assert.Equal($"5h {expectedPercent}", LimitLabels.Format(LimitKind.Session, new UsageWindow(utilization, null), Now));

    private static readonly Usage SampleUsage = new(
        new UsageWindow(85.4, Now.AddMinutes(229)),
        new UsageWindow(40, Now.AddDays(6)));

    [Fact]
    public void Render_without_usage_is_null() =>
        Assert.Null(new LimitsState(null, LimitKind.Session).Render(Now));

    [Fact]
    public void Render_session_uses_raw_utilization_for_the_gauge() =>
        Assert.Equal(LimitGauge.From("5h 85% 3h49m", 85.4), new LimitsState(SampleUsage, LimitKind.Session).Render(Now));

    [Fact]
    public void Render_weekly_shows_the_weekly_window() =>
        Assert.Equal(LimitGauge.From("wk 40% 6d", 40), new LimitsState(SampleUsage, LimitKind.Weekly).Render(Now));

    [Fact]
    public void Toggle_flips_between_session_and_weekly()
    {
        var state = new LimitsState(SampleUsage, LimitKind.Session);
        Assert.Equal(LimitKind.Weekly, state.Toggle().Kind);
        Assert.Equal(LimitKind.Session, state.Toggle().Toggle().Kind);
        Assert.Same(SampleUsage, state.Toggle().Usage);
    }

    [Fact]
    public void WithUsage_replaces_usage_and_keeps_kind()
    {
        var state = new LimitsState(null, LimitKind.Weekly).WithUsage(SampleUsage);
        Assert.Equal(new LimitsState(SampleUsage, LimitKind.Weekly), state);
    }
}
