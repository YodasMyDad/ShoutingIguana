using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// Step 3b's image checks (batch 6 of docs/v2-new-checks.md): images with no source, wide files without srcset, files in
/// an older format, alt text that describes nothing, images from other websites, and large inline images.
/// </summary>
public sealed class ImageCheckTests
{
    private static readonly AnalysisSettings SETTINGS = new();

    [Fact]
    public void TheParserSaysWhichImagesAreDataUrisTheirSizeAndWhichArePictures()
    {
        var payload = Convert.ToBase64String(new byte[12 * 1024]);
        var parsed = HtmlPageParser.Parse(RuleHarness.Html(body:
            $"<img src=\"data:image/png;base64,{payload}\" alt=\"Inline\"><img src=\"data:image/svg+xml,%3Csvg%3E%3C/svg%3E\" alt=\"Svg\">" +
            "<picture><source srcset=\"/a.webp\" type=\"image/webp\"><img src=\"/a.jpg\" alt=\"Pictured\"></picture>" +
            "<picture><img src=\"/b.jpg\" alt=\"Alone\"></picture><img alt=\"Nothing\">"), new Uri("https://example.com/"), [], "ShoutingIguana");

        var images = parsed.Facts.Images;
        Assert.Equal((null, true, 12L * 1024), (images[0].Url, images[0].IsDataUri, images[0].DataUriBytes));
        Assert.Equal((true, 19L), (images[1].IsDataUri, images[1].DataUriBytes));
        Assert.Equal((true, false), (images[2].InPicture, images[3].InPicture));
        Assert.Equal((null, false, null), (images[4].Url, images[4].IsDataUri, images[4].DataUriBytes));
        Assert.Equal(PageFacts.CURRENT_COLLECTION, parsed.Facts.CollectedVersion);
    }

    [Fact]
    public void AnImageWithNoSourceAndALargeInlineImageAreCounted()
    {
        var site = new RuleHarness();
        var payload = Convert.ToBase64String(new byte[12 * 1024]);
        var small = Convert.ToBase64String(new byte[2 * 1024]);
        var page = site.Page($"{site.Site}/", RuleHarness.Html(body:
            "<img alt=\"No file\" width=\"4\" height=\"3\"><img alt=\"Still none\"><img srcset=\"\" alt=\"Empty srcset\"><img width=\"1\" height=\"1\" alt=\"\">" +
            $"<img src=\"data:image/png;base64,{payload}\" alt=\"Big\"><img src=\"data:image/png;base64,{payload}{payload}\" alt=\"Bigger\"><img src=\"data:image/png;base64,{small}\" alt=\"Icon\">" +
            "<img data-src=\"/lazy.jpg\" alt=\"Lazy\"><img srcset=\"/a.jpg 1x\" alt=\"Srcset\">"));

        var hits = new ImageRules(SETTINGS, site.Table(), site.Find).Check(page).ToList();
        // An srcset attribute, even an empty one, is something a script may fill: only an image with neither is counted.
        Assert.Equal("2 images, the first: alt \"No file\"", Detail(hits, IssueCatalog.IMAGE_MISSING_SRC));
        Assert.Equal("2 inline images, the largest 24 KB, alt \"Bigger\"", Detail(hits, IssueCatalog.IMAGE_INLINE_LARGE));
    }

    /// <summary>
    /// The real-site audit's case: "1 image" said nothing of which. An image with no file to name is told by its alt text,
    /// else its class, else where on the page it is; one the crawler parsed before it noted the class and place, by its
    /// alt text or not at all.
    /// </summary>
    [Fact]
    public void AnImageWithoutAFileIsToldByItsAltItsClassOrItsPlace()
    {
        var payload = Convert.ToBase64String(new byte[12 * 1024]);
        var large = Convert.ToBase64String(new byte[48 * 1024]);
        (string? Missing, string? Inline) Details(string body)
        {
            var site = new RuleHarness();
            var hits = new ImageRules(SETTINGS, site.Table(), site.Find).Check(site.Page($"{site.Site}/", Document(body))).ToList();
            return (Detail(hits, IssueCatalog.IMAGE_MISSING_SRC), Detail(hits, IssueCatalog.IMAGE_INLINE_LARGE));
        }

        Assert.Equal("1 image: alt \"Product photo\"", Details("<img alt=\"Product photo\">").Missing);
        Assert.Equal("1 image: class \"hero lazy\"", Details("<img alt=\"\" class=\" hero  lazy \">").Missing);
        Assert.Equal("1 image, in the header", Details("<header><img></header>").Missing);
        Assert.Equal("1 image, in the main content", Details("<main><img alt=\" \"></main>").Missing);
        Assert.Equal("3 images, the first: class \"thumb\"", Details("<img class=\"thumb\"><img alt=\"Two\"><img alt=\"Three\">").Missing);
        Assert.Equal("2 images, the first in the footer", Details("<footer><img><img></footer>").Missing);

        Assert.Equal("1 inline image: 12 KB, alt \"Logo\"", Details($"<img src=\"data:image/png;base64,{payload}\" alt=\"Logo\">").Inline);
        Assert.Equal("1 inline image: 12 KB, in the navigation", Details($"<nav><img src=\"data:image/png;base64,{payload}\"></nav>").Inline);
        Assert.Equal("2 inline images, the largest 48 KB, class \"banner\"",
            Details($"<img src=\"data:image/png;base64,{payload}\" alt=\"Small\"><img src=\"data:image/png;base64,{large}\" class=\"banner\">").Inline);

        // A page parsed before the crawler noted an image's class and place.
        var earlier = new RuleHarness();
        var page = earlier.Page($"{earlier.Site}/", Document("<header><img class=\"hero\"><img alt=\"Two\"></header>"));
        page = page with { Facts = page.Facts with { Images = [.. page.Facts.Images.Select(image => image with { Class = null, Position = null })] } };
        Assert.Equal("2 images", Detail(new ImageRules(SETTINGS, earlier.Table(), earlier.Find).Check(page).ToList(), IssueCatalog.IMAGE_MISSING_SRC));
    }

    [Fact]
    public void TheParserNotesTheClassAndPlaceOfAnImageWithoutAFileOnly()
    {
        var parsed = HtmlPageParser.Parse(Document(
            "<header><img class=\" logo   lazy \"></header><footer><img data-foo=\"1\"></footer><img src=\"/a.jpg\" class=\"photo\">" +
            $"<aside><img src=\"data:image/gif;base64,R0lGOD\" class=\"{new string('c', 150)}\"></aside>"), new Uri("https://example.com/"), [], "ShoutingIguana");

        var images = parsed.Facts.Images;
        Assert.Equal(("logo lazy", (LinkPosition?)LinkPosition.Header), (images[0].Class, images[0].Position));
        Assert.Equal((null, (LinkPosition?)LinkPosition.Footer), (images[1].Class, images[1].Position));
        Assert.Equal((null, (LinkPosition?)null), (images[2].Class, images[2].Position));
        Assert.Equal((100, (LinkPosition?)LinkPosition.Sidebar), (images[3].Class!.Length, images[3].Position));
        Assert.Equal(7, PageFacts.CURRENT_COLLECTION); // 6 noted these; 7 is the links part's collection.
    }

    [Fact]
    public void AWideFileWithoutSrcsetAndALargeOldFormatAreTheirFilesFaults()
    {
        var site = new RuleHarness();
        var wide = site.Fetched($"{site.Site}/wide.jpg", FetchOutcome.Success, 200, size: 30 * 1024, contentType: "image/jpeg", pixelWidth: 2_400);
        var picture = site.Fetched($"{site.Site}/picture.jpg", FetchOutcome.Success, 200, size: 30 * 1024, contentType: "image/jpeg", pixelWidth: 2_000);
        var narrow = site.Fetched($"{site.Site}/narrow.png", FetchOutcome.Success, 200, size: 120 * 1024, contentType: "image/png", pixelWidth: 640);
        var modern = site.Fetched($"{site.Site}/modern.webp", FetchOutcome.Success, 200, size: 120 * 1024, contentType: "image/webp", pixelWidth: 1_000);
        var page = site.Page($"{site.Site}/", RuleHarness.Html(body:
            "<img src=\"/wide.jpg\" alt=\"Wide\"><picture><source srcset=\"/picture.webp\"><img src=\"/picture.jpg\" alt=\"Pictured\"></picture>" +
            "<img src=\"/narrow.png\" alt=\"Narrow\"><img src=\"/modern.webp\" alt=\"Modern\" srcset=\"/modern.webp 1000w\">"));

        var hits = new ImageRules(SETTINGS, site.Table(), site.Find).Check(page).ToList();
        var srcset = Assert.Single(hits, hit => hit.Issue == IssueCatalog.IMAGE_NO_SRCSET);
        Assert.Equal("1 image, 2,400 px wide", srcset.Detail);
        Assert.Equal([wide], srcset.Targets!);
        var legacy = Assert.Single(hits, hit => hit.Issue == IssueCatalog.IMAGE_LEGACY_FORMAT);
        Assert.Equal("1 image: 120 KB", legacy.Detail);
        Assert.Equal([narrow], legacy.Targets!);
        Assert.DoesNotContain(picture, srcset.Targets!);
        Assert.DoesNotContain(modern, legacy.Targets!);
    }

    /// <summary>
    /// The real-site audit's case: a Shopify theme's lazysizes image keeps its other sizes in data-srcset, which its script
    /// copies to srcset, so phones get a smaller file: not flagged. WordPress's lazy loaders write data-lazy-srcset, and a
    /// picture's sources can be lazy too.
    /// </summary>
    [Fact]
    public void AWideFileWithALazyLoadedSrcsetIsntFlagged()
    {
        var site = new RuleHarness();
        var wide = site.Fetched($"{site.Site}/wide.png", FetchOutcome.Success, 200, size: 30 * 1024, contentType: "image/png", pixelWidth: 1_200);
        site.Fetched($"{site.Site}/lazy_1200x.png", FetchOutcome.Success, 200, size: 30 * 1024, contentType: "image/png", pixelWidth: 1_200);
        site.Fetched($"{site.Site}/wordpress.png", FetchOutcome.Success, 200, size: 30 * 1024, contentType: "image/png", pixelWidth: 1_200);
        site.Fetched($"{site.Site}/pictured.png", FetchOutcome.Success, 200, size: 30 * 1024, contentType: "image/png", pixelWidth: 1_200);
        var page = site.Page($"{site.Site}/", RuleHarness.Html(body:
            "<img class=\"lazyload\" src=\"/lazy_1200x.png\" data-srcset=\"/lazy_400x.png 360w, /lazy_1024x.png 1024w\" alt=\"Lazy\">" +
            "<img src=\"/wordpress.png\" data-lazy-srcset=\"/wordpress-480.png 480w, /wordpress.png 1200w\" alt=\"WordPress\">" +
            "<picture><source data-srcset=\"/pictured.webp\" type=\"image/webp\"><img src=\"/pictured.png\" alt=\"Pictured\"></picture>" +
            "<img src=\"/wide.png\" alt=\"Wide\">"));

        var srcset = Assert.Single(new ImageRules(SETTINGS, site.Table(), site.Find).Check(page), hit => hit.Issue == IssueCatalog.IMAGE_NO_SRCSET);
        Assert.Equal([wide], srcset.Targets!);
        Assert.Equal([true, true, true, false], page.Facts.Images.Select(image => image.HasSrcset || image.InPicture));
    }

    [Theory]
    [InlineData("IMG_0423.jpg", true)]
    [InlineData("photo.JPEG", true)]
    [InlineData("DSC01234", true)]
    [InlineData("PXL_20230101_120000123", true)]
    [InlineData("IMG-20230101-WA0001", true)]
    [InlineData("image-3", true)]
    [InlineData("photo_12", true)]
    [InlineData("x", true)]
    [InlineData("--", true)]
    [InlineData("hero-banner", true)]
    [InlineData("Logo", false)]
    [InlineData("Map", false)]
    [InlineData("猫", false)]
    [InlineData("A garden sofa in the sun", false)]
    [InlineData("Photo of 12 chairs", false)]
    [InlineData("  ", false)]
    public void AltTextThatDescribesNothingIsFlagged(string alt, bool unhelpful)
    {
        Assert.Equal(unhelpful, ImageRules.IsUnhelpfulAlt(alt, "https://example.com/img/hero-banner.png"));
    }

    /// <summary>
    /// The real-site audit's case: gardenfurnituresets.co.uk's brand logos, avon.png with the alt "Avon". The file's own
    /// name is a name worth giving it there; a slug, or the name with its extension, isn't.
    /// </summary>
    [Fact]
    public void ABrandNameThatNamesItsLogoFileIsntUnhelpful()
    {
        Assert.False(ImageRules.IsUnhelpfulAlt("Avon", "https://www.example.co.uk/media/4235/avon.png?quality=99"));
        Assert.True(ImageRules.IsUnhelpfulAlt("avon.png", "https://www.example.co.uk/media/4235/avon.png?quality=99"));
        Assert.True(ImageRules.IsUnhelpfulAlt("evolution-of-web-design", "https://example.com/media/27846/evolution-of-web-design.jpg"));
        Assert.True(ImageRules.IsUnhelpfulAlt("garden_sofa_2", "https://example.com/garden_sofa_2.webp"));
    }

    [Theory]
    [InlineData("www.example.co.uk", "example.co.uk")]
    [InlineData("static.example.com", "example.com")]
    [InlineData("example.com", "example.com")]
    [InlineData("127.0.0.1", "127.0.0.1")]
    public void ARegistrableDomainTakesTheSecondLevelSuffixIntoAccount(string host, string domain)
    {
        Assert.Equal(domain, ImageRules.RegistrableDomain(host));
    }

    [Fact]
    public void AnImageFromAnotherWebsiteIsHotlinkedButNotFromTheSitesOwnDomainOrACdn()
    {
        var site = new RuleHarness();
        var other = site.Fetched("https://photos.example.org/a.jpg", FetchOutcome.Success, 200, contentType: "image/jpeg");
        var another = site.Fetched("https://cdn.elsewhere.net/b.jpg", FetchOutcome.Success, 200, contentType: "image/jpeg");
        site.Fetched("https://static.example.com/c.jpg", FetchOutcome.Success, 200, contentType: "image/jpeg");
        site.Fetched("https://cdn.shopify.com/d.jpg", FetchOutcome.Success, 200, contentType: "image/jpeg");
        site.Fetched("https://d111.cloudfront.net/e.jpg", FetchOutcome.Success, 200, contentType: "image/jpeg");
        var page = site.Page($"{site.Site}/", RuleHarness.Html(body:
            "<img src=\"https://photos.example.org/a.jpg\" alt=\"A\"><img src=\"https://cdn.elsewhere.net/b.jpg\" alt=\"B\"><img src=\"https://static.example.com/c.jpg\" alt=\"C\">" +
            "<img src=\"https://cdn.shopify.com/d.jpg\" alt=\"D\"><img src=\"https://d111.cloudfront.net/e.jpg\" alt=\"E\"><img src=\"/f.jpg\" alt=\"F\">"));
        var scope = new CrawlScope(new Uri($"{site.Site}/"), includeSubdomains: false, stayInStartFolder: false);

        var hit = Assert.Single(new ImageRules(SETTINGS, site.Table(), site.Find, scope, readUrl: site.UrlOf).Check(page), hit => hit.Issue == IssueCatalog.IMAGE_HOTLINKED);
        Assert.Equal("2 images from 2 sites", hit.Detail);
        Assert.Equal([other, another], hit.Targets!);
    }

    [Fact]
    public void OnACrawlStartedAtAPrivateAddressOtherPrivateHostsArentOtherWebsites()
    {
        var site = new RuleHarness("http://127.0.0.1:5000");
        site.Fetched("http://10.0.0.5:6000/ext/img/1.jpg", FetchOutcome.Success, 200, contentType: "image/jpeg");
        var page = site.Page($"{site.Site}/", RuleHarness.Html(body: "<img src=\"http://10.0.0.5:6000/ext/img/1.jpg\" alt=\"Partner\">"));
        var scope = new CrawlScope(new Uri($"{site.Site}/"), includeSubdomains: false, stayInStartFolder: false);

        Assert.DoesNotContain(new ImageRules(SETTINGS, site.Table(), site.Find, scope, startIsPrivate: true, readUrl: site.UrlOf).Check(page),
            hit => hit.Issue == IssueCatalog.IMAGE_HOTLINKED);
        Assert.Contains(new ImageRules(SETTINGS, site.Table(), site.Find, scope, startIsPrivate: false, readUrl: site.UrlOf).Check(page),
            hit => hit.Issue == IssueCatalog.IMAGE_HOTLINKED);
    }

    /// <summary>A page whose body is what the test passes in, not in a &lt;main&gt;: a header or footer there is the page's own.</summary>
    private static string Document(string body) =>
        $"<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Images</title></head><body>{body}</body></html>";

    private static string? Detail(List<IssueHit> hits, IssueDefinition check) => hits.Where(hit => hit.Issue == check).Select(hit => hit.Detail).SingleOrDefault();
}
