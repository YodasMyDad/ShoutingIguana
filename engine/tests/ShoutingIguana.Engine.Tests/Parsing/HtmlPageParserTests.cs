using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Tests.Parsing;

public sealed class HtmlPageParserTests
{
    private const string TOKEN = "ShoutingIguana";
    private static readonly Uri PAGE = new("https://example.com/dir/page");

    [Fact]
    public void ReadsHeadSignals()
    {
        var facts = Parse("""
            <!DOCTYPE html><html lang="en-GB"><head>
            <meta charset="utf-8"><title>  Red   Shoes </title>
            <meta name="description" content="Shoes that are red.">
            <meta property="og:title" content="Red shoes">
            <link rel="canonical" href="/dir/page">
            <link rel="alternate" hreflang="de" href="https://example.com/de/page">
            <script type="application/ld+json">{"@type":"Product"}</script>
            </head><body><h1>Red shoes</h1><h2>Sizes</h2></body></html>
            """).Facts;

        Assert.Equal(["Red Shoes"], facts.Titles);
        Assert.Equal(["Shoes that are red."], facts.MetaDescriptions);
        Assert.Contains(facts.MetaTags, tag => tag is { Name: "og:title", Content: "Red shoes" });
        Assert.Equal("en-GB", facts.Language);
        Assert.Equal("utf-8", facts.DeclaredCharset);
        Assert.Equal("https://example.com/dir/page", Assert.Single(facts.Canonicals).Url);
        Assert.True(facts.Canonicals[0].InHead);
        Assert.Equal("de", Assert.Single(facts.Hreflangs).Language);
        Assert.Single(facts.JsonLd);
        Assert.Equal([new HeadingFact(1, "Red shoes"), new HeadingFact(2, "Sizes")], facts.Headings);
    }

    [Fact]
    public void CanonicalAfterAStrayHeadElementLandsInTheBody()
    {
        // An <img> in the head closes it, so the canonical that follows is in the body, where Google ignores it.
        var parsed = Parse("""<html><head><title>T</title><img src="/pixel.gif"><link rel="canonical" href="/c"></head><body></body></html>""");

        var canonical = Assert.Single(parsed.Facts.Canonicals);
        Assert.False(canonical.InHead);
        Assert.Contains(parsed.Links, link => link.Kind == LinkKind.Canonical && link.Flags.HasFlag(LinkFlags.OutsideHead));
    }

    [Fact]
    public void MergesRobotsMetaAndHeaders()
    {
        var facts = Parse(
            """<html><head><meta name="robots" content="noindex, follow"></head><body></body></html>""",
            [new ResponseHeader("x-robots-tag", "googlebot: nofollow"), new ResponseHeader("x-robots-tag", "bingbot: noarchive")]).Facts;

        Assert.True(facts.Robots.NoIndex);
        Assert.True(facts.Robots.NoFollow);
        Assert.DoesNotContain("noarchive", facts.Robots.Directives);
    }

    [Fact]
    public void AMetaTagCanBeBothADescriptionAndAnOpenGraphTag()
    {
        var facts = Parse("""
            <html><head><meta name="description" property="og:description" content="Shared text">
            <meta property="description" content="Not a meta description"><meta name="twitter:title" property="og:title" content="Title">
            </head><body></body></html>
            """).Facts;

        Assert.Equal(["Shared text"], facts.MetaDescriptions);
        Assert.Equal([new MetaTagFact("description", "Shared text"), new MetaTagFact("og:description", "Shared text"),
            new MetaTagFact("description", "Not a meta description"), new MetaTagFact("twitter:title", "Title"), new MetaTagFact("og:title", "Title")],
            facts.MetaTags);
    }

    [Theory]
    [InlineData("noindex nofollow", true, true)]
    [InlineData("NOINDEX  NOFOLLOW max-snippet:0", true, true)]
    [InlineData("noindex, follow", true, false)]
    [InlineData("index follow", false, false)]
    [InlineData("noindexnofollow", false, false)]
    public void RobotsDirectivesSeparatedBySpacesCount(string content, bool noIndex, bool noFollow)
    {
        var robots = Parse($"<html><head><meta name=\"robots\" content=\"{content}\"></head><body></body></html>").Facts.Robots;

        Assert.Equal((noIndex, noFollow), (robots.NoIndex, robots.NoFollow));
    }

    [Theory]
    [InlineData("bingbot: noindex, googlebot: nofollow", false, true)]
    [InlineData("googlebot: noindex, nofollow", true, true)]
    [InlineData("noindex, bingbot: nofollow", true, false)]
    [InlineData("otherbot: noindex nofollow", false, false)]
    [InlineData("ShoutingIguana: noindex", true, false)]
    public void HeaderValuesCanAddressSeveralCrawlers(string value, bool noIndex, bool noFollow)
    {
        var robots = Parse("<html></html>", [new ResponseHeader("x-robots-tag", value)]).Facts.Robots;

        Assert.Equal((noIndex, noFollow), (robots.NoIndex, robots.NoFollow));
    }

    [Fact]
    public void ValuedHeaderDirectivesKeepTheirValues()
    {
        var robots = Parse("<html></html>", [new ResponseHeader("x-robots-tag", "unavailable_after: 25 Jun 2030 15:00:00 PST, max-snippet: 0")]).Facts.Robots;

        Assert.Equal(["unavailable_after: 25 jun 2030 15:00:00 pst", "max-snippet: 0"], robots.Directives);
    }

    [Fact]
    public void ClassifiesLinkPositions()
    {
        var links = Parse("""
            <html><body>
            <nav><a href="/n">N</a></nav>
            <header><a href="/h">H</a></header>
            <aside><a href="/s">S</a></aside>
            <main><a href="/m">M</a><article><header><a href="/ah">AH</a></header></article></main>
            <div class="menu"><a href="/d">D</a></div>
            <div class="page-header"><a href="/ph">PH</a></div>
            <div role="contentinfo"><a href="/r">R</a></div>
            <footer><a href="/f">F</a></footer>
            </body></html>
            """).Links.ToDictionary(link => link.Target.AbsolutePath, link => link.Position);

        Assert.Equal(LinkPosition.Navigation, links["/n"]);
        Assert.Equal(LinkPosition.Header, links["/h"]);
        Assert.Equal(LinkPosition.Sidebar, links["/s"]);
        Assert.Equal(LinkPosition.Content, links["/m"]);
        Assert.Equal(LinkPosition.Content, links["/ah"]);
        Assert.Equal(LinkPosition.Navigation, links["/d"]);
        Assert.Equal(LinkPosition.Content, links["/ph"]);
        Assert.Equal(LinkPosition.Footer, links["/r"]);
        Assert.Equal(LinkPosition.Footer, links["/f"]);
    }

    [Fact]
    public void ReadsAnchorDetails()
    {
        var links = Parse("""
            <html><head><base href="https://cdn.example.com/sub/"></head><body>
            <a href="relative">Relative</a>
            <a href="/x" rel="nofollow sponsored">Paid</a>
            <a href="/logo"><img src="/logo.png" alt="Home page"></a>
            <a href="#reviews">Reviews</a>
            <a href="mailto:hi@example.com">Mail</a>
            </body></html>
            """).Links.Where(link => link.Kind == LinkKind.Hyperlink).ToList();

        Assert.Equal(4, links.Count);
        Assert.Equal("https://cdn.example.com/sub/relative", links[0].Target.AbsoluteUri);
        Assert.Equal(LinkFlags.NoFollow | LinkFlags.Sponsored, links[1].Flags);
        Assert.Equal("Home page", links[2].Text);
        Assert.True(links[2].Flags.HasFlag(LinkFlags.ImageLink));
        Assert.True(links[3].Flags.HasFlag(LinkFlags.SamePage));
        Assert.Equal("#reviews", links[3].Target.Fragment);
    }

    [Fact]
    public void ReadsImagesIncludingLazyLoadedOnes()
    {
        var facts = Parse("""
            <html><body>
            <img src="data:image/gif;base64,R0lGOD" data-src="/real.jpg" alt="" width="400" loading="lazy">
            <img src="/no-alt.png">
            </body></html>
            """).Facts;

        Assert.Equal(2, facts.Images.Count);
        Assert.Equal("https://example.com/real.jpg", facts.Images[0].Url);
        Assert.True(facts.Images[0].HasAlt);
        Assert.Equal("400", facts.Images[0].Width);
        Assert.Equal("lazy", facts.Images[0].Loading);
        Assert.False(facts.Images[1].HasAlt);
    }

    [Fact]
    public void ReadsMetaRefreshAsALink()
    {
        var parsed = Parse("""<html><head><meta http-equiv="refresh" content="0; url='/moved'"></head><body></body></html>""");

        Assert.Equal(new MetaRefreshFact(0, "/moved"), parsed.Facts.MetaRefresh);
        Assert.Contains(parsed.Links, link => link.Kind == LinkKind.MetaRefresh && link.Target.AbsolutePath == "/moved");
    }

    [Fact]
    public void CountsOnlyMainContentWords()
    {
        var facts = Parse("""
            <html><head><title>Ignored title words</title></head><body>
            <nav>alpha beta</nav><main><p>one two</p><p>three</p></main><footer>gamma</footer>
            <script>var notText = 1;</script><svg><title>Icon</title></svg>
            </body></html>
            """).Facts;

        Assert.Equal(3, facts.WordCount);
        Assert.Equal(["Ignored title words"], facts.Titles);
    }

    [Fact]
    public void HeaderOnlyResponsesStillCarryCanonicalsAndRobots()
    {
        var parsed = HtmlPageParser.ParseHeaders(
            new Uri("https://example.com/report.pdf"),
            [new ResponseHeader("link", "<https://example.com/report>; rel=\"canonical\""), new ResponseHeader("x-robots-tag", "noindex")],
            TOKEN);

        Assert.True(Assert.Single(parsed.Facts.Canonicals).FromHttpHeader);
        Assert.True(parsed.Facts.Robots.NoIndex);
        Assert.Equal(LinkKind.Canonical, Assert.Single(parsed.Links).Kind);
    }

    [Fact]
    public void NoscriptPixelsInTheHeadDoNotPushLaterTagsIntoTheBody()
    {
        // Facebook's pixel snippet, placed above the canonical as many sites do.
        var parsed = Parse("""
            <!DOCTYPE html><html><head><meta charset="utf-8">
            <noscript><img height="1" width="1" style="display:none" src="https://www.facebook.com/tr?id=1&ev=PageView"></noscript>
            <link rel="canonical" href="https://example.com/dir/page">
            <link rel="alternate" hreflang="de" href="https://example.com/de/page">
            </head><body><p>Text</p></body></html>
            """);

        Assert.True(Assert.Single(parsed.Facts.Canonicals).InHead);
        Assert.True(Assert.Single(parsed.Facts.Hreflangs).InHead);
        Assert.Empty(parsed.Facts.Images);
    }

    [Fact]
    public void LogoHeadingsUseTheImageAltText()
    {
        var facts = Parse("<html><body><h1><a href=\"/\"><img src=\"/logo.png\" alt=\"Acme Tools\"></a></h1></body></html>").Facts;

        Assert.Equal("Acme Tools", Assert.Single(facts.Headings).Text);
    }

    private static ParsedPage Parse(string html, IReadOnlyList<ResponseHeader>? headers = null) =>
        HtmlPageParser.Parse(html, PAGE, headers ?? [], TOKEN);
}
