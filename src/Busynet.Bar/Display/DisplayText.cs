using System.Globalization;
using System.Text;

namespace Busynet.Bar.Display;

public static class DisplayText
{
    /// <summary>Replaces every character outside printable ASCII (0x20..0x7E) with <c>?</c>.</summary>
    public static string ToDisplayableAscii(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = source[i] is >= ' ' and <= '~' ? source[i] : '?';
            }
        });
    }

    /// <summary>
    /// Drops every character outside printable ASCII and collapses the leftover whitespace. Unlike
    /// <see cref="ToDisplayableAscii"/> this may return an empty string, so it is only for one part of a text
    /// whose other parts keep it non-empty: <c>"🚀 Deploy"</c> becomes <c>"Deploy"</c> rather than <c>"? Deploy"</c>.
    /// </summary>
    public static string StripUndisplayable(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var kept = new string([.. text.Where(c => c is >= ' ' and <= '~')]);
        return string.Join(' ', kept.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Folds accented letters to their base letter (<c>"Kőbánya"</c> becomes <c>"Kobanya"</c>) and arrows to
    /// <c>&gt;</c>, then <see cref="StripUndisplayable"/>s the rest. May return an empty string.
    /// </summary>
    public static string Transliterate(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (c is '\u25BA' or '\u25B6')
                builder.Append('>');
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }

        return StripUndisplayable(builder.ToString());
    }
}
