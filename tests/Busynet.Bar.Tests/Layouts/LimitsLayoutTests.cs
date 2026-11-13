using Busynet.Bar.Layouts;

namespace Busynet.Bar.Tests.Layouts;

public sealed class LimitsLayoutTests
{
    private static RectangleElement Fill(LimitGauge gauge) =>
        Assert.IsType<RectangleElement>(LimitsLayout.Build(gauge).Elements[1]);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 35)]
    [InlineData(100, 70)]
    [InlineData(150, 70)]
    [InlineData(-5, 0)]
    [InlineData(0.5, 0)]
    public void Fill_width_is_proportional_and_clamped(double percent, int width) =>
        Assert.Equal(width, LimitGauge.From("x", percent).FillWidth);

    [Theory]
    [InlineData(0)]
    [InlineData(0.4)]
    public void An_empty_bar_still_draws_a_track_coloured_1px_fill(double percent)
    {
        var fill = Fill(LimitGauge.From("x", percent));

        Assert.Equal(1, fill.Width);
        Assert.Equal(["#1F1F1FFF"], fill.FillColors);
    }

    [Fact]
    public void Above_85_percent_uses_the_high_colour()
    {
        Assert.Equal(["#CD6E58FF"], Fill(LimitGauge.From("x", 85)).FillColors);
        Assert.Equal(["#B3261EFF"], Fill(LimitGauge.From("x", 85.1)).FillColors);
    }

    [Fact]
    public void Gauges_with_same_label_width_and_colour_are_equal() =>
        Assert.Equal(LimitGauge.From("x", 50.0), LimitGauge.From("x", 50.2));

    [Fact]
    public void Label_is_made_displayable()
    {
        var label = Assert.IsType<TextElement>(LimitsLayout.Build(LimitGauge.From("é", 10)).Elements[2]);

        Assert.Equal("?", label.Text);
    }
}
