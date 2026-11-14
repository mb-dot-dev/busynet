using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Busynet.Bar.Display;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextElement), "text")]
[JsonDerivedType(typeof(RectangleElement), "rectangle")]
[JsonDerivedType(typeof(ImageElement), "image")]
public abstract record DisplayElement(string Id, int X, int Y, string Display = "front");

/// <summary>
/// A line of text. With <c>Width</c> set, text wider than it scrolls at <c>ScrollRate</c> pixels per minute.
/// </summary>
public sealed partial record TextElement(
    string Id,
    string Text,
    string Font,
    int X,
    int Y,
    string? Color = null,
    int? Width = null,
    int? ScrollRate = null)
    : DisplayElement(Id, X, Y)
{
    public string Text { get; } = PrintableAscii().IsMatch(Text)
        ? Text
        : throw new ArgumentException("Text must be non-empty printable ASCII.", nameof(Text));

    [GeneratedRegex(@"^[\x20-\x7E]+\z")]
    private static partial Regex PrintableAscii();
}

/// <summary>A filled rectangle. <c>BorderWidth</c> defaults to 0: the vendor default of 1 draws a white outline.</summary>
public sealed record RectangleElement(
    string Id,
    int X,
    int Y,
    int Width,
    int Height,
    IReadOnlyList<string> FillColors,
    string Fill = "solid",
    int BorderWidth = 0) : DisplayElement(Id, X, Y);

/// <summary>An image from this application's uploaded assets (see <c>IBarDisplay.UploadAssetAsync</c>).</summary>
public sealed record ImageElement(string Id, string Path, int X, int Y) : DisplayElement(Id, X, Y);
