using System.Globalization;

namespace ShoutingIguana.Engine.Export.Xlsx;

/// <summary>
/// Text as a workbook's XML holds it. XML 1.0 has no way to write most control characters, so SpreadsheetML escapes them
/// as <c>_xHHHH_</c>, and a carriage return too (an XML parser would turn it into a line feed); text that already holds
/// such a sequence has its underscore written as one (<c>_x005F_</c>), so it reads back as it was written. A surrogate with no partner becomes
/// U+FFFD as the text is encoded, which the writer's encoding does.
/// </summary>
internal static class XlsxText
{
    /// <summary>The most characters a cell holds; Excel refuses a file with a longer one.</summary>
    public const int MAX_CELL_CHARS = 32_767;

    /// <summary>The text cut to what a cell holds, never between the two halves of a surrogate pair.</summary>
    public static ReadOnlySpan<char> Fit(ReadOnlySpan<char> text, out bool cut)
    {
        cut = text.Length > MAX_CELL_CHARS;
        if (!cut)
        {
            return text;
        }

        var length = char.IsHighSurrogate(text[MAX_CELL_CHARS - 1]) ? MAX_CELL_CHARS - 1 : MAX_CELL_CHARS;
        return text[..length];
    }

    /// <summary>Writes text as the content of an element.</summary>
    public static void Write(TextWriter writer, ReadOnlySpan<char> text)
    {
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            string? escaped = character switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '\t' or '\n' => null,
                '_' when IsEscape(text[index..]) => "_x005F_",
                < ' ' or '￾' or '￿' => string.Create(CultureInfo.InvariantCulture, $"_x{(int)character:X4}_"),
                _ => null
            };
            if (escaped is null)
            {
                continue;
            }

            writer.Write(text[start..index]);
            writer.Write(escaped);
            start = index + 1;
        }

        writer.Write(text[start..]);
    }

    /// <summary>Writes text as an attribute's value, in double quotes.</summary>
    public static void WriteAttribute(TextWriter writer, ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            switch (character)
            {
                case '&':
                    writer.Write("&amp;");
                    break;
                case '<':
                    writer.Write("&lt;");
                    break;
                case '>':
                    writer.Write("&gt;");
                    break;
                case '"':
                    writer.Write("&quot;");
                    break;
                case < ' ' or '￾' or '￿':
                    // No name of ours holds one: written as a space rather than as XML no parser reads.
                    writer.Write(' ');
                    break;
                default:
                    writer.Write(character);
                    break;
            }
        }
    }

    /// <summary>Whether the text starts with what SpreadsheetML reads as an escaped character: <c>_xHHHH_</c>.</summary>
    private static bool IsEscape(ReadOnlySpan<char> text) =>
        text.Length >= 7 && text[1] == 'x' && text[6] == '_' && char.IsAsciiHexDigit(text[2]) && char.IsAsciiHexDigit(text[3]) && char.IsAsciiHexDigit(text[4]) &&
        char.IsAsciiHexDigit(text[5]);
}
