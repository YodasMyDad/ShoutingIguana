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
        "slow-response", "large-page", "viewport-missing", "lang-missing", "charset-missing", "open-graph-missing"
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
            Assert.EndsWith(".", issue.Fix);
            Assert.Same(issue, IssueCatalog.Find(issue.Id));
        });
        Assert.Null(IssueCatalog.Find("no-such-issue"));
    }

    [Fact]
    public void DefaultSettingsAreValidAndBadOnesAreRejected()
    {
        Assert.Empty(new AnalysisSettings().Validate());
        Assert.NotEmpty(new AnalysisSettings { TitleMinLength = 70, TitleMaxLength = 60 }.Validate());
        Assert.NotEmpty(new AnalysisSettings { NearDuplicateMaxDistance = 9 }.Validate());
        Assert.NotEmpty(new AnalysisSettings { LargeImageBytes = 0 }.Validate());
    }
}
