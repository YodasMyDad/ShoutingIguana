using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// Step 3b's heading, content and hreflang checks (batch 8 of docs/v2-new-checks.md; title-same-as-h1 isn't built, as
/// ruled): levels skipped, an image H1, long headings, a long page without an H2, placeholder text, hard English, pages
/// that are mostly template, and hreflang codes that disagree, header and HTML sets that differ, versions only hreflang
/// leads to, a code against lang, and a set naming only the page.
/// </summary>
public sealed class TextHreflangCheckTests
{
    private static readonly AnalysisSettings SETTINGS = new();
    private const string HARD = "Institutional organisations systematically underestimate the considerable organisational complexity inherent in multidimensional international regulatory infrastructure. ";

    [Fact]
    public void TextIsMeasuredInSentencesSyllablesAndWords()
    {
        Assert.Equal((2, 5, 5), TextMeasures.SentencesAndSyllables("The cat sat. It ran!"));
        // An abbreviation's stop ends a sentence too, as Flesch counts by hand would not: an estimate, over a page of prose.
        Assert.Equal((2, 8, 8), TextMeasures.SentencesAndSyllables("A table made... of e.g. 3 legs"));
        // A block with no sentence end is no prose: its words and syllables are left out.
        Assert.Equal((1, 3, 3), TextMeasures.SentencesAndSyllables($"Garden benches{TextAccumulator.BLOCK_MARK}The cat sat.{TextAccumulator.BLOCK_MARK}Oak table 120cm"));
        Assert.Equal(4, TextMeasures.Words("Four words, here: 42"));
        Assert.Equal("Lorem ipsum dolor sit amet", TextMeasures.Placeholder("Intro. Lorem   ipsum dolor sit amet"));
        Assert.Null(TextMeasures.Placeholder("No placeholder here"));
    }

    [Theory]
    [InlineData(new[] { 1, 2, 3, 2, 3 }, null)]
    [InlineData(new[] { 1, 3 }, "H1 → H3")]
    [InlineData(new[] { 1, 2, 4 }, "H2 → H4")]
    // Headings before the H1 (a theme's hidden cart, a menu's column titles) aren't judged, as ruled: only from the H1 on.
    [InlineData(new[] { 3, 1, 2 }, null)]
    [InlineData(new[] { 2, 4, 6, 1, 2 }, null)]
    [InlineData(new[] { 6, 6, 1, 3 }, "H1 → H3")]
    [InlineData(new int[0], null)]
    public void AHeadingMoreThanOneLevelDownIsASkip(int[] levels, string? skip)
    {
        Assert.Equal(skip, TextStructureRules.SkippedLevel([.. levels.Select(level => new HeadingFact(level, "A"))]));
    }

    [Fact]
    public void HeadingsAreJudgedOnIndexablePagesWithAnH1()
    {
        Assert.Equal("H1 → H3: \"Third\"", Detail("<h1>Main</h1><h3>Third</h3>", IssueCatalog.HEADING_LEVELS_SKIPPED));
        Assert.Null(Detail("<h2>No H1 at all</h2><h4>Is h1-missing's</h4>", IssueCatalog.HEADING_LEVELS_SKIPPED));
        Assert.Null(Detail("<h6>Your cart</h6><h1>Main</h1><h2>Part</h2>", IssueCatalog.HEADING_LEVELS_SKIPPED));
        Assert.Equal("The H1 is an image: \"Store logo\"", Detail("<h1><img src=\"/l.png\" alt=\"Store logo\"></h1>", IssueCatalog.H1_IMAGE_ONLY));
        Assert.Null(Detail("<h1><img src=\"/l.png\" alt=\"Store logo\"> Sofas</h1>", IssueCatalog.H1_IMAGE_ONLY));
        // An image with an empty alt gives the H1 no text at all: h1-missing says so, and there's no alt to quote here.
        Assert.Null(Detail("<h1><img src=\"/l.png\" alt=\"\"></h1>", IssueCatalog.H1_IMAGE_ONLY));
        Assert.Equal($"H2 of 72 characters, over 70: \"{new string('a', 72)}\"", Detail($"<h1>Short</h1><h2>{new string('a', 72)}</h2>", IssueCatalog.HEADING_TOO_LONG));
        Assert.Null(Detail($"<h1>Short</h1><h3>{new string('a', 90)}</h3>", IssueCatalog.HEADING_TOO_LONG));
        var words = string.Join(' ', Enumerable.Repeat("word", 520));
        Assert.Equal("521 words, no H2", Detail($"<h1>Long</h1><p>{words}</p>", IssueCatalog.H2_MISSING));
        Assert.Null(Detail($"<h1>Long</h1><h2>Part</h2><p>{words}</p>", IssueCatalog.H2_MISSING));
        Assert.Null(Detail("<h1>Main</h1><h3>Third</h3>", IssueCatalog.HEADING_LEVELS_SKIPPED, IndexabilityReason.Noindex));
    }

    /// <summary>
    /// The real-site audit's case: 98 pages said only "H1 → H4", never which heading. The detail quotes the heading skipped
    /// to, and its group is the step alone, so a template's pages are one group whatever their headings say.
    /// </summary>
    [Fact]
    public void ASkippedLevelQuotesTheHeadingAndGroupsByTheStep()
    {
        var detail = Detail("<h1>Garden sofas</h1><h4>Out Of Stock</h4><h2>Details</h2>", IssueCatalog.HEADING_LEVELS_SKIPPED);
        Assert.Equal("H1 → H4: \"Out Of Stock\"", detail);
        Assert.Equal("H1 → H4", IssueCatalog.HEADING_LEVELS_SKIPPED.FaultOf!(detail!));
        Assert.Equal("H1 → H4", IssueCatalog.HEADING_LEVELS_SKIPPED.FaultOf!("H1 → H4"));
        Assert.Equal(18, IssueCatalog.HEADING_LEVELS_SKIPPED.FaultsSince);

        // A heading with no text has nothing to quote; a long one is cut.
        Assert.Equal("H2 → H4", Detail("<h1>Main</h1><h2>Part</h2><h4></h4>", IssueCatalog.HEADING_LEVELS_SKIPPED));
        Assert.Equal($"H1 → H3: \"{new string('a', 59)}…\"", Detail($"<h1>Main</h1><h3>{new string('a', 90)}</h3>", IssueCatalog.HEADING_LEVELS_SKIPPED));
    }

    /// <summary>
    /// The real-site audit's case: an H2 too long was shown beside the page's H1, which was short, so the heading to
    /// shorten was nowhere. The detail quotes it, as much of it as fits the detail's 120 characters.
    /// </summary>
    [Fact]
    public void AHeadingTooLongIsQuotedInTheDetail()
    {
        const string BENCH = "Toddington Steel Garden Bench Heavy-Duty Powder Coated 3 Seater in Antique Bronze Finish";
        Assert.Equal($"H2 of 88 characters, over 70: \"{BENCH}\"", Detail($"<h1>Garden benches</h1><h2>{BENCH}</h2>", IssueCatalog.HEADING_TOO_LONG));

        var detail = Detail($"<h1>{string.Join(' ', Enumerable.Repeat("Sofa", 60))}</h1>", IssueCatalog.HEADING_TOO_LONG)!;
        Assert.Equal(RuleText.DETAIL_MAX, detail.Length);
        Assert.StartsWith("H1 of 299 characters, over 70: \"Sofa Sofa", detail, StringComparison.Ordinal);
        Assert.EndsWith("…\"", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaceholderTextHardEnglishAndPagesMostlyTemplateAreFlagged()
    {
        Assert.Equal("\"Lorem ipsum dolor sit amet, consectetur.\"", Detail("<h1>A</h1><p>Lorem ipsum dolor sit amet, consectetur.</p>", IssueCatalog.PLACEHOLDER_TEXT));
        Assert.Equal("\"Lorem Ipsum product\"", Detail("<h1>A</h1><p>Text</p>", IssueCatalog.PLACEHOLDER_TEXT, head: "<title>Lorem Ipsum product</title>"));

        var hard = $"<h1>Hard</h1><p>{string.Concat(Enumerable.Repeat(HARD, 10))}</p>";
        Assert.Matches(@"^Reading ease -?\d+, under 30 \(very difficult\)$", Detail(hard, IssueCatalog.READABILITY_LOW));
        Assert.Null(Detail(hard, IssueCatalog.READABILITY_LOW, lang: " lang=\"de\""));
        Assert.Null(Detail($"<h1>Hard</h1><p>{HARD}</p>", IssueCatalog.READABILITY_LOW));
        Assert.Null(Detail($"<h1>Easy</h1><p>{string.Concat(Enumerable.Repeat("The cat sat on the mat. It was a good day for it. ", 12))}</p>", IssueCatalog.READABILITY_LOW));

        var own = string.Join(' ', Enumerable.Repeat("garden", 209));
        var menu = string.Join(' ', Enumerable.Repeat("link", 750));
        Assert.Equal("210 of 960 words are the page's own (22%)", Detail($"<h1>Own</h1><p>{own}</p><nav>{menu}</nav>", IssueCatalog.BOILERPLATE_HEAVY));
        Assert.Null(Detail($"<h1>Few</h1><p>Too few words</p><nav>{menu}</nav>", IssueCatalog.BOILERPLATE_HEAVY));

        // A <noscript>'s content is markup the parser keeps as text (scripting is on): a lazy image's fallback is no word of
        // the page's. 250 own words and a 300-word menu are 45% the page's own, whatever the fallbacks say.
        var fallbacks = string.Concat(Enumerable.Repeat(
            "<img class=\"lazy\" data-src=\"/a-800.jpg\" alt=\"Sofa\"><noscript><img src=\"https://cdn.example.com/a-800.jpg\" alt=\"A garden sofa\" class=\"lazy\" width=\"800\" height=\"600\"></noscript>", 60));
        Assert.Null(Detail($"<h1>Own</h1><p>{string.Join(' ', Enumerable.Repeat("garden", 249))}</p>{fallbacks}<nav>{string.Join(' ', Enumerable.Repeat("link", 300))}</nav>",
            IssueCatalog.BOILERPLATE_HEAVY));
    }

    /// <summary>
    /// Flesch's reading ease is a measure of sentences: text with no sentence end (a specification list, a product grid, a
    /// code sample, a heading) is no prose, and its words belong to no sentence. They made every real sentence look longer.
    /// </summary>
    [Fact]
    public void ReadabilityJudgesOnlyTheBlocksOfTextThatHaveSentences()
    {
        var prose = string.Concat(Enumerable.Repeat("<p>The cat sat on the mat. It was a good day for it. We all had a very nice time at home.</p>", 6));
        var list = $"<ul>{string.Concat(Enumerable.Repeat("<li>Rectangular aluminium extending table 180cm</li>", 150))}</ul>";
        var code = $"<pre>{string.Concat(Enumerable.Repeat("public override void Initialise(ApplicationContext applicationContext)\n", 40))}</pre>";

        Assert.Null(Detail($"<h1>Easy</h1>{prose}{list}", IssueCatalog.READABILITY_LOW));
        Assert.Null(Detail($"<h1>Easy</h1>{prose}{code}", IssueCatalog.READABILITY_LOW));
        // Hard prose is still hard beside a list.
        Assert.NotNull(Detail($"<h1>Hard</h1><p>{string.Concat(Enumerable.Repeat(HARD, 10))}</p>{list}", IssueCatalog.READABILITY_LOW));

        var facts = HtmlPageParser.Parse(RuleHarness.Html(body: $"<h1>Easy</h1>{prose}{list}"), new Uri("https://example.com/"), [], "ShoutingIguana").Facts;
        Assert.Equal((18, 132), (facts.SentenceCount, facts.ProseWordCount));
        Assert.Equal(132 + 1 + 150 * 5, facts.WordCount);
    }

    [Fact]
    public void HreflangSetsAreComparedWithTheHeaderLangAndThePageItself()
    {
        var site = new RuleHarness();
        string Set(params (string Code, string Path)[] entries) =>
            string.Concat(entries.Select(entry => $"<link rel=\"alternate\" hreflang=\"{entry.Code}\" href=\"{site.Site}{entry.Path}\">"));

        var header = site.Page($"{site.Site}/a", RuleHarness.Html(Set(("en", "/a"), ("de", "/b"), ("x-default", "/a"))),
            headers: [new ResponseHeader("link", $"<{site.Site}/a>; rel=\"alternate\"; hreflang=\"en\", <{site.Site}/b>; rel=\"alternate\"; hreflang=\"de\"")]);
        var lang = site.Page($"{site.Site}/c", RuleHarness.Html(Set(("en", "/c"), ("fr", "/d")), lang: " lang=\"fr\""));
        var region = site.Page($"{site.Site}/e", RuleHarness.Html(Set(("en-GB", "/e"), ("fr", "/d")), lang: " lang=\"en-US\""));
        // Norwegian is "no" in hreflang and usually "nb" in lang, and Filipino "tl" and "fil": one language, two codes.
        var norwegian = site.Page($"{site.Site}/g", RuleHarness.Html(Set(("no", "/g"), ("en", "/a")), lang: " lang=\"nb-NO\""));
        var filipino = site.Page($"{site.Site}/h", RuleHarness.Html(Set(("tl", "/h"), ("en", "/a")), lang: " lang=\"fil\""));
        var self = site.Page($"{site.Site}/f", RuleHarness.Html(Set(("en", "/f"), ("x-default", "/f"))));
        var rules = new HreflangRules(site.Table());

        Assert.Equal("The HTML names 3 versions, the header 2; only in the HTML: x-default", Detail(rules.Check(header), IssueCatalog.HREFLANG_HEADER_MISMATCH));
        Assert.Equal("hreflang en, lang fr", Detail(rules.Check(lang), IssueCatalog.HREFLANG_LANG_MISMATCH));
        Assert.Null(Detail(rules.Check(region), IssueCatalog.HREFLANG_LANG_MISMATCH));
        Assert.Null(Detail(rules.Check(norwegian), IssueCatalog.HREFLANG_LANG_MISMATCH));
        Assert.Null(Detail(rules.Check(filipino), IssueCatalog.HREFLANG_LANG_MISMATCH));
        Assert.Contains(rules.Check(self), hit => hit.Issue == IssueCatalog.HREFLANG_SELF_ONLY);
        Assert.DoesNotContain(rules.Check(lang), hit => hit.Issue == IssueCatalog.HREFLANG_SELF_ONLY);
    }

    [Fact]
    public void AnAlternateCallingItselfByAnotherCodeAndAVersionOnlyHreflangLeadsToAreFlagged()
    {
        var site = new RuleHarness();
        string Set(params (string Code, string Path)[] entries) =>
            string.Concat(entries.Select(entry => $"<link rel=\"alternate\" hreflang=\"{entry.Code}\" href=\"{site.Site}{entry.Path}\">"));
        var a = site.Page($"{site.Site}/a", RuleHarness.Html(Set(("en", "/a"), ("fr", "/b"), ("x-default", "/a")), body: "<a href=\"/b\">B</a>"));
        var b = site.Page($"{site.Site}/b", RuleHarness.Html(Set(("en", "/a"), ("fr-CA", "/b"), ("x-default", "/a")), body: "<a href=\"/a\">A</a>"));
        var c = site.Page($"{site.Site}/c", RuleHarness.Html(Set(("en", "/c"), ("es", "/d"))));
        var d = site.Page($"{site.Site}/d", RuleHarness.Html(Set(("en", "/c"), ("es", "/d"))));
        var urls = site.Table();
        var rules = new HreflangRules(urls, site.UrlOf);
        foreach (var page in new[] { a, b, c, d })
        {
            urls.CountInlinks(page);
            rules.Observe(page);
        }

        var hits = rules.Finish().ToList();
        var inconsistent = Assert.Single(hits, hit => hit.Issue == IssueCatalog.HREFLANG_INCONSISTENT_CODE);
        Assert.Equal((a.UrlId, "Names /b as \"fr\"; /b calls itself \"fr-CA\""), (inconsistent.UrlId, inconsistent.Detail));
        Assert.Equal([c.UrlId, d.UrlId], hits.Where(hit => hit.Issue == IssueCatalog.HREFLANG_UNLINKED).Select(hit => hit.UrlId).Order());
    }


    private static string? Detail(string body, IssueDefinition check, IndexabilityReason reason = IndexabilityReason.None, string head = "", string lang = " lang=\"en\"")
    {
        var site = new RuleHarness();
        var page = site.Page($"{site.Site}/page", RuleHarness.Html(head, body, lang), reason);
        return Detail(new TextStructureRules(SETTINGS).Check(page), check);
    }

    private static string? Detail(IEnumerable<IssueHit> hits, IssueDefinition check) => hits.Where(hit => hit.Issue == check).Select(hit => hit.Detail).SingleOrDefault();
}
