using System.Text.Json;
using System.Text.Json.Nodes;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Serialization;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// The limits a plain text names as <c>{limit:name}</c>, filled from an analysis's settings with their units, as a sentence
/// says them. Every numeric threshold has a name; a name that is none of them is an error.
/// </summary>
public sealed class PlainLimitsTests
{
    private static readonly AnalysisSettings DEFAULTS = new();

    /// <summary>
    /// Every numeric threshold the analysis settings have, by the name their JSON gives it: a new threshold must be given
    /// its unit in <see cref="PlainLimits"/> before a text can name it, and this fails until it is.
    /// </summary>
    [Fact]
    public void EveryNumericThresholdHasAName()
    {
        var stored = JsonSerializer.SerializeToNode(DEFAULTS, EngineJsonContext.Default.AnalysisSettings)!.AsObject();
        var numeric = stored.Where(property => property.Value is JsonValue value && value.GetValueKind() == JsonValueKind.Number).Select(property => property.Key);

        Assert.Equal(numeric.Order(StringComparer.Ordinal), PlainLimits.Names.Order(StringComparer.Ordinal));
        Assert.Equal(20, PlainLimits.Names.Count);
    }

    /// <summary>Each threshold at its default, with its unit, as a sentence gives it.</summary>
    [Theory]
    [InlineData("titleMinPixels", "about 20 characters")]
    [InlineData("titleMaxPixels", "about 60 characters")]
    [InlineData("descriptionMinPixels", "about 65 characters")]
    [InlineData("descriptionMaxPixels", "about 160 characters")]
    [InlineData("minWordCount", "200 words")]
    [InlineData("duplicateMinWordCount", "50 words")]
    [InlineData("nearDuplicateMaxBits", "24 bits")]
    [InlineData("imageAltMaxLength", "125 characters")]
    [InlineData("largeImageBytes", "200 KB")]
    [InlineData("responsiveImageMinWidth", "800 pixels")]
    [InlineData("legacyImageMinBytes", "50 KB")]
    [InlineData("inlineImageMaxBytes", "10 KB")]
    [InlineData("largePageBytes", "1 MB")]
    [InlineData("slowResponseMs", "1 second")]
    [InlineData("maxUrlLength", "115 characters")]
    [InlineData("minInternalOutlinks", "3 links")]
    [InlineData("maxClickDepth", "4 clicks")]
    [InlineData("maxInternalLinks", "1,000 links")]
    [InlineData("maxExternalLinks", "100 links")]
    [InlineData("slowExternalLinkMs", "3 seconds")]
    public void ALimitIsItsValueWithItsUnit(string name, string described) => Assert.Equal(described, PlainLimits.Describe(name, DEFAULTS));

    /// <summary>The value is the settings', whatever they are: one of something is singular, sizes and times take the unit that reads best.</summary>
    [Fact]
    public void TheValueFollowsTheSettings()
    {
        Assert.Equal("1 character", PlainLimits.Describe("imageAltMaxLength", DEFAULTS with { ImageAltMaxLength = 1 }));

        // A width in pixels says the ordinary characters it holds, never pixels: to the nearest 5 from 20 on, to the nearest whole below.
        Assert.Equal("about 70 characters", PlainLimits.Describe("titleMaxPixels", DEFAULTS with { TitleMaxPixels = 658 }));
        Assert.Equal("about 65 characters", PlainLimits.Describe("titleMaxPixels", DEFAULTS with { TitleMaxPixels = 612 }));
        Assert.Equal("about 19 characters", PlainLimits.Describe("titleMinPixels", DEFAULTS with { TitleMinPixels = 183 }));
        Assert.Equal("about 1 character", PlainLimits.Describe("titleMinPixels", DEFAULTS with { TitleMinPixels = 9 }));
        Assert.Equal("about 100 characters", PlainLimits.Describe("descriptionMaxPixels", DEFAULTS with { DescriptionMaxPixels = 620 }));
        Assert.Equal("1,200 characters", PlainLimits.Describe("maxUrlLength", DEFAULTS with { MaxUrlLength = 1_200 }));
        Assert.Equal("1 word", PlainLimits.Describe("minWordCount", DEFAULTS with { MinWordCount = 1 }));
        Assert.Equal("0 words", PlainLimits.Describe("duplicateMinWordCount", DEFAULTS with { DuplicateMinWordCount = 0 }));

        Assert.Equal("1.5 seconds", PlainLimits.Describe("slowResponseMs", DEFAULTS with { SlowResponseMs = 1_500 }));
        Assert.Equal("0.8 seconds", PlainLimits.Describe("slowResponseMs", DEFAULTS with { SlowResponseMs = 800 }));
        Assert.Equal("2 seconds", PlainLimits.Describe("slowResponseMs", DEFAULTS with { SlowResponseMs = 2_000 }));
        Assert.Equal("0.25 seconds", PlainLimits.Describe("slowResponseMs", DEFAULTS with { SlowResponseMs = 250 }));

        Assert.Equal("100 KB", PlainLimits.Describe("largeImageBytes", DEFAULTS with { LargeImageBytes = 100 * 1024 }));
        Assert.Equal("1.5 KB", PlainLimits.Describe("largeImageBytes", DEFAULTS with { LargeImageBytes = 1_536 }));
        Assert.Equal("512 bytes", PlainLimits.Describe("largeImageBytes", DEFAULTS with { LargeImageBytes = 512 }));
        Assert.Equal("1 byte", PlainLimits.Describe("largeImageBytes", DEFAULTS with { LargeImageBytes = 1 }));
        Assert.Equal("1.5 MB", PlainLimits.Describe("largePageBytes", DEFAULTS with { LargePageBytes = 1_536 * 1024 }));
        Assert.Equal("2 MB", PlainLimits.Describe("largePageBytes", DEFAULTS with { LargePageBytes = 2 * 1024 * 1024 }));
        Assert.Equal("3 GB", PlainLimits.Describe("largePageBytes", DEFAULTS with { LargePageBytes = 3L * 1024 * 1024 * 1024 }));

        // By the rounded number: a byte under 1 MB is "1 MB", never "1024 KB".
        Assert.Equal("1 MB", PlainLimits.Describe("largePageBytes", DEFAULTS with { LargePageBytes = 1024 * 1024 - 1 }));
        Assert.Equal("1,023.9 KB", PlainLimits.Describe("largePageBytes", DEFAULTS with { LargePageBytes = 1024 * 1024 - 100 }));
    }

    [Fact]
    public void ATextIsFilledWhereverItNamesALimit()
    {
        const string TEXT = "These pages have a title wider than {limit:titleMaxPixels}, or took over {limit:slowResponseMs} to arrive.";

        Assert.Equal("These pages have a title wider than about 60 characters, or took over 1 second to arrive.", PlainLimits.Fill(TEXT, DEFAULTS));
        Assert.Equal("These pages have a title wider than about 70 characters, or took over 2.5 seconds to arrive.",
            PlainLimits.Fill(TEXT, DEFAULTS with { TitleMaxPixels = 658, SlowResponseMs = 2_500 }));

        // A text that names none is the same text, and other braces are left alone.
        const string PLAIN = "Every page has a title";
        Assert.Same(PLAIN, PlainLimits.Fill(PLAIN, DEFAULTS));
        Assert.Equal("A {placeholder} in a page", PlainLimits.Fill("A {placeholder} in a page", DEFAULTS));
    }

    /// <summary>A limit no setting has is an error that names it, never a text left with its token or filled with a guess.</summary>
    [Theory]
    [InlineData("{limit:titleLength}", "titleLength")]
    [InlineData("{limit:TitleMaxPixels}", "TitleMaxPixels")]
    [InlineData("{limit:titleMaxLength}", "titleMaxLength")]
    [InlineData("{limit:}", "''")]
    [InlineData("Over {limit: titleMaxLength}.", "' titleMaxLength'")]
    public void ALimitNoSettingHasIsAnError(string text, string named)
    {
        var error = Assert.Throws<ArgumentException>(() => PlainLimits.Fill(text, DEFAULTS));

        Assert.Contains(named, error.Message, StringComparison.Ordinal);
        Assert.Contains("titleMaxPixels", error.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => PlainLimits.Describe("noSuchLimit", DEFAULTS));
    }

    /// <summary>A check's plain words are filled in every part, and a skip reason's in both.</summary>
    [Fact]
    public void EveryPartOfACheckAndOfASkipReasonIsFilled()
    {
        var written = new IssuePlainText("Titles over {limit:titleMaxPixels}",
            ["These pages have a title wider than {limit:titleMaxPixels}.", "Search results cut it off."],
            ["Trim it to {limit:titleMaxPixels}."], IssueFixer.You, "No title is over {limit:titleMaxPixels}")
        {
            OneFix = "That's usually a slogan of over {limit:titleMinPixels}.", LooksFor = "Titles over {limit:titleMaxPixels}"
        };

        var filled = PlainLimits.Fill(written, DEFAULTS with { TitleMaxPixels = 612 });

        Assert.Equal(("Titles over about 65 characters", "No title is over about 65 characters", "That's usually a slogan of over about 20 characters.",
                "Titles over about 65 characters", IssueFixer.You),
            (filled.Title, filled.Passed, filled.OneFix, filled.LooksFor, filled.Fixer));
        Assert.Equal(["These pages have a title wider than about 65 characters.", "Search results cut it off."], filled.What);
        Assert.Equal(["Trim it to about 65 characters."], filled.Fix);

        // The optional parts stay absent, and a check or reason with no plain words stays without.
        var bare = PlainLimits.Fill(written with { OneFix = null, LooksFor = null }, DEFAULTS);
        Assert.Equal(((string?)null, (string?)null), (bare.OneFix, bare.LooksFor));
        Assert.Null(PlainLimits.Fill((IssuePlainText?)null, DEFAULTS));
        Assert.Null(PlainLimits.Fill((SkipReasonPlainText?)null, DEFAULTS));

        Assert.Equal(new SkipReasonPlainText("Pages under 100 words weren't compared.", "Check again once pages have 100 words."),
            PlainLimits.Fill(new SkipReasonPlainText("Pages under {limit:duplicateMinWordCount} weren't compared.", "Check again once pages have {limit:duplicateMinWordCount}."),
                DEFAULTS with { DuplicateMinWordCount = 100 }));
    }
}
