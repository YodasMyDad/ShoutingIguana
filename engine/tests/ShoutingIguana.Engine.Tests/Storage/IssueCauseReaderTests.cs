using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using static ShoutingIguana.Engine.Tests.Support.GridQueries;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// The checks as work to do, against the reference: the separate things each check's issues come to, as its groups show
/// them, the ones likely in the template, and the pages a fix of one check clears of another the catalog relates it to,
/// counted from every target each page names.
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class IssueCauseReaderTests(GridCrawl crawl)
{
    [Fact]
    public void EveryCheckThatFoundSomethingComesToTheThingsItsGroupsShow()
    {
        var read = IssueCauseReader.Read(crawl.Path)!;
        Assert.Equal(IssueReader.ReadAnalysis(crawl.Path)!.AnalyzedAt, read.AnalyzedAt);
        var causes = read.Checks;
        Assert.Equal(Checks().Order(StringComparer.Ordinal), causes.Keys.Order(StringComparer.Ordinal));

        var pagesAnalyzed = IssueReader.ReadAnalysis(crawl.Path)!.PagesAnalyzed;
        HashSet<IssueGrouping> seen = [];
        var templates = 0L;
        var fewer = 0;
        foreach (var check in Checks())
        {
            var definition = IssueCatalog.Find(check)!;
            var flagged = Flagged(check).Count;
            var (expected, template) = definition.Grouping switch
            {
                IssueGrouping.None => IssueGroupReader.IsLikelyTemplateCheck(definition, flagged, pagesAnalyzed) ? (1L, 1L) : (flagged, 0L),
                // How each URL answered: two 404s are two things to fix, however many share the status.
                IssueGrouping.ResponseDetail => (flagged, 0L),
                _ => Grouped(check)
            };

            Assert.True(expected == causes[check].Causes, $"{check}: {causes[check].Causes} things to change, expected {expected}");
            Assert.Equal(template, causes[check].InTemplate);
            Assert.InRange(causes[check].InTemplate, 0, causes[check].Causes);
            seen.Add(definition.Grouping);
            templates += template;
            if (causes[check].Causes < flagged)
            {
                fewer++;
            }
        }

        // No check comes to more things than it flagged URLs, even one whose pages each name several targets.
        Assert.All(Checks(), check => Assert.InRange(causes[check].Causes, 1, Flagged(check).Count));

        // Every kind of grouping was met, and some checks come to fewer things than URLs. The synthetic site's template has
        // no fault the checks flag on half their pages: the next test plants one.
        Assert.Equal(Enum.GetValues<IssueGrouping>(), seen.Order());
        Assert.Equal(0, templates);
        Assert.True(fewer > 0, "no check came to fewer things to change than URLs");
    }

    /// <summary>The same fault in the HTML of most of a check's pages is one thing to change, in the template.</summary>
    [Fact]
    public void ATemplateFaultIsOneThingToChange()
    {
        var copy = crawl.Copy();
        Change(copy, """
            INSERT INTO issues (issue, url_id, detail, related_url_id)
            SELECT 'open-graph-missing', s.url_id, 'Missing og:image', NULL FROM url_summary s JOIN urls u ON u.id = s.url_id
            WHERE s.resource_type = 0 AND s.status_code = 200 AND u.url LIKE '%/p/1__'
              AND NOT EXISTS (SELECT 1 FROM issues i WHERE i.issue = 'open-graph-missing' AND i.url_id = s.url_id);
            UPDATE issue_types SET url_count = (SELECT COUNT(*) FROM issues WHERE issue = 'open-graph-missing') WHERE id = 'open-graph-missing';
            """);
        var groups = IssueGroupReader.ReadGroups(copy, "open-graph-missing", 0, 1_000)!;
        var template = Assert.Single(groups.Groups, group => group.LikelyTemplate);

        // The template's group is one thing, each other page with the fault its own.
        var causes = IssueCauseReader.Read(copy)!.Checks["open-graph-missing"];
        Assert.Equal(1, causes.InTemplate);
        Assert.Equal(1 + groups.Total!.Value - template.Pages, causes.Causes);
        Assert.True(causes.Causes < groups.Total);
    }

    /// <summary>
    /// A fix clears another check's page only when every target the page names is one of the fixed check's URLs: a page
    /// that also links to another broken URL still has the issue. Only the checks the catalog relates, both having found
    /// something, and at least one page.
    /// </summary>
    [Fact]
    public void AFixClearsThePagesWhoseEveryTargetItFixes()
    {
        var causes = IssueCauseReader.Read(crawl.Path)!.Checks;
        var related = 0;
        foreach (var check in Checks())
        {
            var fixedUrls = Flagged(check).Select(issue => issue.Url).ToHashSet();
            List<ClearedCheck> expected = [];
            foreach (var other in IssueCatalog.Find(check)!.Clears.Where(Checks().Contains))
            {
                // A check of the URL itself (a sitemap entry that fails) clears where it's the fixed URL; one of the pages
                // naming targets, where every target it names is fixed.
                var pages = IssueCatalog.Find(other)!.References == IssueReferences.Targets
                    ? Flagged(other).Count(issue => crawl.Reference.Targets[(other, issue.Url)].All(fixedUrls.Contains))
                    : Flagged(other).Count(issue => fixedUrls.Contains(issue.Url));
                if (pages > 0)
                {
                    expected.Add(new ClearedCheck(other, pages, pages == Flagged(other).Count));
                }
            }

            Assert.Equal(expected, causes[check].Clears);
            related += expected.Count;
        }

        // The synthetic site's 404s are linked from its pages: restoring them clears those links. Its missing share image is
        // og-image-broken's only one: restoring it clears that page.
        Assert.Contains(causes["internal-client-error"].Clears, cleared => cleared.CheckId == "broken-internal-links");
        Assert.Contains(causes["internal-client-error"].Clears, cleared => cleared.CheckId == "social-image-broken" && cleared.Pages == 1);
        Assert.True(related > 0);
    }

    /// <summary>
    /// The "can I fix it from this?" pass: a sitemap entry that fails with a server error is one job with the error, as
    /// fixing the error makes the entry a working page again ("Fixed along with the problem above").
    /// </summary>
    [Fact]
    public void FixingAServerErrorClearsTheSitemapEntryOfTheSameUrl()
    {
        Assert.Contains("sitemap-non-indexable", IssueCatalog.INTERNAL_SERVER_ERROR.Clears);
        var copy = crawl.Copy();
        Change(copy, """
            INSERT OR IGNORE INTO issues (issue, url_id, detail, related_url_id)
            SELECT 'internal-server-error', url_id, '500 Internal Server Error', NULL FROM issues WHERE issue = 'sitemap-non-indexable';
            UPDATE issue_types SET url_count = (SELECT COUNT(*) FROM issues WHERE issue = 'internal-server-error') WHERE id = 'internal-server-error';
            """);

        var listed = Flagged("sitemap-non-indexable").Count;
        var cleared = Assert.Single(IssueCauseReader.Read(copy)!.Checks["internal-server-error"].Clears, check => check.CheckId == "sitemap-non-indexable");
        Assert.Equal(new ClearedCheck("sitemap-non-indexable", listed, All: true), cleared);
    }

    [Fact]
    public void ACrawlNeverAnalysedHasNoCausesAndAnEarlierAnalysisClearsNothing()
    {
        var neverAnalysed = crawl.Copy();
        Change(neverAnalysed, "DELETE FROM analysis");
        Assert.Null(IssueCauseReader.Read(neverAnalysed));

        // An analysis from before every target was kept can't tell a page that names a second broken URL: it says nothing
        // is cleared rather than guess, and still counts its causes.
        var earlier = crawl.Copy();
        Change(earlier, "DROP TABLE issue_targets");
        var causes = IssueCauseReader.Read(earlier)!.Checks;
        Assert.All(causes.Values, check => Assert.Empty(check.Clears));
        Assert.Equal(IssueCauseReader.Read(crawl.Path)!.Checks["internal-client-error"].Causes, causes["internal-client-error"].Causes);
    }

    [Fact]
    public void ARepeatedReadIsKeptWhileTheFileStandsStill()
    {
        var cache = new ReadCache();
        var first = IssueCauseReader.Read(crawl.Path, cache);
        Assert.Same(first, IssueCauseReader.Read(crawl.Path, cache));
    }

    /// <summary>
    /// A grouped check's things to change from its groups as the Issues screen shows them: one for a shared cause, never
    /// more than the URLs it flagged.
    /// </summary>
    private (long Causes, long InTemplate) Grouped(string check)
    {
        var groups = IssueGroupReader.ReadGroups(crawl.Path, check, 0, 1_000, IssueGroupSort.Pages)!;
        Assert.True(groups.GroupCount <= 1_000, $"{check} has more groups than one page reads");
        var shares = check == IssueCatalog.BROKEN_FRAGMENT_LINKS.Id;
        var things = Math.Min(groups.Groups.Sum(group => group.LikelyTemplate || group.RelatedUrlId is not null || shares ? 1 : group.Pages), groups.Total!.Value);
        return (things, Math.Min(groups.Groups.Count(group => group.LikelyTemplate), things));
    }

    private List<string> Checks()
    {
        var flagged = crawl.Reference.Issues.SelectMany(entry => entry.Value).Select(issue => issue.Issue).ToHashSet(StringComparer.Ordinal);
        return [.. IssueCatalog.All.Select(check => check.Id).Where(flagged.Contains)];
    }

    private List<(int Url, string? Detail, int? Related)> Flagged(string check) =>
    [
        .. crawl.Reference.Issues.SelectMany(entry => entry.Value.Where(issue => issue.Issue == check).Select(issue => (entry.Key, issue.Detail, issue.Related)))
    ];
}
