using System.Reflection;
using System.Text.RegularExpressions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Tests.Analysis;

public sealed class IssueCatalogTests
{
    /// <summary>
    /// Every id ever shipped. Ids are stored in crawl files, so one may be added here but never renamed or removed:
    /// older files would show issues the catalog no longer knows.
    /// </summary>
    private static readonly string[] SHIPPED_IDS =
    [
        "internal-client-error", "internal-server-error", "internal-unreachable", "internal-blocked", "soft-404",
        "redirect-chain", "redirect-loop", "redirect-broken", "redirect-temporary", "redirect-downgrade", "meta-refresh",
        "broken-internal-links", "broken-external-links", "links-to-redirects", "internal-nofollow-links", "empty-anchor-text",
        "broken-fragment-links", "orphan-page", "noindex-page", "nofollow-page", "blocked-by-robots", "snippet-blocked",
        "robots-txt-unreachable", "robots-txt-missing", "canonicalised-page", "canonical-missing", "canonical-multiple",
        "canonical-broken", "canonical-to-redirect", "canonical-to-non-indexable", "canonical-outside-head", "canonical-relative",
        "canonical-invalid", "sitemap-non-indexable", "not-in-sitemap", "sitemap-error", "sitemap-missing", "title-missing",
        "title-multiple", "title-too-long", "title-too-short", "title-duplicate", "description-missing", "description-multiple",
        "description-too-long", "description-too-short", "description-duplicate", "h1-missing", "h1-multiple", "h1-duplicate",
        "low-word-count", "duplicate-content", "near-duplicate-content", "image-missing-alt", "image-alt-too-long",
        "image-missing-dimensions", "image-broken", "image-too-large", "hreflang-invalid-code", "hreflang-missing-self",
        "hreflang-missing-return", "hreflang-broken-target", "hreflang-to-non-indexable", "hreflang-duplicate-code",
        "hreflang-missing-x-default", "hreflang-outside-head", "structured-data-invalid", "structured-data-missing-required",
        "no-https", "http-page", "mixed-content", "security-headers-missing", "url-uppercase", "url-underscores", "url-too-long",
        "slow-response", "large-page", "html-past-crawl-limit", "viewport-missing", "lang-missing", "charset-missing", "open-graph-missing",
        "sitemap-url-other-host", "noindex-with-canonical", "robots-txt-unreachable-other-origin", "duplicate-site-version",
        "site-hidden-from-search",

        // The new checks of step 3b (docs/v2-new-checks.md), batch 1.
        "robots-directives-conflict", "noindex-near-home", "unavailable-after-passed", "media-preview-blocked", "translation-blocked",
        "indexifembedded-without-noindex", "canonical-to-first-page", "canonical-other-domain", "canonical-loop", "canonical-on-redirect",
        "canonical-fragment", "canonical-invalid-attribute", "canonical-unlinked",

        // Batch 2.
        "uncrawlable-links", "broken-resources", "links-to-localhost", "few-internal-outlinks", "generic-anchor-text",
        "non-indexable-inlinks-only", "deep-page", "too-many-links", "slow-external-links", "pagination-not-linked", "pagination-broken",
        "pagination-to-non-indexable", "pagination-sequence-error",

        // Batch 3.
        "content-type-mismatch", "redirect-temporary-cached", "resource-past-crawl-limit", "head-tags-in-body", "html-structure-errors",
        "viewport-invalid", "charset-invalid", "lang-invalid", "mobile-alternate",
        "robots-txt-no-sitemap", "sitemap-over-limit", "sitemap-near-limit", "sitemap-empty", "sitemap-url-repeated", "sitemap-lastmod-invalid"
    ];

    [Fact]
    public void ShippedIdsAreNeverRenamedOrRemoved()
    {
        var ids = IssueCatalog.All.Select(issue => issue.Id).ToList();
        var removed = SHIPPED_IDS.Except(ids, StringComparer.Ordinal).ToList();
        var added = ids.Except(SHIPPED_IDS, StringComparer.Ordinal).ToList();

        Assert.True(removed.Count == 0, $"Shipped issue ids missing from the catalog: {string.Join(", ", removed)}");
        Assert.True(added.Count == 0, $"Add new issue ids to SHIPPED_IDS: {string.Join(", ", added)}");
    }

    [Fact]
    public void EveryDeclaredIssueIsInTheCatalogOnce()
    {
        var declared = typeof(IssueCatalog).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(IssueDefinition))
            .Select(field => (IssueDefinition)field.GetValue(null)!)
            .ToList();

        Assert.Equal(declared.Count, IssueCatalog.All.Count);
        Assert.All(declared, issue => Assert.Contains(issue, IssueCatalog.All));
        Assert.Equal(IssueCatalog.All.Count, IssueCatalog.All.Select(issue => issue.Id).Distinct().Count());
    }

    [Fact]
    public void IdsAreKebabCaseAndTextsAreFilledIn()
    {
        Assert.All(IssueCatalog.All, issue =>
        {
            Assert.Matches(new Regex("^[a-z0-9]+(-[a-z0-9]+)*$"), issue.Id);
            Assert.False(string.IsNullOrWhiteSpace(issue.Title));
            Assert.EndsWith(".", issue.Description);
            Assert.EndsWith(".", issue.Why);
            Assert.EndsWith(".", issue.Fix);
            if (issue.LearnMore is { } learnMore)
            {
                Assert.StartsWith("https://", learnMore);
                Assert.True(Uri.IsWellFormedUriString(learnMore, UriKind.Absolute));
            }

            Assert.Same(issue, IssueCatalog.Find(issue.Id));
        });
        Assert.Null(IssueCatalog.Find("no-such-issue"));
    }

    /// <summary>
    /// The technical texts the review of 2026-11-11 found saying something other than their rules do, each held to what
    /// its rule does (the rules' own tests show that): a title that claimed more or less than its rule finds, a fix that
    /// named the wrong URL, a cause the rule can't flag, and what three checks now do.
    /// </summary>
    [Fact]
    public void TheTextsSayWhatTheirRulesDo()
    {
        var hidden = IssueCatalog.SITE_HIDDEN_FROM_SEARCH;

        // Every text is judged, so one that slips doesn't hide another.
        Assert.Multiple(
            // 4xx, a firewall's 401 or 403 among them, and too many redirects: the file may well exist.
            () => Assert.Equal("robots.txt missing or refused", IssueCatalog.ROBOTS_TXT_MISSING.Title),

            // An empty alt has no text and passes: the rule looks for the attribute.
            () => Assert.Equal("Images without an alt attribute", IssueCatalog.IMAGE_MISSING_ALT.Title),

            // Only pages a sitemap lists are tested.
            () => Assert.Equal("Orphan page in a sitemap", IssueCatalog.ORPHAN_PAGE.Title),

            // A redirect with no usable Location leads to no URL at all, and is reported too.
            () => Assert.Equal("Broken redirect", IssueCatalog.REDIRECT_BROKEN.Title),

            // The rule compares each entry with the page's URL as crawled, not with its canonical.
            () => Assert.DoesNotContain("canonical", IssueCatalog.HREFLANG_MISSING_SELF.Fix, StringComparison.OrdinalIgnoreCase),
            () => Assert.Contains("exactly as the page is served", IssueCatalog.HREFLANG_MISSING_SELF.Fix),

            // Only links to the site's own URLs are tested, and social media icons link elsewhere.
            () => Assert.DoesNotContain("social", IssueCatalog.EMPTY_ANCHOR_TEXT.Why, StringComparison.OrdinalIgnoreCase),

            // Titles and descriptions are measured in pixels, as Google's desktop results show them, with the cut as evidence.
            () => Assert.Contains("wider than 580 pixels (the default", IssueCatalog.TITLE_TOO_LONG.Description),
            () => Assert.Contains("narrower than 200 pixels (the default", IssueCatalog.TITLE_TOO_SHORT.Description),
            () => Assert.Contains("wider than 985 pixels (the default", IssueCatalog.DESCRIPTION_TOO_LONG.Description),
            () => Assert.Contains("narrower than 400 pixels (the default", IssueCatalog.DESCRIPTION_TOO_SHORT.Description),
            () => Assert.Equal(["title", "titleInResults", "titleWidth", "titleLength"], IssueCatalog.TITLE_TOO_LONG.Evidence),
            () => Assert.Equal(["title", "titleWidth", "titleLength"], IssueCatalog.TITLE_TOO_SHORT.Evidence),
            () => Assert.Equal(["description", "descriptionInResults", "descriptionWidth", "descriptionLength"], IssueCatalog.DESCRIPTION_TOO_LONG.Evidence),
            () => Assert.Equal(["description", "descriptionWidth", "descriptionLength"], IssueCatalog.DESCRIPTION_TOO_SHORT.Evidence),

            // One threshold, named as the default once.
            () => Assert.Single(Regex.Matches(IssueCatalog.NEAR_DUPLICATE_CONTENT.Description, "default")),
            () => Assert.Contains("roughly 96% or more alike (the default", IssueCatalog.NEAR_DUPLICATE_CONTENT.Description),
            () => Assert.DoesNotContain("bits", IssueCatalog.NEAR_DUPLICATE_CONTENT.Description),

            // A URL that refused the crawler isn't judged (the real-site audit, docs/v2-check-audit.md).
            () => Assert.DoesNotContain("firewall).", IssueCatalog.HREFLANG_TO_NON_INDEXABLE.Description),
            () => Assert.Contains("refused the crawler", IssueCatalog.HREFLANG_TO_NON_INDEXABLE.Description),
            () => Assert.DoesNotContain("or a firewall.", IssueCatalog.CANONICAL_TO_NON_INDEXABLE.Description),
            () => Assert.Contains("refused the crawler", IssueCatalog.CANONICAL_TO_NON_INDEXABLE.Description),

            // Temporary redirects: links to them aren't counted, and those to other sites aren't flagged.
            () => Assert.Contains("Links to temporary redirects (302, 303 or 307)", IssueCatalog.LINKS_TO_REDIRECTS.Description),
            () => Assert.Contains("Temporary redirects to other sites", IssueCatalog.REDIRECT_TEMPORARY.Description),

            // A logo linking its own page counts; a link written as a fragment doesn't.
            () => Assert.Contains("A link to the page itself counts too", IssueCatalog.EMPTY_ANCHOR_TEXT.Description),

            // Orphans are skipped once 1 in 20 of the pages couldn't be read.
            () => Assert.Contains("1 in 20", IssueCatalog.ORPHAN_PAGE.Description),

            // A meta tag counts within the first 1024 bytes only.
            () => Assert.Contains("within the first 1024 bytes", IssueCatalog.CHARSET_MISSING.Description),

            // A URL is also refused when robots.txt itself couldn't be read, and the check doesn't run on a crawl that ignored robots.txt.
            () => Assert.StartsWith("The crawler didn't fetch this internal URL", IssueCatalog.BLOCKED_BY_ROBOTS.Description),
            () => Assert.Contains("couldn't be read", IssueCatalog.BLOCKED_BY_ROBOTS.Description),
            () => Assert.Contains("ignore robots.txt", IssueCatalog.BLOCKED_BY_ROBOTS.Description),

            // A byte-order mark is looked for now.
            () => Assert.Contains("no byte-order mark", IssueCatalog.CHARSET_MISSING.Description),
            () => Assert.DoesNotContain("doesn't look for one", IssueCatalog.CHARSET_MISSING.Why),

            // The detail names where the canonical's redirects end.
            () => Assert.Contains("The detail gives the URL the redirects end at", IssueCatalog.CANONICAL_TO_REDIRECT.Description),

            // A URL the crawl didn't check, or was refused at, may redirect again: the texts don't call it the end.
            () => Assert.Contains("may not be where they end", IssueCatalog.CANONICAL_TO_REDIRECT.Description),
            () => Assert.Contains("it may redirect again", IssueCatalog.CANONICAL_TO_REDIRECT.Fix),

            // The home page must be noindex too, the check is only ever reported there, and a noindex page is never a variant.
            () => Assert.StartsWith("The home page is noindex, and so are more than half of the site's pages", hidden.Description),
            () => Assert.EndsWith("Reported once, on the home page.", hidden.Description),
            () => Assert.Contains("one that isn't noindex and whose canonical names another URL", hidden.Description),
            () => Assert.StartsWith("Your home page and more than half of your pages", hidden.Plain!.What[0]),

            // A crawl that didn't start at the site's root has no home page to judge the site by, and skips the check.
            () => Assert.Contains("Not checked on a crawl that didn't start at the site's root.", hidden.Description),

            // A crawl told to ignore robots.txt reads it only for the start origin's sitemaps, and judges only what it read.
            () => Assert.EndsWith("Not checked on a crawl told to ignore robots.txt that never read it.", IssueCatalog.ROBOTS_TXT_UNREACHABLE.Description),
            () => Assert.EndsWith("Not checked on a crawl told to ignore robots.txt, which doesn't read it there.", IssueCatalog.ROBOTS_TXT_UNREACHABLE_OTHER_ORIGIN.Description),
            () => Assert.EndsWith("A crawl told to ignore robots.txt judges the start URL's origin alone, and not at all when it never read robots.txt.",
                IssueCatalog.ROBOTS_TXT_MISSING.Description));
    }

    /// <summary>
    /// The checks that find one fault of the whole site, which the score counts as the whole site whatever their count.
    /// Each is reported on a URL that stands for the site (the home page, or the start URL without one), or on each other
    /// version of the site that answers: never on the site's pages one by one.
    /// </summary>
    [Fact]
    public void TheSiteWideChecksAreTheFaultsOfTheWholeSite()
    {
        Assert.Equal(128, IssueCatalog.All.Count);
        Assert.Equal(["duplicate-site-version", "no-https", "robots-txt-unreachable", "robots-txt-unreachable-other-origin", "site-hidden-from-search"],
            IssueCatalog.All.Where(check => check.SiteWide).Select(check => check.Id).Order(StringComparer.Ordinal));
        Assert.Equal(
            [
                (IssueCatalog.ROBOTS_TXT_UNREACHABLE, IssueSeverity.Error), (IssueCatalog.NO_HTTPS, IssueSeverity.Error),
                (IssueCatalog.SITE_HIDDEN_FROM_SEARCH, IssueSeverity.Error), (IssueCatalog.DUPLICATE_SITE_VERSION, IssueSeverity.Warning),
                (IssueCatalog.ROBOTS_TXT_UNREACHABLE_OTHER_ORIGIN, IssueSeverity.Warning)
            ],
            new[]
            {
                IssueCatalog.ROBOTS_TXT_UNREACHABLE, IssueCatalog.NO_HTTPS, IssueCatalog.SITE_HIDDEN_FROM_SEARCH, IssueCatalog.DUPLICATE_SITE_VERSION,
                IssueCatalog.ROBOTS_TXT_UNREACHABLE_OTHER_ORIGIN
            }.Select(check => (check, check.Severity)));

        // The new check sits with the site's other faults of indexability, after robots.txt's: level with one of them in
        // the order to fix, an unreadable robots.txt comes first.
        var order = IssueCatalog.All.Select(check => check.Id).ToList();
        Assert.Equal(order.IndexOf("robots-txt-missing") + 1, order.IndexOf("site-hidden-from-search"));
        Assert.Equal(IssueCategory.Indexability, IssueCatalog.SITE_HIDDEN_FROM_SEARCH.Category);
    }

    /// <summary>Hosts show the titles as they are, so every category needs a plain name of its own, never its identifier.</summary>
    [Fact]
    public void EveryCategoryHasItsOwnPlainTitle()
    {
        var titles = Enum.GetValues<IssueCategory>().Select(category => category.DisplayName()).ToList();

        Assert.All(titles, title =>
        {
            Assert.Matches(new Regex("^[A-Z][A-Za-z]*( [a-z]+)*$"), title);
            Assert.DoesNotMatch(new Regex("[a-z][A-Z]"), title);
        });
        Assert.Equal(titles.Count, titles.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal("Meta descriptions", IssueCategory.MetaDescriptions.DisplayName());
        Assert.Equal("URLs", IssueCategory.Urls.DisplayName());
        Assert.Throws<ArgumentOutOfRangeException>(() => ((IssueCategory)200).DisplayName());
    }

    [Fact]
    public void DefaultSettingsAreValidAndBadOnesAreRejected()
    {
        Assert.Empty(new AnalysisSettings().Validate());
        Assert.NotEmpty(new AnalysisSettings { TitleMinPixels = 700, TitleMaxPixels = 600 }.Validate());
        Assert.NotEmpty(new AnalysisSettings { NearDuplicateMaxBits = 33 }.Validate());
        Assert.Empty(new AnalysisSettings { NearDuplicateMaxBits = 0 }.Validate());
        Assert.NotEmpty(new AnalysisSettings { LargeImageBytes = 0 }.Validate());
    }
}
