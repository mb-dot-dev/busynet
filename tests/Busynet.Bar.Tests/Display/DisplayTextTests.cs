using Busynet.Bar.Display;
namespace Busynet.Bar.Tests.Display;

public sealed class DisplayTextTests
{
    [Fact]
    public void Non_ascii_becomes_question_mark() =>
        Assert.Equal("? ok", DisplayText.ToDisplayableAscii("é ok"));

    [Fact]
    public void Control_characters_become_question_mark() =>
        Assert.Equal("a?b", DisplayText.ToDisplayableAscii("a\tb"));

    [Theory]
    [InlineData("")]
    [InlineData("é")]
    [InlineData("a\nb")]
    [InlineData("hi\n")]
    public void TextElement_rejects_non_ascii_and_empty(string text) =>
        Assert.Throws<ArgumentException>(() => new TextElement("id", text, "small", 0, 0));

    [Theory]
    [InlineData("CI", "CI")]
    [InlineData("🚀 Deploy", "Deploy")]
    [InlineData("CI 🚀 Build", "CI Build")]
    [InlineData("  a\tb  ", "ab")]
    [InlineData("🚀", "")]
    [InlineData("", "")]
    public void StripUndisplayable_drops_and_collapses(string text, string expected) =>
        Assert.Equal(expected, DisplayText.StripUndisplayable(text));
}
