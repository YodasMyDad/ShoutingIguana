using System.Text;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// Which characters take a whole em in a search result: East Asian Wide and Fullwidth characters (UAX #11: CJK
/// ideographs, kana, Hangul, full-width forms) and emoji. <see cref="SearchResultWidth"/> measures them as one em, whatever
/// Arial's table says, and breaks a line between them.
/// </summary>
internal static class DisplayWidth
{
    private const int EMOJI_PRESENTATION = 0xFE0F;

    /// <summary>Wide code point ranges, sorted. The emoji blocks are counted whole, as they render.</summary>
    private static readonly (int First, int Last)[] WIDE =
    [
        (0x1100, 0x115F), (0x231A, 0x231B), (0x2329, 0x232A), (0x23E9, 0x23EC), (0x23F0, 0x23F0), (0x23F3, 0x23F3),
        (0x25FD, 0x25FE), (0x2614, 0x2615), (0x2648, 0x2653), (0x267F, 0x267F), (0x2693, 0x2693), (0x26A1, 0x26A1),
        (0x26AA, 0x26AB), (0x26BD, 0x26BE), (0x26C4, 0x26C5), (0x26CE, 0x26CE), (0x26D4, 0x26D4), (0x26EA, 0x26EA),
        (0x26F2, 0x26F3), (0x26F5, 0x26F5), (0x26FA, 0x26FA), (0x26FD, 0x26FD), (0x2705, 0x2705), (0x270A, 0x270B),
        (0x2728, 0x2728), (0x274C, 0x274C), (0x274E, 0x274E), (0x2753, 0x2755), (0x2757, 0x2757), (0x2795, 0x2797),
        (0x27B0, 0x27B0), (0x27BF, 0x27BF), (0x2B1B, 0x2B1C), (0x2B50, 0x2B50), (0x2B55, 0x2B55),
        (0x2E80, 0x303E), // CJK radicals, Kangxi radicals, CJK symbols and punctuation
        (0x3041, 0x33FF), // kana, Bopomofo, Hangul compatibility jamo, enclosed CJK, CJK compatibility
        (0x3400, 0x4DBF), (0x4E00, 0x9FFF), (0xA000, 0xA4CF), (0xA960, 0xA97F), (0xAC00, 0xD7A3), (0xF900, 0xFAFF),
        (0xFE10, 0xFE19), (0xFE30, 0xFE6F),
        (0xFF00, 0xFF60), (0xFFE0, 0xFFE6), // full-width forms
        (0x16FE0, 0x16FFF), (0x17000, 0x18D08), (0x1AFF0, 0x1B2FF),
        (0x1F004, 0x1F004), (0x1F0CF, 0x1F0CF), (0x1F18E, 0x1F18E), (0x1F191, 0x1F19A), (0x1F1E6, 0x1F2FF),
        (0x1F300, 0x1F64F), (0x1F680, 0x1F6FF), (0x1F7E0, 0x1F7FF), (0x1F900, 0x1F9FF), (0x1FA70, 0x1FAFF),
        (0x20000, 0x3FFFD) // CJK extensions B onwards
    ];

    /// <summary>Characters and width of text after trimming, e.g. (21, 42) for a 21-character Japanese title.</summary>
    public static (int Characters, int Width) Measure(string text)
    {
        int characters = 0, width = 0;
        var rest = text.AsSpan().Trim();
        while (!rest.IsEmpty)
        {
            var length = System.Globalization.StringInfo.GetNextTextElementLength(rest);
            var element = rest[..length];
            characters++;
            width += IsWide(element) ? 2 : 1;
            rest = rest[length..];
        }

        return (characters, width);
    }

    /// <summary>
    /// Whether a user-perceived character takes a whole em: its first code point is wide, or it asks for emoji presentation
    /// (U+FE0F). A ZWJ emoji sequence and a flag start with a wide code point.
    /// </summary>
    public static bool IsWide(ReadOnlySpan<char> element)
    {
        // A lone surrogate decodes as U+FFFD, which is narrow.
        Rune.DecodeFromUtf16(element, out var first, out _);
        return IsWide(first.Value) || element.Contains((char)EMOJI_PRESENTATION);
    }

    private static bool IsWide(int codePoint)
    {
        int low = 0, high = WIDE.Length - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (codePoint < WIDE[middle].First)
            {
                high = middle - 1;
            }
            else if (codePoint > WIDE[middle].Last)
            {
                low = middle + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }
}
