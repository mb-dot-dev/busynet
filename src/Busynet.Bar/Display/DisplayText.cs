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
}
