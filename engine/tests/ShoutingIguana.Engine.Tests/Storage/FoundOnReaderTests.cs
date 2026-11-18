using System.Globalization;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using static ShoutingIguana.Engine.Tests.Support.GridQueries;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// Where to fix what a check flags, against the links table: for each URL other URLs refer to, the links to it, the pages
/// they're on (exact, or a floor past the links counted), the first of them in crawl order, the sitemaps listing it and
/// whether it's the start URL; for each page holding bad references, every URL its issue names, with its status and the
/// page's link to it. Every check the catalog says is one or the other finds where on the synthetic site.
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class FoundOnReaderTests(GridCrawl crawl)
{
    [Fact]
    public void EveryCheckSaysWhereItsUrlsAreFound()
    {
        // Each check is classified on purpose: a new one that refers to other URLs must say so.
        string[] sources =
        [
            "internal-client-error", "internal-server-error", "internal-unreachable", "internal-blocked", "redirect-chain", "redirect-loop",
            "redirect-broken", "redirect-temporary", "redirect-downgrade", "orphan-page", "blocked-by-robots", "sitemap-non-indexable",
            "sitemap-url-other-host", "http-page", "url-uppercase", "url-underscores", "canonical-on-redirect", "canonical-unlinked",
            "non-indexable-inlinks-only", "content-type-mismatch", "redirect-temporary-cached", "resource-past-crawl-limit", "sitemap-url-repeated",
            "url-tracking-parameters", "url-multiple-slashes", "url-space", "url-parameters", "url-non-ascii", "url-repetitive-path", "url-internal-search",
            "hreflang-unlinked"
        ];
        string[] targets =
        [
            "broken-internal-links", "broken-external-links", "links-to-redirects", "internal-nofollow-links", "empty-anchor-text",
            "broken-fragment-links", "canonicalised-page", "canonical-broken", "canonical-to-redirect", "canonical-to-non-indexable",
            "noindex-with-canonical", "image-broken", "image-too-large", "hreflang-missing-return", "hreflang-broken-target",
            "hreflang-to-non-indexable", "mixed-content", "image-missing-alt", "image-alt-too-long", "image-missing-dimensions",
            "canonical-to-first-page", "canonical-other-domain", "canonical-loop", "broken-resources", "links-to-localhost", "generic-anchor-text",
            "slow-external-links", "pagination-not-linked", "pagination-broken", "pagination-to-non-indexable",
            "image-no-srcset", "image-legacy-format", "image-alt-unhelpful", "image-hotlinked", "hreflang-inconsistent-code",
            "social-image-broken", "social-image-too-large", "social-image-too-small"
        ];
        Assert.Equal(sources.Order(), IssueCatalog.All.Where(check => check.References == IssueReferences.Sources).Select(check => check.Id).Order());
        Assert.Equal(targets.Order(), IssueCatalog.All.Where(check => check.References == IssueReferences.Targets).Select(check => check.Id).Order());
    }

    /// <summary>
    /// Each URL a "sources" check flags on the synthetic site is found somewhere: linked, listed in a sitemap, or the start
    /// URL; and each page a "targets" check flags names a URL (but its links to its own missing sections).
    /// </summary>
    [Fact]
    public void EveryFlaggedUrlIsFoundAndEveryFlaggedPageNamesItsTargets()
    {
        foreach (var check in IssueCatalog.All.Where(check => check.References != IssueReferences.None))
        {
            var flagged = Flagged(check.Id);
            Assert.True(flagged.Count > 0 != NotOnTheSyntheticSite(check.Id), $"{check.Id} flags {flagged.Count} on the synthetic site");
            foreach (var chunk in flagged.Chunk(FoundOnReader.MAX_URLS))
            {
                if (check.References == IssueReferences.Sources)
                {
                    var found = FoundOnReader.ReadFoundOn(crawl.Path, chunk, 1);
                    Assert.Equal(chunk, found.Select(url => url.UrlId));
                    Assert.All(found, url => Assert.True(url.Links > 0 || url.Sitemaps.Total > 0 || url.Start, $"{check.Id}: {Url(url.UrlId)} is found nowhere"));
                }
                else
                {
                    var named = FoundOnReader.ReadTargets(crawl.Path, check.Id, chunk, 1)!;
                    Assert.Equal(chunk, named.Select(url => url.UrlId));
                    Assert.All(named, url => Assert.True(url.Total > 0 || check == IssueCatalog.BROKEN_FRAGMENT_LINKS, $"{check.Id}: {Url(url.UrlId)} names nothing"));
                }
            }
        }
    }

    [Fact]
    public void ABrokenUrlsLinksPagesAndSitemapsAreTheLinksTables()
    {
        var broken = Flagged("internal-client-error");
        Assert.NotEmpty(broken);
        var found = FoundOnReader.ReadFoundOn(crawl.Path, [.. broken.Take(FoundOnReader.MAX_URLS)], 3);
        foreach (var url in found)
        {
            var links = Query($"SELECT from_id FROM links WHERE to_id = {url.UrlId} AND from_id <> {url.UrlId} ORDER BY rowid");
            Assert.Equal(links.Count, url.Links);
            Assert.Equal(links.Distinct().Count(), url.Pages);
            Assert.True(url.PagesExact);
            Assert.Equal(links.Take(3).Select(int.Parse), url.Sources.Select(source => source.Id));
            Assert.Equal(Query($"SELECT sitemap_id FROM sitemap_urls WHERE url_id = {url.UrlId}").Count, url.Sitemaps.Total);
            Assert.False(url.Start);

            // Each source is the inspector's inlink, so the two can't differ.
            var inlinks = UrlInspectorReader.ReadLinks(crawl.Path, url.UrlId, LinkDirection.Inlinks, new LinkQuery { Limit = 3 })!;
            Assert.Equal(inlinks.Rows, url.Sources);
        }

        // A broken link planted in a page's content is found with its text, position and whether it's followed.
        var planted = found.First(url => url.Sources.Any(source => source.Kind == LinkKind.Hyperlink));
        var link = planted.Sources.First(source => source.Kind == LinkKind.Hyperlink);
        Assert.NotNull(link.Position);
        Assert.NotNull(link.Followed);
    }

    [Fact]
    public void TheStartUrlAndASitemapEntryAreFoundThere()
    {
        var home = Query("SELECT id FROM urls WHERE source = 0 ORDER BY id LIMIT 1").Select(int.Parse).Single();
        Assert.True(FoundOnReader.ReadFoundOn(crawl.Path, [home]).Single().Start);

        var listed = Query("SELECT url_id FROM sitemap_urls ORDER BY url_id LIMIT 1").Select(int.Parse).Single();
        var sitemaps = FoundOnReader.ReadFoundOn(crawl.Path, [listed]).Single().Sitemaps;
        Assert.Equal(Query($"SELECT s.url FROM sitemaps s JOIN sitemap_urls m ON m.sitemap_id = s.id WHERE m.url_id = {listed} ORDER BY s.id"),
            sitemaps.Sitemaps.Select(sitemap => sitemap.Url));
    }

    [Fact]
    public void PastTheLinksCountedThePagesAreAFloor()
    {
        var home = Query("SELECT id FROM urls WHERE source = 0 ORDER BY id LIMIT 1").Select(int.Parse).Single();
        var links = Query($"SELECT from_id FROM links WHERE to_id = {home} AND from_id <> {home} ORDER BY rowid");
        Assert.True(links.Count > 40);
        FoundOnReader.PagesCountedLinks = 20;
        try
        {
            var found = FoundOnReader.ReadFoundOn(crawl.Path, [home], 0, new ReadCache()).Single();
            Assert.Equal(links.Count, found.Links);
            Assert.False(found.PagesExact);
            Assert.InRange(found.Pages, links.Take(20).Distinct().Count(), links.Distinct().Count());
            Assert.Empty(found.Sources);
        }
        finally
        {
            FoundOnReader.PagesCountedLinks = FoundOnReader.PAGES_COUNTED_LINKS;
        }
    }

    [Fact]
    public void ABrokenLinksPageNamesEachTargetWithItsLink()
    {
        var pages = Flagged("broken-internal-links");
        var named = FoundOnReader.ReadTargets(crawl.Path, "broken-internal-links", [.. pages.Take(FoundOnReader.MAX_URLS)], 2)!;
        Assert.NotEmpty(named);
        foreach (var page in named)
        {
            var targets = Query($"SELECT related_url_id FROM issue_targets WHERE issue = 'broken-internal-links' AND url_id = {page.UrlId} AND related_url_id <> 0 ORDER BY related_url_id");
            Assert.Equal(targets.Count, page.Total);
            Assert.Equal(targets.Take(2).Select(int.Parse), page.Targets.Select(target => target.Id));
            foreach (var target in page.Targets)
            {
                Assert.True(target.StatusCode is >= 400 || target.Outcome is not null, $"{target.Url} isn't broken");
                var link = Query($"SELECT kind || ' ' || position || ' ' || COALESCE(text, '') FROM links WHERE from_id = {page.UrlId} AND to_id = {target.Id} ORDER BY kind <> 0, rowid LIMIT 1").Single();
                Assert.Equal(link, $"{(int)target.Kind!} {(int)target.Position!} {target.Text}");
            }
        }
    }

    /// <summary>
    /// A share image the page also shows is named by its share tag: the link the check is about, of the two (batch 9).
    /// </summary>
    [Fact]
    public void AShareImageIsNamedByItsShareTagWhereThePageAlsoShowsIt()
    {
        var page = Flagged("social-image-too-small").Single(id => Url(id).EndsWith("/plants/og-image-small", StringComparison.Ordinal));
        var target = Assert.Single(FoundOnReader.ReadTargets(crawl.Path, "social-image-too-small", [page])!.Single().Targets);
        Assert.Equal((LinkKind.SocialImage, "og:image", LinkPosition.Head), (target.Kind, target.Text, target.Position));
        Assert.Equal(["1"], Query($"SELECT COUNT(*) FROM links WHERE from_id = {page} AND to_id = {target.Id} AND kind = {(int)LinkKind.Image}"));
    }

    /// <summary>
    /// A page's logo linking the page itself, with nothing to read, is named as the page itself, and its link is that one:
    /// not the page's "#" and "#top" links to itself, which are jumps within it that the check passes.
    /// </summary>
    [Fact]
    public void AnEmptyLinkToThePageItselfIsReadAsThatLinkNotAJumpWithinThePage()
    {
        var page = Flagged("empty-anchor-text").Single(id => Url(id).EndsWith("/plants/empty-self-link", StringComparison.Ordinal));
        var named = FoundOnReader.ReadTargets(crawl.Path, "empty-anchor-text", [page])!.Single();
        var target = Assert.Single(named.Targets);
        Assert.Equal(page, target.Id);
        Assert.Equal(LinkKind.Hyperlink, target.Kind);
        Assert.Null(target.Text);
        Assert.Null(target.Fragment);
        Assert.Equal(LinkFlags.ImageLink | LinkFlags.SamePage, target.Flags);
        Assert.Equal(LinkPosition.Content, target.Position);

        // Its links are the one the check flags: the jumps within the page are links to it, but not ones without text.
        Assert.Equal(1, target.Links);
        Assert.Equal(["3"], Query($"SELECT COUNT(*) FROM links WHERE from_id = {page} AND to_id = {page} AND kind = {(int)LinkKind.Hyperlink}"));
    }

    /// <summary>
    /// A page's link to a missing section is the one named: on /plants/fragments, the link to fragment-target#missing, not
    /// the working one to #present written before it. Its links are the page's links there that miss: that one.
    /// </summary>
    [Fact]
    public void ABrokenFragmentIsNamedByTheLinkWhoseSectionIsMissing()
    {
        var page = Flagged("broken-fragment-links").Single(id => Url(id).EndsWith("/plants/fragments", StringComparison.Ordinal));
        var target = Assert.Single(FoundOnReader.ReadTargets(crawl.Path, "broken-fragment-links", [page])!.Single().Targets);
        Assert.EndsWith("/plants/fragment-target", target.Url, StringComparison.Ordinal);
        Assert.Equal(("missing", "Missing section", 1), (target.Fragment, target.Text, target.Links));
        Assert.Equal(["2"], Query($"SELECT COUNT(*) FROM links WHERE from_id = {page} AND to_id = {target.Id}"));
    }

    /// <summary>
    /// A page linking a URL twice, once with nofollow, names the nofollow link, and counts it alone: the followed one is fine.
    /// </summary>
    [Fact]
    public void ANofollowTargetIsNamedAndCountedByItsNofollowLinksOnly()
    {
        var page = Flagged("internal-nofollow-links").Single(id => Url(id).EndsWith("/plants/nofollow-link", StringComparison.Ordinal));
        var named = FoundOnReader.ReadTargets(crawl.Path, "internal-nofollow-links", [page], FoundOnReader.MAX_LISTED)!.Single();
        var thin = Assert.Single(named.Targets, target => target.Url.EndsWith("/plants/thin", StringComparison.Ordinal));
        Assert.Equal((LinkFlags.NoFollow, 1), (thin.Flags, thin.Links));
        Assert.Equal(["2"], Query($"SELECT COUNT(*) FROM links WHERE from_id = {page} AND to_id = {thin.Id}"));
    }

    [Fact]
    public void AskedOnceInOrderUnknownLeftOutAndBounded()
    {
        var broken = Flagged("internal-client-error");
        int[] asked = [broken[1], 999_999, broken[0], broken[1]];
        Assert.Equal([broken[1], broken[0]], FoundOnReader.ReadFoundOn(crawl.Path, asked).Select(url => url.UrlId));
        Assert.Throws<UrlQueryException>(() => FoundOnReader.ReadFoundOn(crawl.Path, []));
        Assert.Throws<UrlQueryException>(() => FoundOnReader.ReadFoundOn(crawl.Path, [.. Enumerable.Range(1, FoundOnReader.MAX_URLS + 1)]));
        Assert.Throws<UrlQueryException>(() => FoundOnReader.ReadFoundOn(crawl.Path, [broken[0]], FoundOnReader.MAX_LISTED + 1));
        Assert.Throws<UrlQueryException>(() => FoundOnReader.ReadTargets(crawl.Path, "broken-internal-links", [broken[0]], -1));

        // A URL the check didn't flag names nothing.
        Assert.Empty(FoundOnReader.ReadTargets(crawl.Path, "broken-internal-links", [broken[0]])!);
    }

    [Fact]
    public void ACheckNotRunNamesNothingAndACrawlNeverAnalysedHasNoTargets()
    {
        var pages = Flagged("broken-internal-links");
        var copy = crawl.Copy();
        Change(copy, "DELETE FROM issue_types WHERE id = 'broken-internal-links'");
        Assert.Empty(FoundOnReader.ReadTargets(copy, "broken-internal-links", [pages[0]])!);
        Assert.Empty(FoundOnReader.ReadTargets(crawl.Path, "no-such-check", [pages[0]])!);

        var neverAnalysed = crawl.Copy();
        Change(neverAnalysed, "DELETE FROM analysis");
        Assert.Null(FoundOnReader.ReadTargets(neverAnalysed, "broken-internal-links", [pages[0]]));

        // Where it's found needs no analysis: it's the crawl's own links.
        var broken = Flagged("internal-client-error")[0];
        Assert.Equal(FoundOnReader.ReadFoundOn(crawl.Path, [broken]).Single().Links, FoundOnReader.ReadFoundOn(neverAnalysed, [broken]).Single().Links);
    }

    /// <summary>Checks the synthetic site plants only when served over HTTPS (a linked redirect to http, a page loading http resources).</summary>
    /// <summary>
    /// Checks the synthetic site can't plant: the HTTPS ones (it's served over HTTP here), and links to private addresses, as
    /// the site and its partner are both on 127.0.0.1, one machine's addresses.
    /// </summary>
    private static bool NotOnTheSyntheticSite(string check) => check is "redirect-downgrade" or "mixed-content" or "http-page" or "links-to-localhost" or "image-hotlinked";

    private List<int> Flagged(string check) =>
    [
        .. crawl.Reference.Issues.Where(entry => entry.Value.Any(issue => issue.Issue == check)).Select(entry => entry.Key).Order()
    ];

    private string Url(int id) => Query($"SELECT url FROM urls WHERE id = {id}").SingleOrDefault() ?? id.ToString(CultureInfo.InvariantCulture);

    /// <summary>The first column of a query's rows, in its order.</summary>
    private List<string> Query(string sql)
    {
        using var connection = CrawlDatabase.OpenReader(crawl.Path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        List<string> rows = [];
        while (reader.Read())
        {
            rows.Add(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)!);
        }

        return rows;
    }
}
