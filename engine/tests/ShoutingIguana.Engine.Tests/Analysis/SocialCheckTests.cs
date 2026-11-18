using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// Step 3b's Open Graph and X card checks (batch 9 of docs/v2-new-checks.md): a page without an X card, a card X can't
/// build, a share image named by part of its address, an og:url that isn't the page, a share tag given twice, share
/// titles and descriptions pages share while their own differ, and share images that are broken, too large or too small.
/// </summary>
public sealed class SocialCheckTests
{
    private const string OPEN_GRAPH =
        "<meta property=\"og:title\" content=\"Sofas\"><meta property=\"og:description\" content=\"All our sofas\">" +
        "<meta property=\"og:image\" content=\"https://example.com/img/share.jpg\">";

    private const string CARD = "<meta name=\"twitter:card\" content=\"summary_large_image\">";

    [Fact]
    public void AMetaTagSaysWhetherItWasWrittenAsAProperty()
    {
        var facts = HtmlPageParser.Parse(RuleHarness.Html(
                "<meta property=\"twitter:card\" content=\"summary\"><meta name=\"twitter:title\" content=\"A\"><meta name=\"description\" property=\"og:description\" content=\"B\">"),
            new Uri("https://example.com/"), [], "ShoutingIguana").Facts;

        Assert.Equal(
            [("twitter:card", true), ("twitter:title", false), ("description", false), ("og:description", true)],
            facts.MetaTags.Select(tag => (tag.Name, tag.IsProperty)));
    }

    [Fact]
    public void AShareImageGivenAsAFullUrlIsALinkTheCrawlerChecks()
    {
        var links = HtmlPageParser.Parse(RuleHarness.Html(
                "<meta property=\"og:image\" content=\"https://example.com/a.jpg\"><meta name=\"twitter:image\" content=\"https://cdn.example.net/b.png\">" +
                "<meta property=\"og:image:secure_url\" content=\"/c.jpg\"><meta property=\"og:image\" content=\"//cdn.example.net/d.jpg\">"),
            new Uri("https://example.com/"), [], "ShoutingIguana").Links.Where(link => link.Kind == LinkKind.SocialImage).ToList();

        Assert.Equal(["https://example.com/a.jpg", "https://cdn.example.net/b.png"], links.Select(link => link.Target.AbsoluteUri));
        Assert.Equal(["og:image", "twitter:image"], links.Select(link => link.Text));
    }

    [Fact]
    public void APageWithoutACardOrWithACardXCantBuildIsFlagged()
    {
        Assert.Contains(IssueCatalog.X_CARD_MISSING, Issues(OPEN_GRAPH));
        Assert.DoesNotContain(IssueCatalog.X_CARD_MISSING, Issues(OPEN_GRAPH + "<meta property=\"twitter:card\" content=\"summary\">"));
        Assert.Empty(Issues(OPEN_GRAPH + CARD));
        Assert.Empty(Issues(OPEN_GRAPH + CARD, IndexabilityReason.Noindex));

        Assert.Equal("twitter: tags written as property=, which X doesn't read",
            Detail(OPEN_GRAPH + "<meta property=\"twitter:card\" content=\"summary\">", IssueCatalog.X_CARD_INCOMPLETE));
        Assert.Equal("No title (twitter:title or og:title); No image (twitter:image or og:image)", Detail(CARD, IssueCatalog.X_CARD_INCOMPLETE));
        Assert.Null(Detail(CARD + "<meta name=\"twitter:title\" content=\"A\"><meta name=\"twitter:image\" content=\"https://example.com/a.jpg\">",
            IssueCatalog.X_CARD_INCOMPLETE));
        Assert.Equal("Card type \"photo\" isn't one X shows", Detail(OPEN_GRAPH + "<meta name=\"twitter:card\" content=\"photo\">", IssueCatalog.X_CARD_INCOMPLETE));
        Assert.Equal("No twitter:player", Detail(OPEN_GRAPH + "<meta name=\"twitter:card\" content=\"player\">", IssueCatalog.X_CARD_INCOMPLETE));
        Assert.Null(Detail(OPEN_GRAPH + "<meta name=\"twitter:card\" content=\"app\">", IssueCatalog.X_CARD_INCOMPLETE));
    }

    [Fact]
    public void RelativeShareImagesOgUrlsElsewhereAndTagsGivenTwiceAreFlagged()
    {
        Assert.Equal("og:image is /img/share.jpg", Detail(CARD + OPEN_GRAPH.Replace("https://example.com/img/share.jpg", "/img/share.jpg"), IssueCatalog.SOCIAL_IMAGE_RELATIVE));
        Assert.Equal("twitter:image is //cdn.example.com/x.jpg",
            Detail(CARD + OPEN_GRAPH + "<meta name=\"twitter:image\" content=\"//cdn.example.com/x.jpg\">", IssueCatalog.SOCIAL_IMAGE_RELATIVE));

        Assert.Equal("og:url is https://example.com/", Detail(CARD + OPEN_GRAPH + "<meta property=\"og:url\" content=\"https://example.com/\">", IssueCatalog.OG_URL_NOT_CANONICAL));
        Assert.Null(Detail(CARD + OPEN_GRAPH + "<meta property=\"og:url\" content=\"https://EXAMPLE.com/page\">", IssueCatalog.OG_URL_NOT_CANONICAL));
        Assert.Null(Detail(CARD + OPEN_GRAPH + "<meta property=\"og:url\" content=\"/page\">", IssueCatalog.OG_URL_NOT_CANONICAL));

        Assert.Equal("2 og:title, 2 twitter:card",
            Detail(CARD + OPEN_GRAPH + "<meta property=\"og:title\" content=\"Chairs\"><meta name=\"twitter:card\" content=\"summary\">", IssueCatalog.SOCIAL_TAGS_MULTIPLE));
        Assert.Null(Detail(CARD + OPEN_GRAPH + "<meta property=\"og:title\" content=\"Sofas\"><meta property=\"og:image\" content=\"https://example.com/2.jpg\">",
            IssueCatalog.SOCIAL_TAGS_MULTIPLE));
    }

    [Fact]
    public void ShareImagesThatDontLoadOrAreTooLargeOrTooSmallAreTheirTargets()
    {
        var site = new RuleHarness();
        var missing = site.Fetched($"{site.Site}/img/missing.jpg", FetchOutcome.ClientError, 404);
        var page = site.Fetched($"{site.Site}/img/page.jpg", FetchOutcome.Success, 200, isHtml: true, isParsed: false);
        var huge = site.Fetched($"{site.Site}/img/huge.png", FetchOutcome.Success, 200, contentType: "image/png", size: 6_500_000, pixelWidth: 1_200, pixelHeight: 630);
        var small = site.Fetched($"{site.Site}/img/small.png", FetchOutcome.Success, 200, contentType: "image/png", size: 2_000, pixelWidth: 100, pixelHeight: 100);
        var wide = site.Fetched($"{site.Site}/img/wide.png", FetchOutcome.Success, 200, contentType: "image/png", size: 2_000, pixelWidth: 280, pixelHeight: 280);
        var fine = site.Fetched($"{site.Site}/img/fine.png", FetchOutcome.Success, 200, contentType: "image/png", size: 90_000, pixelWidth: 1_200, pixelHeight: 630);
        string Images(params (string Tag, string Path)[] images) =>
            string.Concat(images.Select(image => $"<meta {(image.Tag.StartsWith("og:", StringComparison.Ordinal) ? "property" : "name")}=\"{image.Tag}\" content=\"{site.Site}{image.Path}\">"));
        var broken = site.Page($"{site.Site}/a", RuleHarness.Html(CARD + Images(("og:image", "/img/missing.jpg"), ("twitter:image", "/img/page.jpg"))));
        var big = site.Page($"{site.Site}/b", RuleHarness.Html(CARD + Images(("og:image", "/img/huge.png"))));
        var tiny = site.Page($"{site.Site}/c", RuleHarness.Html(CARD + Images(("og:image", "/img/small.png"))));
        var forX = site.Page($"{site.Site}/d", RuleHarness.Html(CARD + Images(("og:image", "/img/fine.png"), ("twitter:image", "/img/wide.png"))));
        var forBoth = site.Page($"{site.Site}/e", RuleHarness.Html(CARD + Images(("og:image", "/img/wide.png"))));
        var urls = site.Table();
        var rules = new SocialRules(urls);

        var hit = Assert.Single(rules.Check(broken), hit => hit.Issue == IssueCatalog.SOCIAL_IMAGE_BROKEN);
        Assert.Equal(("og:image returns 404 Not Found", missing), (hit.Detail, hit.RelatedUrlId));
        Assert.Equal([missing, page], hit.Targets!);
        hit = Assert.Single(rules.Check(big), hit => hit.Issue == IssueCatalog.SOCIAL_IMAGE_TOO_LARGE);
        Assert.Equal(("og:image is 6.2 MB, over 5 MB", huge), (hit.Detail, hit.RelatedUrlId));
        hit = Assert.Single(rules.Check(tiny), hit => hit.Issue == IssueCatalog.SOCIAL_IMAGE_TOO_SMALL);
        Assert.Equal(("og:image is 100 × 100 px, under 200 × 200", small), (hit.Detail, hit.RelatedUrlId));

        // X uses its own image where there is one: 280 × 280 is under its large card's 300 × 157; on Facebook it's fine.
        hit = Assert.Single(rules.Check(forX), hit => hit.Issue == IssueCatalog.SOCIAL_IMAGE_TOO_SMALL);
        Assert.Equal(("twitter:image is 280 × 280 px, under 300 × 157 (X's large card)", wide), (hit.Detail, hit.RelatedUrlId));
        Assert.Equal("og:image is 280 × 280 px, under 300 × 157 (X's large card)",
            Assert.Single(rules.Check(forBoth), hit => hit.Issue == IssueCatalog.SOCIAL_IMAGE_TOO_SMALL).Detail);
        Assert.DoesNotContain(rules.Check(forX), hit => hit.Targets?.Contains(fine) == true);
    }

    [Fact]
    public void PagesSharingAShareTitleOrDescriptionWhileTheirOwnDifferAreDuplicates()
    {
        var site = new RuleHarness();
        AnalyzedPage Page(string path, string title, string shareTitle, string description, string shareDescription) =>
            site.Page($"{site.Site}{path}", RuleHarness.Html(
                $"<title>{title}</title><meta name=\"description\" content=\"{description}\">" +
                $"<meta property=\"og:title\" content=\"{shareTitle}\"><meta name=\"twitter:description\" content=\"{shareDescription}\">"));
        var a = Page("/a", "Sofas", "Our shop", "Sofas for every room", "Shop with us");
        var b = Page("/b", "Chairs", "OUR  shop", "Chairs for every room", "Shop with us");
        var c = Page("/c", "Tables", "Tables", "Same", "Same share");
        var d = Page("/d", "Tables", "Tables", "Same", "Same share");
        var rules = new DuplicateRules(new AnalysisSettings());
        foreach (var page in new[] { a, b, c, d })
        {
            rules.Observe(page);
        }

        var hits = rules.Finish().ToList();
        // c and d share their titles and descriptions too: those are title-duplicate's and description-duplicate's.
        Assert.Equal([a.UrlId, b.UrlId], hits.Where(hit => hit.Issue == IssueCatalog.OG_TITLE_DUPLICATE).Select(hit => hit.UrlId).Order());
        Assert.Equal([a.UrlId, b.UrlId], hits.Where(hit => hit.Issue == IssueCatalog.OG_DESCRIPTION_DUPLICATE).Select(hit => hit.UrlId).Order());
        Assert.All(hits.Where(hit => hit.Issue == IssueCatalog.OG_TITLE_DUPLICATE), hit => Assert.Equal("1 other page", hit.Detail));
    }

    /// <summary>
    /// A page whose share description is its own (a home page's, which a platform gives every page without one) is the
    /// one the others copied: they're flagged, and it isn't, as found on jitsunami.com's contact, cart and policy pages.
    /// </summary>
    [Fact]
    public void ThePageWhoseShareTextIsItsOwnIsntTheDuplicate()
    {
        var site = new RuleHarness();
        AnalyzedPage Page(string path, string title, string? description) =>
            site.Page($"{site.Site}{path}", RuleHarness.Html(
                $"<title>{title}</title>{(description is null ? "" : $"<meta name=\"description\" content=\"{description}\">")}" +
                $"<meta property=\"og:title\" content=\"{title}\"><meta property=\"og:description\" content=\"Hand crafted belts\">"));
        var home = Page("/", "Belts", "Hand crafted belts");
        var contact = Page("/contact", "Contact", null);
        var cart = Page("/cart", "Cart", null);
        var rules = new DuplicateRules(new AnalysisSettings());
        foreach (var page in new[] { home, contact, cart })
        {
            rules.Observe(page);
        }

        var hits = rules.Finish().Where(hit => hit.Issue == IssueCatalog.OG_DESCRIPTION_DUPLICATE).OrderBy(hit => hit.UrlId).ToList();
        Assert.Equal([contact.UrlId, cart.UrlId], hits.Select(hit => hit.UrlId).Order());
        Assert.All(hits, hit => Assert.Equal("2 other pages", hit.Detail));
        // The cluster is named after its first page flagged, which names another.
        var first = Math.Min(contact.UrlId, cart.UrlId);
        Assert.Equal([first], hits.Single(hit => hit.UrlId == first).Targets!);
    }

    private static List<IssueDefinition> Issues(string head, IndexabilityReason reason = IndexabilityReason.None)
    {
        var site = new RuleHarness();
        var page = site.Page($"{site.Site}/page", RuleHarness.Html(head), reason);
        return [.. new SocialRules(site.Table()).Check(page).Select(hit => hit.Issue)];
    }

    private static string? Detail(string head, IssueDefinition check)
    {
        var site = new RuleHarness();
        var page = site.Page($"{site.Site}/page", RuleHarness.Html(head));
        return new SocialRules(site.Table()).Check(page).Where(hit => hit.Issue == check).Select(hit => hit.Detail).SingleOrDefault();
    }
}
