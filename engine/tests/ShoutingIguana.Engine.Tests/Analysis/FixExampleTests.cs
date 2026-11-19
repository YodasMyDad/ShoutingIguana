using System.Text.RegularExpressions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Sitemaps;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// The catalog's fixes as code (<see cref="IssueDefinition.FixExamples"/>): how they're written, and, where a check
/// needs only its page, that the code as it is fires the check and the corrected code clears it, through the parser and
/// the rules a crawl's analysis runs.
/// </summary>
public sealed class FixExampleTests
{
    private const string SITE = "https://www.example.com";
    private static readonly AnalysisSettings SETTINGS = new();

    public static TheoryData<string, int> Examples()
    {
        TheoryData<string, int> rows = [];
        foreach (var check in IssueCatalog.All)
        {
            for (var index = 0; index < check.FixExamples.Count; index++)
            {
                rows.Add(check.Id, index);
            }
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void AnExampleIsShortCodeWithNothingAroundIt(string checkId, int index)
    {
        var example = IssueCatalog.Find(checkId)!.FixExamples[index];

        Assert.False(string.IsNullOrWhiteSpace(example.After));
        foreach (var code in new[] { example.After, example.Before }.OfType<string>())
        {
            var lines = code.Split('\n');
            Assert.InRange(lines.Length, 1, 4);
            Assert.False(string.IsNullOrWhiteSpace(lines[0]), "starts with a blank line");
            Assert.False(string.IsNullOrWhiteSpace(lines[^1]), "ends with a blank line");
            Assert.DoesNotContain('\r', code);
            Assert.DoesNotContain('\t', code);
            Assert.All(lines, line => Assert.Equal(line.TrimEnd(), line));

            // Elided content is the one character, never three dots.
            Assert.DoesNotContain("...", code, StringComparison.Ordinal);
        }

        Assert.NotEqual(example.After, example.Before);
        Assert.True(Enum.IsDefined(example.Language));
    }

    /// <summary>
    /// A host shows a case's <c>When</c> as the name of a choice and a <c>Where</c> after a label ("Add · in the page's
    /// &lt;head&gt;"), so the first is a sentence without its full stop and the second starts in lower case.
    /// </summary>
    [Theory]
    [MemberData(nameof(Examples))]
    public void AnExamplesWordsFitWhereAHostShowsThem(string checkId, int index)
    {
        var example = IssueCatalog.Find(checkId)!.FixExamples[index];

        if (example.When is { } when)
        {
            Assert.False(string.IsNullOrWhiteSpace(when));
            Assert.True(char.IsUpper(when[0]), when);
            Assert.False(when.EndsWith('.'), when);
            Assert.InRange(when.Length, 1, 80);
        }

        if (example.Where is { } where)
        {
            Assert.False(string.IsNullOrWhiteSpace(where));
            Assert.True(char.IsLower(where[0]), where);
            Assert.False(where.EndsWith('.'), where);
        }

        if (example.Case is { } key)
        {
            Assert.Matches(new Regex("^[a-z0-9]+(-[a-z0-9]+)*$"), key);
        }
    }

    [Fact]
    public void ACheckWithSeveralExamplesNamesEachCase()
    {
        Assert.All(IssueCatalog.All.Where(check => check.FixExamples.Count > 1), check =>
        {
            Assert.All(check.FixExamples, example => Assert.False(string.IsNullOrWhiteSpace(example.When), check.Id));
            Assert.All(check.FixExamples, example => Assert.NotNull(example.Case));
            Assert.Equal(check.FixExamples.Count, check.FixExamples.Select(example => example.Case).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(check.FixExamples.Count, check.FixExamples.Select(example => example.When).Distinct(StringComparer.Ordinal).Count());
        });

        // One example has no case to tell apart.
        Assert.All(IssueCatalog.All.Where(check => check.FixExamples.Count == 1), check => Assert.Null(check.FixExamples[0].Case));
    }

    /// <summary>
    /// The owner's case: a logo that is an inline SVG in a link, on every page. The cases come likeliest first, and the
    /// prose names what works for an SVG, where alt text doesn't apply.
    /// </summary>
    [Fact]
    public void AnEmptyLinkHasItsThreeCasesAndAnSvgLogoIsSaid()
    {
        var check = IssueCatalog.EMPTY_ANCHOR_TEXT;

        Assert.Equal(["icon-link", "image-link", "empty-link"], check.FixExamples.Select(example => example.Case));
        Assert.Equal(["The link wraps an icon or an inline SVG", "The link wraps an image", "The link has nothing inside"],
            check.FixExamples.Select(example => example.When));
        Assert.All(check.FixExamples, example => Assert.Equal(FixLanguage.Html, example.Language));
        Assert.Contains("aria-label=", check.FixExamples[0].After, StringComparison.Ordinal);
        Assert.Contains("<svg", check.FixExamples[0].Before, StringComparison.Ordinal);
        Assert.Contains("alt=\"Example Furniture\"", check.FixExamples[1].After, StringComparison.Ordinal);

        Assert.DoesNotContain("alt text with the site name is enough", check.Fix, StringComparison.Ordinal);
        Assert.EndsWith(
            "For a logo that links home, the site name is enough: as alt text on an <img>, or for an inline SVG as an aria-label on the link or a <title> inside the SVG.",
            check.Fix);
    }

    /// <summary>A fix that is editorial or structural keeps its prose alone: nobody can paste a shorter title.</summary>
    [Theory]
    [InlineData("title-too-long")]
    [InlineData("low-word-count")]
    [InlineData("redirect-chain")]
    [InlineData("orphan-page")]
    [InlineData("duplicate-content")]
    [InlineData("generic-anchor-text")]
    [InlineData("internal-server-error")]
    public void AnEditorialOrStructuralFixHasNoExample(string checkId) => Assert.Empty(IssueCatalog.Find(checkId)!.FixExamples);

    /// <summary>
    /// The examples of checks that need only their page, each put in a page as it is and as corrected. <c>slot</c> is
    /// where the code goes (<c>head</c>, <c>body</c>, <c>html</c> for the &lt;html&gt; tag itself, <c>headers</c> for the
    /// page's response headers, <c>jsonld</c> for a JSON-LD block's content); <c>around</c> is the rest of the slot, the
    /// code at <c>{code}</c>; <c>elided</c> is what an example's … stands for, when the check reads it.
    /// </summary>
    public static TheoryData<string, string?, string, string, string, string> Proven() => new()
    {
        { "internal-nofollow-links", null, "body", "{code}", "/", "" },
        { "empty-anchor-text", "icon-link", "body", "{code}", "/about/", "<path d=\"M0 0h120v32H0z\"/>" },
        { "empty-anchor-text", "image-link", "body", "{code}", "/about/", "" },
        { "empty-anchor-text", "empty-link", "body", "{code}", "/about/", "" },
        { "uncrawlable-links", "onclick", "body", "{code}", "/", "" },
        { "uncrawlable-links", "javascript-href", "body", "{code}", "/", "" },
        { "uncrawlable-links", "other-element", "body", "{code}", "/", "" },
        { "links-to-localhost", null, "body", "{code}", "/", "" },
        { "noindex-page", null, "head", "{code}", "/oak-dining-tables/", "" },
        { "nofollow-page", null, "head", "{code}", "/oak-dining-tables/", "" },
        { "snippet-blocked", null, "head", "{code}", "/oak-dining-tables/", "" },
        { "media-preview-blocked", null, "head", "{code}", "/oak-dining-tables/", "" },
        { "indexifembedded-without-noindex", null, "head", "{code}", "/oak-dining-tables/", "" },
        { "canonical-missing", null, "head", "{code}", "/oak-dining-tables/", "" },
        { "canonical-multiple", null, "head", "{code}", "/oak-dining-tables/", "" },
        { "canonical-relative", null, "head", "{code}", "/oak-dining-tables/", "" },
        { "canonical-invalid", null, "head", "{code}", "/oak-dining-tables/", "" },
        { "canonical-fragment", null, "head", "{code}", "/oak-dining-tables/", "" },
        { "canonical-invalid-attribute", null, "head", "{code}", "/oak-dining-tables/", "" },
        { "title-missing", null, "head", "{code}", "/oak-dining-tables/", "" },
        { "title-multiple", null, "head", "{code}", "/oak-dining-tables/", "" },
        { "description-missing", null, "head", "{code}", "/oak-dining-tables/", "" },
        { "description-multiple", null, "head", "{code}", "/oak-dining-tables/", "" },
        { "h1-missing", null, "body", "{code}", "/oak-dining-tables/", "" },
        { "h1-multiple", null, "body", "{code}", "/oak-dining-tables/", "" },
        { "heading-levels-skipped", null, "body", "<h1>Oak dining tables</h1>{code}", "/oak-dining-tables/", "" },
        { "h1-image-only", null, "body", "{code}", "/oak-dining-tables/", "" },
        { "image-missing-alt", "content-image", "body", "{code}", "/oak-dining-tables/", "" },
        { "image-missing-alt", "decorative-image", "body", "{code}", "/oak-dining-tables/", "" },
        { "image-missing-dimensions", null, "body", "{code}", "/oak-dining-tables/", "" },
        { "image-missing-src", null, "body", "{code}", "/oak-dining-tables/", "" },
        { "image-no-srcset", null, "body", "{code}", "/oak-dining-tables/", "" },
        { "image-legacy-format", null, "body", "{code}", "/oak-dining-tables/", "" },
        { "image-alt-unhelpful", null, "body", "{code}", "/oak-dining-tables/", "" },
        { "image-inline-large", null, "body", "{code}", "/oak-dining-tables/", new string('A', 20_000) },
        { "hreflang-invalid-code", null, "head", "{code}", "/uk/", "" },
        { "hreflang-missing-self", null, "head", "<link rel=\"alternate\" hreflang=\"fr\" href=\"https://www.example.com/fr/tables-en-chene/\">{code}", "/uk/oak-dining-tables/", "" },
        { "hreflang-missing-x-default", null, "head", "<link rel=\"alternate\" hreflang=\"en-GB\" href=\"https://www.example.com/uk/\">{code}", "/uk/", "" },
        { "structured-data-invalid", null, "jsonld", "{code}", "/oak-dining-tables/", "\"@context\": \"https://schema.org\", \"@type\": \"Product\"," },
        { "structured-data-schema-errors", "spelling", "jsonld", "{ \"@context\": \"https://schema.org\", {code}, \"name\": \"Example Furniture\" }", "/", "" },
        { "structured-data-schema-errors", "date", "jsonld", "{ \"@context\": \"https://schema.org\", \"@type\": \"Article\", {code} }", "/", "" },
        { "structured-data-schema-errors", "currency", "jsonld", "{ \"@context\": \"https://schema.org\", \"@type\": \"Offer\", \"price\": \"499\", {code} }", "/", "" },
        { "structured-data-schema-errors", "context", "jsonld", "{ {code} \"@type\": \"Organization\", \"name\": \"Example Furniture\" }", "/", "" },
        { "mixed-content", null, "body", "{code}", "/oak-dining-tables/", "" },
        { "security-headers-missing", null, "headers", "{code}", "/", "" },
        { "security-headers-weak", "hsts", "headers", "{code}", "/", "" },
        { "security-headers-weak", "nosniff", "headers", "{code}", "/", "" },
        { "cookies-insecure", null, "headers", "{code}", "/oak-dining-tables/", "" },
        { "form-not-secure", null, "body", "{code}<input name=\"email\"></form>", "/oak-dining-tables/", "" },
        { "protocol-relative-links", null, "head", "{code}", "/oak-dining-tables/", "" },
        { "url-parameters", null, "head", "{code}", "/sofas/?sort=price", "" },
        { "url-internal-search", null, "head", "{code}", "/search/?q=oak", "" },
        { "viewport-missing", null, "head", "{code}", "/oak-dining-tables/", "" },
        { "lang-missing", null, "html", "{code}", "/oak-dining-tables/", "" },
        { "lang-invalid", null, "html", "{code}", "/oak-dining-tables/", "" },
        { "charset-missing", "meta-tag", "head", "{code}", "/oak-dining-tables/", "" },
        { "charset-missing", "header", "headers", "{code}", "/oak-dining-tables/", "" },
        { "open-graph-missing", null, "head", "{code}", "/oak-dining-tables/", "" },
        { "x-card-missing", null, "head", "{code}", "/oak-dining-tables/", "" },
        { "x-card-incomplete", null, "head", "<meta property=\"og:title\" content=\"Oak dining tables\"><meta property=\"og:image\" content=\"https://www.example.com/img/share.jpg\">{code}", "/oak-dining-tables/", "" },
        { "social-image-relative", null, "head", "{code}", "/oak-dining-tables/", "" },
        { "og-url-not-canonical", null, "head", "{code}", "/oak-dining-tables/", "" },
        { "social-tags-multiple", null, "head", "{code}", "/oak-dining-tables/", "" },
        { "viewport-invalid", null, "head", "{code}", "/oak-dining-tables/", "" }
    };

    [Theory]
    [MemberData(nameof(Proven))]
    public void TheCodeAsItIsFiresTheCheckAndTheFixedCodeClearsIt(string checkId, string? key, string slot, string around, string path, string elided)
    {
        var check = IssueCatalog.Find(checkId)!;
        var example = Assert.Single(check.FixExamples, example => example.Case == key);

        // A fix that adds something starts from a page without it.
        Assert.True(Fires(check, slot, around, path, example.Before?.Replace("…", elided, StringComparison.Ordinal) ?? ""), "the code as it is doesn't fire the check");
        Assert.False(Fires(check, slot, around, path, example.After.Replace("…", elided, StringComparison.Ordinal)), "the fixed code still fires the check");
    }

    /// <summary>The prose's other fix for an inline SVG: a title as the SVG's first child names the link too.</summary>
    [Fact]
    public void AnSvgsTitleNamesItsLinkToo()
    {
        var check = IssueCatalog.EMPTY_ANCHOR_TEXT;
        Assert.True(Fires(check, "body", "{code}", "/about/", "<a href=\"/\"><svg viewBox=\"0 0 120 32\"><path d=\"M0 0h120v32H0z\"/></svg></a>"));
        Assert.False(Fires(check, "body", "{code}", "/about/",
            "<a href=\"/\"><svg viewBox=\"0 0 120 32\"><title>Example Furniture</title><path d=\"M0 0h120v32H0z\"/></svg></a>"));
    }

    /// <summary>
    /// The examples of checks whose rules read more than a page, held to the part of the engine that judges the value:
    /// what a Content-Type makes a file, whether a lastmod is a date.
    /// </summary>
    [Fact]
    public void TheExamplesOfSiteChecksHoldToWhatJudgesTheirValues()
    {
        static string Value(string header) => header[(header.IndexOf(':', StringComparison.Ordinal) + 1)..].Trim();
        static ContentClass ClassOf(string header) => ResourceTypes.ClassOf(ContentTypeHeader.MediaTypeOf(Value(header)));

        var script = IssueCatalog.CONTENT_TYPE_MISMATCH.FixExamples.Single(example => example.Case == "script");
        Assert.NotEqual(ContentClass.Script, ClassOf(script.Before!));
        Assert.Equal(ContentClass.Script, ClassOf(script.After));
        var stylesheet = IssueCatalog.CONTENT_TYPE_MISMATCH.FixExamples.Single(example => example.Case == "stylesheet");
        Assert.NotEqual(ContentClass.Stylesheet, ClassOf(stylesheet.Before!));
        Assert.Equal(ContentClass.Stylesheet, ClassOf(stylesheet.After));

        static string Lastmod(string element) => Regex.Match(element, "<lastmod>(.*)</lastmod>").Groups[1].Value;
        var lastmod = Assert.Single(IssueCatalog.SITEMAP_LASTMOD_INVALID.FixExamples);
        Assert.Equal(SitemapLastmod.UNREADABLE, SitemapLastmod.Parse(Lastmod(lastmod.Before!)));
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(), SitemapLastmod.Parse(Lastmod(lastmod.After)));
    }

    /// <summary>Whether a page with the code in it gets the check, by every rule the analysis runs on a page.</summary>
    private static bool Fires(IssueDefinition check, string slot, string around, string path, string code)
    {
        var site = new RuleHarness(SITE);
        var filled = around.Replace("{code}", code, StringComparison.Ordinal);
        List<ResponseHeader> headers = [];

        // A page with nothing in it but the code: no charset, no title. Only the example's own check is looked for.
        static string Document(string head = "", string body = "", string html = "<html lang=\"en\">") =>
            $"<!DOCTYPE html>{html}<head>{head}</head><body><main>{body}</main></body></html>";

        var html = slot switch
        {
            "head" => Document(head: filled),
            "body" => Document(body: filled),
            "html" => Document(html: filled),
            "jsonld" => Document(head: $"<script type=\"application/ld+json\">{filled}</script>"),
            "headers" => Document(),
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null)
        };
        if (slot == "headers")
        {
            // Header names are stored in lower case, as the fetch records them.
            headers.AddRange(filled.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split(':', 2))
                .Select(parts => new ResponseHeader(parts[0].Trim().ToLowerInvariant(), parts[1].Trim())));
        }

        var url = SITE + path;
        var page = site.Page(url, html, headers: headers);

        // Indexability as the crawl settles it for a page that answered (PageProcessor.IndexabilityOf): noindex first,
        // then a canonical, in the head or a header, to another URL.
        var reason = page.Facts.Robots.NoIndex ? IndexabilityReason.Noindex
            : page.Facts.Canonicals.FirstOrDefault(canonical => canonical.Url is not null && (canonical.InHead || canonical.FromHttpHeader)) is { Url: { } target } && target != page.Url
                ? IndexabilityReason.Canonicalised
                : IndexabilityReason.None;
        page = site.Page(url, html, reason, headers);

        // Every image the page shows was fetched: a 1,600 by 900 pixel file of 120 KB, of the type its name says.
        foreach (var image in page.Facts.Images.Select(image => image.Url).OfType<string>())
        {
            var type = Path.GetExtension(new Uri(image).AbsolutePath) switch
            {
                ".jpg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".svg" => "image/svg+xml",
                var other => throw new ArgumentOutOfRangeException(nameof(code), other, null)
            };
            site.Fetched(image, FetchOutcome.Success, 200, contentType: type, size: 120_000, pixelWidth: 1600, pixelHeight: 900);
        }

        var urls = site.Table(SITE + "/");
        var scope = new CrawlScope(new Uri(SITE + "/"), includeSubdomains: false, stayInStartFolder: false);
        IPageRule[] rules =
        [
            new TitleRules(SETTINGS), new DescriptionRules(SETTINGS), new HeadingRules(), new ContentRules(SETTINGS),
            new ImageRules(SETTINGS, urls, site.Find, scope, startIsPrivate: false, readUrl: site.UrlOf),
            new TextStructureRules(SETTINGS), new SocialRules(urls), new LinkRules(urls), new CanonicalRules(urls, site.UrlOf), new IndexabilityRules(),
            new DirectiveRules(urls, "ShoutingIguana"), new LinkStructureRules(SETTINGS, urls, site.UrlOf, new Uri(SITE + "/")), new HreflangRules(urls, site.UrlOf),
            new StructuredDataRules(), new SecurityRules(urls), new UrlRules(SETTINGS), new PerformanceRules(SETTINGS), new HtmlRules()
        ];
        var analysed = RuleHarness.AsAnalysed(page, urls);
        return rules.SelectMany(rule => rule.Check(analysed)).Any(hit => hit.Issue == check);
    }
}
