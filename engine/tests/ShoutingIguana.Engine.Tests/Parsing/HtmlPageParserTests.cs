using System.Text;
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

    /// <summary>
    /// The parser reads text, which has no byte-order mark left, so the fetch hands over the one it saw and the facts keep
    /// it beside the declared charset: the page is parsed once, and the analysis works from the facts.
    /// </summary>
    [Fact]
    public void AByteOrderMarkTheFetchSawIsKeptWithTheFacts()
    {
        const string html = "<!DOCTYPE html><html lang=\"en\"><head><title>T</title></head><body></body></html>";

        var marked = HtmlPageParser.Parse(html, PAGE, [], TOKEN, byteOrderMark: "utf-8").Facts;
        var plain = Parse(html).Facts;

        Assert.Equal(("utf-8", (string?)null), (marked.ByteOrderMark, marked.DeclaredCharset));
        Assert.Equal(((string?)null, (string?)null), (plain.ByteOrderMark, plain.DeclaredCharset));
    }

    /// <summary>
    /// Browsers look for a meta tag declaring the encoding in the first 1024 bytes only, so the facts say where in the bytes
    /// the declaring tag ends, counted in the encoding it names: "é" is two bytes in UTF-8 and one in windows-1252. The
    /// first declaration counts, a later one or one in a script's text not at all.
    /// </summary>
    [Fact]
    public void TheFactsSayWhereInTheBytesTheCharsetDeclarationEnds()
    {
        const string head = "<!DOCTYPE html><html><head><title>é</title><script>var tag = '<meta charset=\"x\">';</script>";
        const string tag = "<meta charset=\"utf-8\">";

        var early = Parse($"{head}{tag}<meta charset=\"latin1\"></head><body></body></html>").Facts;
        var latin = Parse($"{head}<meta http-equiv=\"Content-Type\" content=\"text/html; charset=windows-1252\"></head></html>").Facts;
        var late = Parse($"{head}<style>{new string('x', 2_000)}</style>{tag}</head><body></body></html>").Facts;

        Assert.Equal(("utf-8", Encoding.UTF8.GetByteCount(head + tag)), (early.DeclaredCharset, early.DeclaredCharsetEnd));
        Assert.Equal(("windows-1252", head.Length + 74), (latin.DeclaredCharset, latin.DeclaredCharsetEnd));
        Assert.Equal(Encoding.UTF8.GetByteCount(head) + 2_015 + tag.Length, late.DeclaredCharsetEnd);
        Assert.Null(Parse("<html><head><title>T</title></head></html>").Facts.DeclaredCharsetEnd);
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
        // Each says which attribute named it: property= alone (as Open Graph writes it) is noted, as X reads only name=.
        Assert.Equal([new MetaTagFact("description", "Shared text"), new MetaTagFact("og:description", "Shared text") { IsProperty = true },
            new MetaTagFact("description", "Not a meta description") { IsProperty = true }, new MetaTagFact("twitter:title", "Title"),
            new MetaTagFact("og:title", "Title") { IsProperty = true }],
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

        // Under a base on another URL, #reviews is a link to the base URL, as browsers resolve it (HTML spec).
        Assert.Equal("https://cdn.example.com/sub/#reviews", links[3].Target.AbsoluteUri);
        Assert.False(links[3].Flags.HasFlag(LinkFlags.SamePage));
    }

    /// <summary>Icon and image links are named the way screen readers name them; only a link with no name at all has no text.</summary>
    [Theory]
    [InlineData("<a href=\"/cart\"><svg aria-label=\"Basket\"><path d=\"M0 0\"/></svg></a>", "Basket")]
    [InlineData("<a href=\"/cart\"><span class=\"icon\" aria-label=\"Basket\"></span></a>", "Basket")]
    [InlineData("<a href=\"/\"><img src=\"/spacer.gif\" alt=\"\"><img src=\"/logo.png\" alt=\"Home page\"></a>", "Home page")]
    [InlineData("<a href=\"/cart\" aria-label=\"\" title=\"Basket\"><svg><path d=\"M0 0\"/></svg></a>", "Basket")]
    [InlineData("<a href=\"/cart\" aria-labelledby=\"open basket\"><svg><path d=\"M0 0\"/></svg></a><span id=\"open\">Open</span><b id=\"basket\">basket</b>", "Open basket")]
    [InlineData("<a href=\"/\"><svg><title>Home</title><path d=\"M0 0\"/></svg></a>", "Home")]
    [InlineData("<a href=\"/\"><svg><path d=\"M0 0\"/></svg></a>", null)]
    [InlineData("<a href=\"/\" aria-labelledby=\"nowhere\"><img src=\"/logo.png\" alt=\"\"></a>", null)]
    public void LinksWithoutTextAreNamedAsScreenReadersNameThem(string body, string? name)
    {
        var link = Parse($"<html><body>{body}</body></html>").Links.First(found => found.Kind == LinkKind.Hyperlink);

        Assert.Equal(name, link.Text);
    }

    [Fact]
    public void SamePageLinksAreTheOnesThatResolveToThePage()
    {
        var links = Parse("""
            <html><body><a href="#reviews">A</a><a href="page#specs">B</a><a href="/dir/page?tab=2#x">C</a><a href="/dir/other#x">D</a></body></html>
            """).Links;

        Assert.Equal([true, true, false, false], links.Select(link => link.Flags.HasFlag(LinkFlags.SamePage)));
    }

    [Fact]
    public void ImagesFramesAndSvgShapesAreFragmentTargets()
    {
        var facts = Parse("""
            <html><head><title id="doc-title">T</title></head><body>
            <img id="photo" src="/a.jpg" alt="A"><iframe id="map" src="/map"></iframe>
            <svg id="chart"><g id="bars"><text id="label">L</text></g></svg><script id="data">1</script>
            </body></html>
            """).Facts;

        Assert.Equal(["doc-title", "photo", "map", "chart", "bars", "label", "data"], facts.AnchorIds);
    }

    [Fact]
    public void ImageMapAreasUseTheirAltTextAsLinkText()
    {
        var links = Parse("""
            <html><body><img src="/plan.png" usemap="#plan" alt="Floor plan">
            <map name="plan"><area href="/kitchen" alt="Kitchen" shape="rect" coords="0,0,9,9"><area href="/hall" shape="rect" coords="9,9,19,19"></map>
            </body></html>
            """).Links.Where(link => link.Kind == LinkKind.Hyperlink).ToList();

        Assert.Equal(["Kitchen", null], links.Select(link => link.Text));
    }

    [Fact]
    public void HeadingAndLinkTextKeepWordsApartAtLineBreaksAndBlocks()
    {
        var parsed = Parse("""
            <html><body><h1>Welcome to<br>Our Store</h1><h2>One<div>Two</div>Three</h2>
            <a href="/a">Shop<br/>now</a><a href="/b">Gift<span>card</span></a><a href="/c"><p>Read</p><p>more</p></a>
            </body></html>
            """);

        Assert.Equal(["Welcome to Our Store", "One Two Three"], parsed.Facts.Headings.Select(heading => heading.Text));
        Assert.Equal(["Shop now", "Giftcard", "Read more"], parsed.Links.Select(link => link.Text));
    }

    [Fact]
    public void ImagesCanComeFromSrcsetOrAnyCommonLazyAttribute()
    {
        var facts = Parse("""
            <html><body>
            <img srcset="/one.jpg 1x, /two.jpg 2x">
            <img src="data:image/gif;base64,R0lGOD" data-original="/original.jpg">
            <img data-lazy="/lazy.jpg"><img data-srcset=" /set.jpg 400w, /set-big.jpg 800w">
            <img srcset="https://cdn.example/w_100,h_100/cut.jpg, https://cdn.example/w_200,h_200/cut.jpg 2x">
            </body></html>
            """).Facts;

        Assert.Equal(["https://example.com/one.jpg", "https://example.com/original.jpg", "https://example.com/lazy.jpg", "https://example.com/set.jpg",
            "https://cdn.example/w_100,h_100/cut.jpg"], facts.Images.Select(image => image.Url));
    }

    [Fact]
    public void ImagesPastTheLinkCapStillHaveTheirUrl()
    {
        var anchors = string.Concat(Enumerable.Range(0, 10_000).Select(index => $"<a href=\"/p/{index}\">{index}</a>"));
        var parsed = Parse($"<html><body>{anchors}<img src=\"/late.jpg\" alt=\"Late\"></body></html>");

        Assert.Equal("https://example.com/late.jpg", Assert.Single(parsed.Facts.Images).Url);
        Assert.DoesNotContain(parsed.Links, link => link.Kind == LinkKind.Image);
    }

    [Fact]
    public void ABasePushedIntoTheBodyStillCounts()
    {
        // The stray <img> ends the head, so the base that follows is in the body; browsers still use it.
        var parsed = Parse("""<html><head><title>T</title><img src="/pixel.gif"><base href="https://example.com/other/"></head><body><a href="x">X</a></body></html>""");

        Assert.Equal("https://example.com/other/", parsed.Facts.BaseHref);
        Assert.Equal("https://example.com/other/x", parsed.Links.Single(link => link.Kind == LinkKind.Hyperlink).Target.AbsoluteUri);
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
