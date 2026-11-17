using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// Titles and descriptions measured as Google's desktop results show them: Arial at 20 px and 14 px, by Liberation Sans's
/// advance widths (Arial's by design), with wide characters and emoji one em; and where the text will likely be cut. The
/// expected widths are summed by hand from the font's advances, not by the code under test (docs/v2-pixel-widths.md).
/// </summary>
public sealed class SearchResultWidthTests
{
    /// <summary>Spot values of the table, in font units (2048 to the em), the same in Arial and Liberation Sans by design.</summary>
    [Theory]
    [InlineData('W', 1933)]
    [InlineData('i', 455)]
    [InlineData(' ', 569)]
    [InlineData('n', 1139)]
    [InlineData('.', 569)]
    [InlineData('\u2026', 2048)]
    [InlineData('|', 532)]
    [InlineData('\u00A0', 569)]
    public void TheTableHoldsArialsAdvances(char character, int advance)
    {
        Assert.True(ArialAdvanceWidths.TryGet(character, out var found));
        Assert.Equal(advance, found);
        Assert.Equal(2048, ArialAdvanceWidths.UNITS_PER_EM);
    }

    [Fact]
    public void TheTableLeavesOutWideAndPrivateUseCodePoints()
    {
        Assert.False(ArialAdvanceWidths.TryGet('日', out _));
        Assert.False(ArialAdvanceWidths.TryGet(0xE000, out _));
        Assert.False(ArialAdvanceWidths.TryGet(0x0E01, out _)); // Thai: Arial doesn't draw it
    }

    /// <summary>W 1933 + i 455 + space 569 + n 1139 = 4096 units, two ems: 40 px at 20 px, 28 at 14.</summary>
    [Fact]
    public void WidthIsTheSumOfTheAdvancesAtTheFontSize()
    {
        Assert.Equal(new SearchResultMeasure(4, 40), SearchResultWidth.Measure("Wi n", SearchResultText.Title));
        Assert.Equal(new SearchResultMeasure(4, 28), SearchResultWidth.Measure("Wi n", SearchResultText.Description));

        // 15,256 units: 148.98 px, so 149; at 14 px 104.29, so 104.
        Assert.Equal(new SearchResultMeasure(15, 149), SearchResultWidth.Measure("Garden Sofa Set", SearchResultText.Title));
        Assert.Equal(new SearchResultMeasure(15, 104), SearchResultWidth.Measure("Garden Sofa Set", SearchResultText.Description));
    }

    [Fact]
    public void WideLettersTakeMoreRoomThanNarrowOnes()
    {
        Assert.Equal(new SearchResultMeasure(3, 57), SearchResultWidth.Measure("WWW", SearchResultText.Title));
        Assert.Equal(new SearchResultMeasure(3, 13), SearchResultWidth.Measure("iii", SearchResultText.Title));
    }

    /// <summary>As a browser renders it: runs of white space are one space and the ends are trimmed; a no-break space stays.</summary>
    [Fact]
    public void WhiteSpaceCollapsesAsABrowserRendersIt()
    {
        Assert.Equal(new SearchResultMeasure(4, 40), SearchResultWidth.Measure("  Wi \t\r\n  n  ", SearchResultText.Title));
        Assert.Equal(new SearchResultMeasure(4, 40), SearchResultWidth.Measure("Wi\u00A0n", SearchResultText.Title));
        // W, i, a no-break space, a space and n: 4,665 units, 45.56 px.
        Assert.Equal(new SearchResultMeasure(5, 46), SearchResultWidth.Measure("Wi\u00A0 n", SearchResultText.Title));
        Assert.Equal(new SearchResultMeasure(0, 0), SearchResultWidth.Measure(" \t ", SearchResultText.Title));
    }

    /// <summary>Combining marks and format characters (a zero-width joiner, a soft hyphen) draw nothing of their own.</summary>
    [Fact]
    public void CombiningMarksAndFormatCharactersAreZero()
    {
        // e 1139 units: 11.12 px.
        Assert.Equal(new SearchResultMeasure(1, 11), SearchResultWidth.Measure("e\u0301", SearchResultText.Title));
        Assert.Equal(new SearchResultMeasure(2, 22), SearchResultWidth.Measure("a\u200Db", SearchResultText.Title));
        // A soft hyphen is a character of its own (a text element), but takes no room.
        Assert.Equal(22, SearchResultWidth.Measure("a\u00ADb", SearchResultText.Title).Pixels);
    }

    /// <summary>Chinese, Japanese and Korean, a family emoji (a ZWJ sequence) and a flag (a pair) are each one em, whole.</summary>
    [Fact]
    public void WideCharactersAndEmojiAreOneEm()
    {
        Assert.Equal(new SearchResultMeasure(3, 60), SearchResultWidth.Measure("日本語", SearchResultText.Title));
        Assert.Equal(new SearchResultMeasure(3, 42), SearchResultWidth.Measure("日本語", SearchResultText.Description));
        Assert.Equal(new SearchResultMeasure(1, 20), SearchResultWidth.Measure("\U0001F468\u200D\U0001F469\u200D\U0001F467", SearchResultText.Title));
        Assert.Equal(new SearchResultMeasure(1, 20), SearchResultWidth.Measure("\U0001F1EC\U0001F1E7", SearchResultText.Title));
        Assert.Equal(new SearchResultMeasure(1, 20), SearchResultWidth.Measure("\u2764\uFE0F", SearchResultText.Title));
    }

    /// <summary>Thai, Devanagari, Arabic: drawn from fallback fonts, so a rough average, the width of n, once per character.</summary>
    [Fact]
    public void ACharacterArialDoesntDrawIsTheWidthOfN()
    {
        Assert.Equal(new SearchResultMeasure(1, 11), SearchResultWidth.Measure("\u0E01", SearchResultText.Title));
        Assert.Equal(new SearchResultMeasure(1, 11), SearchResultWidth.Measure("\u0E01\u0E35", SearchResultText.Title));
        Assert.Equal(new SearchResultMeasure(2, 22), SearchResultWidth.Measure("\u0915\u0916", SearchResultText.Title));
    }

    /// <summary>Halves round up, in whole pixels: 256 units at 20 px is 2.5 px, 768 is 7.5, 512 at 14 px is 3.5.</summary>
    [Theory]
    [InlineData(256, 20, 3)]
    [InlineData(768, 20, 8)]
    [InlineData(255, 20, 2)]
    [InlineData(512, 14, 4)]
    [InlineData(1536, 14, 11)]
    [InlineData(0, 20, 0)]
    public void HalvesRoundUp(long units, int fontPixels, int pixels) => Assert.Equal(pixels, SearchResultWidth.Pixels(units, fontPixels));

    /// <summary>Text within the limit isn't cut, to the pixel the checks compare: a flagged title has a cut, an unflagged one none.</summary>
    [Fact]
    public void TextWithinTheLimitIsNotCut()
    {
        Assert.Null(SearchResultWidth.Cut("Garden Sofa Set", SearchResultText.Title, 149));
        Assert.Equal("Garden Sofa", SearchResultWidth.Cut("Garden Sofa Set", SearchResultText.Title, 148));
        Assert.Null(SearchResultWidth.Cut("", SearchResultText.Title, 1));
    }

    /// <summary>After the last whole word that fits: "Garden Sofa Se" is 143 px, but Google doesn't cut mid-word.</summary>
    [Fact]
    public void TheCutIsAfterTheLastWholeWordThatFits()
    {
        Assert.Equal("Garden Sofa", SearchResultWidth.Cut("Garden Sofa Set", SearchResultText.Title, 145));
        Assert.Equal("Garden", SearchResultWidth.Cut("Garden Sofa Set", SearchResultText.Title, 100));
        Assert.Equal("Garden Sofa", SearchResultWidth.Cut("  Garden   Sofa  Set ", SearchResultText.Title, 145));
    }

    /// <summary>A word followed by a separator is cut before it: "…Garden Sofas", not "…Garden Sofas |".</summary>
    [Theory]
    [InlineData("Garden Sofas | Example Store", 200, "Garden Sofas")]
    [InlineData("Chair Set & More", 120, "Chair Set")]
    [InlineData("Chair Set - More", 120, "Chair Set")]
    [InlineData("Chair Set, More", 120, "Chair Set")]
    public void ADanglingSeparatorIsDropped(string text, int limit, string cut) => Assert.Equal(cut, SearchResultWidth.Cut(text, SearchResultText.Title, limit));

    /// <summary>Chinese, Japanese and Korean break between characters.</summary>
    [Fact]
    public void WideTextIsCutBetweenCharacters()
    {
        Assert.Equal("日本", SearchResultWidth.Cut("日本語のテキスト", SearchResultText.Title, 50));
        Assert.Equal("Tokyo 日本", SearchResultWidth.Cut("Tokyo 日本語", SearchResultText.Title, 100));
    }

    /// <summary>A single word or URL longer than the limit is cut mid-word, after the last character that fits.</summary>
    [Fact]
    public void AWordLongerThanTheLimitIsCutMidWord() =>
        Assert.Equal("Supercal", SearchResultWidth.Cut("Supercalifragilistic", SearchResultText.Title, 80));
}
