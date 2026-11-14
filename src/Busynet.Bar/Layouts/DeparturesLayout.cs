using Busynet.Bar.Display;

namespace Busynet.Bar.Layouts;

/// <summary>
/// The two-row departures layout on the 72x16 front display, using the row positions measured for
/// <see cref="WorkflowLayout"/>: <c>normal</c> at <c>y=-2</c> and <c>small</c> at <c>y=7</c>. Rows wider than the
/// display scroll. Element ids are stable so a redraw replaces rather than stacks.
/// </summary>
public static class DeparturesLayout
{
    public const int X = 1;
    public const int Width = 72 - X;
    public const int HeaderY = -2;
    public const int DestinationY = 7;
    public const string HeaderFont = "normal";
    public const string DestinationFont = "small";
    public const int ScrollRate = WorkflowLayout.ScrollRate;

    public static DisplayElements Build(DepartureFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        return new DisplayElements(
            BarApplication.Name,
            [
                Row("header", frame.Header, HeaderY, HeaderFont),
                Row("destination", frame.Destination, DestinationY, DestinationFont),
            ]);
    }

    // An empty label still yields valid text: the element requires at least one character.
    private static TextElement Row(string id, string label, int y, string font) =>
        new(id, label.Length == 0 ? "?" : DisplayText.ToDisplayableAscii(label), font, X, y,
            Width: Width, ScrollRate: ScrollRate);
}
