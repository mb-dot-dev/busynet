namespace Busynet.Bar.Tests.Display;

public sealed class BarColorTests
{
    [Theory]
    [InlineData("#1F1F1F", "#1F1F1FFF")]
    [InlineData("#cd6e58", "#CD6E58FF")]
    [InlineData("#cd6e5880", "#CD6E5880")]
    [InlineData("#CD6E5880", "#CD6E5880")]
    public void Color_normalize(string input, string expected) =>
        Assert.Equal(expected, BarColor.Normalize(input));

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("")]
    public void Color_normalize_rejects_invalid(string input) =>
        Assert.Throws<ArgumentException>(() => BarColor.Normalize(input));
}
