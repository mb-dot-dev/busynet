using System.Text.RegularExpressions;

namespace Busynet.Bar;

public static partial class BarColor
{
    /// <summary>Normalises <c>#RRGGBB</c> to <c>#RRGGBBFF</c> and upper-cases <c>#RRGGBBAA</c>.</summary>
    public static string Normalize(string color)
    {
        ArgumentNullException.ThrowIfNull(color);

        if (!Hex().IsMatch(color))
        {
            throw new ArgumentException("Colour must be #RRGGBB or #RRGGBBAA.", nameof(color));
        }

        var upper = color.ToUpperInvariant();
        return upper.Length == 7 ? upper + "FF" : upper;
    }

    [GeneratedRegex(@"^#(?:[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")]
    private static partial Regex Hex();
}
