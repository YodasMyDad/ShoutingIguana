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
        Assert.Equal("2 images", Detail(hits, IssueCatalog.IMAGE_MISSING_SRC));
        Assert.Equal("2 inline images, the largest 24 KB", Detail(hits, IssueCatalog.IMAGE_INLINE_LARGE));
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

    private static string? Detail(List<IssueHit> hits, IssueDefinition check) => hits.Where(hit => hit.Issue == check).Select(hit => hit.Detail).SingleOrDefault();
}
