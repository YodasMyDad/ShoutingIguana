using System.Text.Json;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Parsing;

/// <summary>Custom extraction reads the document the page was parsed into, and stores nothing in the facts the checks read.</summary>
public sealed class CustomExtractionTests
{
    private static readonly ExtractionRule[] RULES =
    [
        new() { Name = "Price", SelectorType = ExtractionSelector.Css, Selector = ".plant-price" },
        new() { Name = "Sku", SelectorType = ExtractionSelector.XPath, Selector = "//span[@id='plant-sku']" },
        new() { Name = "Code", SelectorType = ExtractionSelector.Regex, Selector = @"extract-code:\s*(\S+)" }
    ];

    [Fact]
    public void ThePlantedPageYieldsItsThreeValues()
    {
        var parsed = ParsePlant(RULES);

        Assert.Equal(
            [
                ("Price", SyntheticSite.CUSTOM_EXTRACT_PRICE, 1),
                ("Sku", SyntheticSite.CUSTOM_EXTRACT_SKU, 1),
                ("Code", SyntheticSite.CUSTOM_EXTRACT_CODE, 1)
            ],
            parsed.Extractions.Select(match => (match.Name, match.Values.Single(), match.Count)).ToArray());
        Assert.All(parsed.Extractions, match => Assert.Null(match.Error));
    }

    [Fact]
    public void ARuleThatMatchesNothingIsLeftOutAndADisabledRuleIsNotApplied()
    {
        ExtractionRule[] rules =
        [
            .. RULES,
            new() { Name = "Missing", SelectorType = ExtractionSelector.Css, Selector = ".not-on-the-page" },
            new() { Name = "Off", SelectorType = ExtractionSelector.Css, Selector = ".plant-price", Enabled = false }
        ];

        var names = ParsePlant(rules).Extractions.Select(match => match.Name).ToArray();

        Assert.Equal(["Price", "Sku", "Code"], names);
    }

    [Fact]
    public void ExtractedTextIsNotStoredWithTheFactsTheChecksRead()
    {
        var parsed = ParsePlant(RULES);
        var json = JsonSerializer.Serialize(new ResponseDetails([], parsed.Facts), EngineJsonContext.Default.ResponseDetails);

        Assert.DoesNotContain("plant-price", json, StringComparison.Ordinal);
        Assert.DoesNotContain(SyntheticSite.CUSTOM_EXTRACT_CODE, json, StringComparison.Ordinal);
    }

    [Fact]
    public void ARegularExpressionWithoutACaptureGroupKeepsTheWholeMatch()
    {
        var session = CustomExtractor.Open([new ExtractionRule { Name = "Whole", SelectorType = ExtractionSelector.Regex, Selector = "SKU-4401" }]);
        var match = session.Apply(Document("<p>SKU-4401</p>"), "<p>SKU-4401</p>").Single();

        Assert.Equal("SKU-4401", match.Values.Single());
    }

    [Fact]
    public void ARuleThatFailsIsReportedOnceAndThenSkipped()
    {
        var session = CustomExtractor.Open([new ExtractionRule { Name = "Bad", SelectorType = ExtractionSelector.Css, Selector = "???" }]);
        var document = Document("<p>x</p>");

        var first = session.Apply(document, "<p>x</p>").Single();
        var second = session.Apply(document, "<p>x</p>");

        Assert.Contains("was not tried on later pages", first.Error, StringComparison.Ordinal);
        Assert.Empty(second);
    }

    [Fact]
    public void SeveralMatchesAreKeptAndTheCountSaysHowManyThereWere()
    {
        var html = string.Concat(Enumerable.Range(0, 60).Select(index => $"<li class=\"item\">Item {index}</li>"));
        var match = CustomExtractor.Open([new ExtractionRule { Name = "Items", SelectorType = ExtractionSelector.Css, Selector = ".item" }])
            .Apply(Document(html), html).Single();

        Assert.Equal(60, match.Count);
        Assert.Equal(CustomExtractor.MAX_VALUES, match.Values.Count);
        Assert.Equal("Item 0", match.Values[0]);
    }

    [Fact]
    public void ARuleWithABadSelectorOrARepeatedNameIsRefused()
    {
        var broken = new CrawlOptions
        {
            StartUrl = "https://example.test/",
            ExtractionRules =
            [
                new() { Name = "Price", SelectorType = ExtractionSelector.Css, Selector = "???" },
                new() { Name = "Price", SelectorType = ExtractionSelector.Css, Selector = "h1" },
                new() { Name = "Code", SelectorType = ExtractionSelector.Regex, Selector = "(" }
            ]
        };

        var problems = broken.Validate();

        Assert.Contains(problems, problem => problem.Contains("CSS selector", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("named 'Price'", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("regular expression", StringComparison.Ordinal));
    }

    [Fact]
    public void TheCommandLineFormIsNameThenTypeThenSelector()
    {
        Assert.True(ExtractionRule.TryParse("Price=css:.plant-price", out var css, out _));
        Assert.Equal(ExtractionSelector.Css, css.SelectorType);
        Assert.Equal(".plant-price", css.Selector);

        Assert.True(ExtractionRule.TryParse("Sku=xpath://span[@id='plant-sku']", out var xpath, out _));
        Assert.Equal("//span[@id='plant-sku']", xpath.Selector);

        Assert.True(ExtractionRule.TryParse(@"Code=regex:extract-code:\s*(\S+)", out var regex, out _));
        Assert.Equal(@"extract-code:\s*(\S+)", regex.Selector);

        Assert.False(ExtractionRule.TryParse("just a name", out _, out var error));
        Assert.Contains("Name=css:selector", error, StringComparison.Ordinal);
    }

    private static ParsedPage ParsePlant(IReadOnlyList<ExtractionRule> rules)
    {
        var site = new SyntheticSite(new SyntheticSiteOptions { PageCount = 2 }, new Uri("https://example.test/"), new Uri("https://partner.test/"));
        var html = site.Handle(SyntheticSite.CUSTOM_EXTRACT_PATH, external: false).Text!;
        return HtmlPageParser.Parse(html, new Uri("https://example.test" + SyntheticSite.CUSTOM_EXTRACT_PATH), [], "ShoutingIguana", byteOrderMark: null, extractions: CustomExtractor.Open(rules));
    }

    private static AngleSharp.Html.Dom.IHtmlDocument Document(string body)
    {
        return new AngleSharp.Html.Parser.HtmlParser().ParseDocument($"<html><body>{body}</body></html>");
    }
}
