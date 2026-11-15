using Busynet.Bar.Display;
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
        var label = Assert.IsType<TextElement>(LimitsLayout.Build(LimitGauge.From("é", 10)).Elements[^1]);

        Assert.Equal("?", label.Text);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.6, 42)]
    [InlineData(1, 69)]
    [InlineData(1.5, 69)]
    [InlineData(-0.2, 0)]
    public void Marker_offset_is_proportional_and_clamped(double progress, int offset) =>
        Assert.Equal(offset, LimitGauge.From("x", 10, progress).MarkerOffset);

    [Fact]
    public void No_time_progress_means_no_marker()
    {
        var gauge = LimitGauge.From("x", 10);

        Assert.Null(gauge.MarkerOffset);
        Assert.Equal(3, LimitsLayout.Build(gauge).Elements.Count);
    }

    [Fact]
    public void Marker_is_a_white_1px_rectangle_over_the_fill()
    {
        var elements = LimitsLayout.Build(LimitGauge.From("x", 30, 0.6)).Elements;

        Assert.Equal("limit-fill", elements[1].Id);
        var marker = Assert.IsType<RectangleElement>(elements[2]);
        Assert.Equal("limit-time", marker.Id);
        Assert.Equal((43, 8, 1, 8), (marker.X, marker.Y, marker.Width, marker.Height));
        Assert.Equal(["#FFFFFFFF"], marker.FillColors);
        Assert.IsType<TextElement>(elements[3]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(95)]
    public void Marker_is_drawn_on_an_empty_and_on_a_high_gauge(double percent) =>
        Assert.Contains(LimitsLayout.Build(LimitGauge.From("x", percent, 0.5)).Elements, e => e.Id == "limit-time");
}
