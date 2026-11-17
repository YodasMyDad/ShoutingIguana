using System.Globalization;
using System.Text;
using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// How wide a title or meta description is in Google's desktop results, in pixels, and where it will likely be cut
/// (docs/v2-pixel-widths.md). Google shows titles in Arial at 20 px and descriptions at 14 px, in a column about 600 px
/// wide, and cuts what doesn't fit after the last whole word, adding "...".
/// <list type="bullet">
/// <item>Each user-perceived character is as wide as Arial draws it (<see cref="ArialAdvanceWidths"/>): its first code
/// point's advance, plus those of any others the font draws. Combining marks and format characters (a zero-width joiner,
/// a soft hyphen, bidi marks) take no room.</item>
/// <item>Wide characters (<see cref="DisplayWidth.IsWide"/>: Chinese, Japanese, Korean, full-width forms) and emoji, flags
/// and ZWJ sequences included, are one em.</item>
/// <item>A character Arial doesn't draw (Thai, Devanagari, Arabic), which a browser takes from a fallback font, is the
/// width of <c>n</c>: a rough average.</item>
/// </list>
/// Kerning, bold query words, mobile results and Google's rewrites aren't modelled.
/// </summary>
internal static class SearchResultWidth
{
    /// <summary>The size Google's desktop results show a title at.</summary>
    public const int TITLE_FONT_PX = 20;

    /// <summary>The size Google's desktop results show a description at.</summary>
    public const int DESCRIPTION_FONT_PX = 14;

    /// <summary>A character Arial doesn't draw: the width of n, in font units.</summary>
    private const int FALLBACK_UNITS = 1139;

    /// <summary>Separators a cut doesn't end on: "…Chair Set", not "…Chair Set &amp;".</summary>
    private const string DANGLING = "|-\u2013\u2014\u00B7:,&/";

    /// <summary>The text's characters and its width in whole pixels, after runs of white space become one space and the ends are trimmed.</summary>
    public static SearchResultMeasure Measure(string text, SearchResultText kind)
    {
        var rendered = Rendered(text).AsSpan();
        int characters = 0;
        long units = 0;
        while (!rendered.IsEmpty)
        {
            var length = StringInfo.GetNextTextElementLength(rendered);
            units += Units(rendered[..length]);
            characters++;
            rendered = rendered[length..];
        }

        return new SearchResultMeasure(characters, Pixels(units, FontPixels(kind)));
    }

    /// <summary>
    /// The part of the text a result with room for <paramref name="limitPixels"/> shows, as the text renders (white space
    /// collapsed), or null when all of it fits. It ends at the last break that fits: before a space, or between two
    /// characters where either is wide, as Chinese, Japanese and Korean break; with a trailing separator dropped. A word
    /// longer than the limit with no break before it is cut after its last character that fits.
    /// </summary>
    /// <remarks>
    /// The width is compared in the whole pixels <see cref="Measure"/> gives, so text is cut exactly when it measures over
    /// the limit: every title the check flags has a cut, and no other title has one.
    /// </remarks>
    public static string? Cut(string text, SearchResultText kind, int limitPixels)
    {
        var rendered = Rendered(text);
        var fontPixels = FontPixels(kind);
        long units = 0;
        int at = 0, lastBreak = -1;
        var previousWide = false;
        while (at < rendered.Length)
        {
            var element = rendered.AsSpan(at, StringInfo.GetNextTextElementLength(rendered.AsSpan(at)));
            var wide = DisplayWidth.IsWide(element);
            if (at > 0 && (element[0] == ' ' || wide || previousWide))
            {
                lastBreak = at;
            }

            units += Units(element);
            if (Pixels(units, fontPixels) > limitPixels)
            {
                return Visible(rendered[..(lastBreak > 0 ? lastBreak : at)]);
            }

            previousWide = wide;
            at += element.Length;
        }

        return null;
    }

    /// <summary>Font units at a font size, in whole pixels, halves rounded up.</summary>
    public static int Pixels(long units, int fontPixels) =>
        (int)((units * fontPixels + ArialAdvanceWidths.UNITS_PER_EM / 2) / ArialAdvanceWidths.UNITS_PER_EM);

    private static int FontPixels(SearchResultText kind) => kind == SearchResultText.Title ? TITLE_FONT_PX : DESCRIPTION_FONT_PX;

    /// <summary>One user-perceived character's width in font units.</summary>
    private static int Units(ReadOnlySpan<char> element)
    {
        if (DisplayWidth.IsWide(element))
        {
            return ArialAdvanceWidths.UNITS_PER_EM;
        }

        var units = 0;
        var first = true;
        foreach (var rune in element.EnumerateRunes())
        {
            if (TakesNoRoom(rune))
            {
                first = false;
                continue;
            }

            if (ArialAdvanceWidths.TryGet(rune.Value, out var advance))
            {
                units += advance;
            }
            else if (first)
            {
                // Drawn from a fallback font, marks and all.
                return FALLBACK_UNITS;
            }

            first = false;
        }

        return units;
    }

    private static bool TakesNoRoom(Rune rune) =>
        Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format;

    /// <summary>The text as a browser renders it: runs of HTML white space as one space, the ends trimmed. A no-break space stays.</summary>
    private static string Rendered(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var character in text)
        {
            if (character is ' ' or '\t' or '\n' or '\r' or '\f')
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>What shows before the cut: without trailing space, and without one separator left dangling there.</summary>
    private static string Visible(string shown)
    {
        var trimmed = shown.TrimEnd();
        return trimmed.Length > 0 && DANGLING.Contains(trimmed[^1], StringComparison.Ordinal) ? trimmed[..^1].TrimEnd() : trimmed;
    }
}
