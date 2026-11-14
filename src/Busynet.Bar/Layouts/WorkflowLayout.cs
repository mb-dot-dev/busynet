using Busynet.Bar.Display;

namespace Busynet.Bar.Layouts;

/// <summary>
/// The two-row workflow layout on the 72x16 front display: a 12x12 icon on the left and a 54px text column beside
/// it. Measured on a real bar (busyboy's <c>tools/capture_screen.py</c>): <c>normal</c> inks 9 rows starting 2 below
/// its <c>y</c>, <c>small</c> 7 rows starting 2 below, so <c>y=-2</c> and <c>y=7</c> stack them in rows 0-8 and 9-15.
/// Rows wider than the column scroll. Element ids are stable so a redraw replaces rather than stacks.
/// </summary>
public static class WorkflowLayout
{
    public const int IconX = 2;
    public const int IconY = 2;
    public const int TextX = 18;
    public const int TextWidth = 72 - TextX;
    public const int RepoY = -2;
    public const int RefY = 7;
    public const string RepoFont = "normal";
    public const string RefFont = "small";

    /// <summary>Measured: 1200 px/min is ~18 px/s, crossing the display in about four seconds.</summary>
    public const int ScrollRate = 1200;

    public static DisplayElements Build(WorkflowStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new DisplayElements(
            BarApplication.Name,
            [
                new ImageElement("icon", WorkflowIcons.FileName(status.Icon), IconX, IconY),
                Row("repo", status.RepoLabel, RepoY, RepoFont),
                Row("ref", status.RefLabel, RefY, RefFont),
            ]);
    }

    // An empty label still yields valid text: the element requires at least one character.
    private static TextElement Row(string id, string label, int y, string font) =>
        new(id, label.Length == 0 ? "?" : DisplayText.ToDisplayableAscii(label), font, TextX, y,
            Width: TextWidth, ScrollRate: ScrollRate);
}
