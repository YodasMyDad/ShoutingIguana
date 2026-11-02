using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using static ShoutingIguana.Engine.Storage.Models.IndexabilityTestKind;
using static ShoutingIguana.Engine.Tests.Support.GridQueries;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// The inspector's URL in full: for every URL, agreeing with the grid's columns and with a checklist whose first failed
/// test is the reason it isn't indexable; for the planted pages, each kind of failure in the test it fails, redirects hop
/// by hop, what the page says and the limits it's judged by, the issues on it and naming it; and before an analysis, the
/// crawl's own judgement.
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class UrlInspectorReaderTests(GridCrawl crawl)
{
    [Fact]
    public void EveryUrlReadsAsTheGridShowsIt()
    {
        foreach (var (id, row) in crawl.Reference.Rows)
        {
            var url = Read(crawl.Path, id);
            var what = $"URL {id} ({row["url"]})";
            Assert.Equal(row["url"], url.Url);
            Assert.Equal(row["isInternal"], url.IsInternal);
            Assert.Equal(row["type"], Name(url.Type));
            Assert.Equal(row["source"], Name(url.Discovery.Source));
            Assert.Equal(row["depth"], (long?)url.Discovery.Depth);
            Assert.True(Equals(row["outcome"], url.Response is { } response ? Name(response.Outcome) : null), what);
            Assert.True(Equals(row["statusCode"], (long?)url.Response?.StatusCode), what);
            Assert.True(Equals(row["responseMs"], (long?)url.Response?.ResponseMs), what);
            Assert.True(Equals(row["ttfbMs"], (long?)url.Response?.TtfbMs), what);
            Assert.True(Equals(row["sizeBytes"], url.Response?.SizeBytes), what);
            Assert.True(Equals(row["indexable"], url.Indexability.Indexable), $"{what}: indexable {url.Indexability.Indexable}");
            Assert.True(Equals(row["indexability"], url.Indexability.Reason is { } reason ? Name(reason) : null), $"{what}: {url.Indexability.Reason}");
            Assert.Equal(IndexabilityJudge.Analysis, url.Indexability.JudgedBy);
            Assert.True(Equals(row["finalUrl"], url.Redirects.Final.Url), $"{what}: ends at {url.Redirects.Final.Url}");
            Assert.True(Equals(row["finalStatusCode"], (long?)url.Redirects.Final.StatusCode), what);
            var links = url.Links!;
            Assert.Equal(new[] { row["inlinks"], row["followedInlinks"], row["nofollowInlinks"], row["linkingPages"], row["outlinks"], row["inSitemap"] },
                new object?[] { (long)links.Inlinks, (long)links.FollowedInlinks, (long)links.NofollowInlinks, (long)links.LinkingPages, (long?)links.Outlinks, links.InSitemap });
            Assert.Equal(SummaryState.Present, url.Summary);
        }

        Assert.Null(UrlInspectorReader.Read(crawl.Path, crawl.Reference.Rows.Keys.Max() + 1));
    }

    /// <summary>
    /// "Indexable, and why", for every URL: an indexable one (or one whose verdict is unknown) fails no test, and one that
    /// isn't fails the test its reason names first, in the order they decide it.
    /// </summary>
    [Fact]
    public void TheChecklistExplainsEveryVerdict()
    {
        foreach (var (id, row) in crawl.Reference.Rows)
        {
            var verdict = Read(crawl.Path, id).Indexability;
            var what = $"URL {id} ({row["url"]}), {verdict.Reason?.ToString() ?? "no verdict"}: {string.Join(", ", verdict.Tests.Select(test => $"{test.Test} {test.Outcome}"))}";
            Assert.Equal([Answered, RobotsTxt, Noindex, Canonical], verdict.Tests.Select(test => test.Test));
            var failed = verdict.Tests.FirstOrDefault(test => test.Outcome == TestOutcome.Fail);
            if (verdict.Indexable != false)
            {
                Assert.True(failed is null, what);
                continue;
            }

            Assert.True(failed is not null, what);
            var decidedBy = verdict.Reason switch
            {
                IndexabilityReason.BlockedByRobots => RobotsTxt,
                IndexabilityReason.Noindex => Noindex,
                IndexabilityReason.Canonicalised => Canonical,
                _ => Answered
            };
            Assert.True(decidedBy == failed.Test, what);
        }
    }

    [Fact]
    public void EachKindOfFailureFailsItsOwnTest()
    {
        // A product page passes every test, its canonical naming itself.
        var product = Inspect("/p/7");
        Assert.True(product.Indexability.Indexable);
        Assert.Equal(IndexabilityReason.None, product.Indexability.Reason);
        Assert.All(product.Indexability.Tests, test => Assert.Equal(TestOutcome.Pass, test.Outcome));
        Assert.Equal(200, Test(product, Answered).StatusCode);
        Assert.Equal(new UrlTarget(product.Id, product.Url, 200), Test(product, Canonical).Canonical);
        Assert.False(Test(product, Noindex).Meta);
        Assert.False(Test(product, Noindex).Header);

        // It answered, but not 2xx: the status says how.
        Assert.Equal(404, AssertDecidedBy("/plants/gone", IndexabilityReason.ClientError, Answered).StatusCode);
        Assert.Equal(500, AssertDecidedBy("/err/211", IndexabilityReason.ServerError, Answered).StatusCode);
        Assert.Equal(403, AssertDecidedBy("/plants/blocked", IndexabilityReason.Blocked, Answered).StatusCode);
        Assert.Equal(301, AssertDecidedBy("/plants/chain-a", IndexabilityReason.Redirected, Answered).StatusCode);
        Assert.Null(AssertDecidedBy("/plants/unreachable", IndexabilityReason.Unreachable, Answered).StatusCode);

        // An instant meta refresh moves on, as a redirect does: the test names where to. A delayed one doesn't.
        var refresh = AssertDecidedBy("/plants/refresh-instant", IndexabilityReason.Redirected, Answered);
        Assert.Equal(200, refresh.StatusCode);
        Assert.Equal(Url("/plants/refresh-target"), refresh.MetaRefresh?.Url);
        Assert.Equal(200, refresh.MetaRefresh?.StatusCode);
        Assert.Equal(TestOutcome.Pass, Test(Inspect("/plants/meta-refresh"), Answered).Outcome);
        Assert.Null(Test(product, Answered).MetaRefresh);

        // robots.txt refused it: no request, so whether it answered, and what it says, were never checked.
        var refused = Inspect("/private/1");
        Assert.Equal(IndexabilityReason.BlockedByRobots, refused.Indexability.Reason);
        Assert.Equal([TestOutcome.NotChecked, TestOutcome.Fail, TestOutcome.NotChecked, TestOutcome.NotChecked], refused.Indexability.Tests.Select(test => test.Outcome));
        Assert.False(Test(refused, RobotsTxt).Unreachable);
        Assert.Null(refused.Response!.ResponseMs);
        Assert.Null(refused.Response.TtfbMs);

        // A noindex in a robots meta tag; with a canonical elsewhere too, the noindex decides, and the canonical still fails.
        var noindex = AssertDecidedBy("/plants/noindex", IndexabilityReason.Noindex, Noindex);
        Assert.True(noindex.Meta);
        Assert.False(noindex.Header);
        var both = Inspect("/plants/noindex-canonical");
        Assert.Equal(IndexabilityReason.Noindex, both.Indexability.Reason);
        Assert.Equal(TestOutcome.Fail, Test(both, Canonical).Outcome);
        Assert.Equal(Url("/plants/hreflang-lonely"), Test(both, Canonical).Canonical?.Url);

        // A canonical elsewhere, named with its status; one that leads back to the page, or nowhere, is ignored.
        var canonical = AssertDecidedBy("/plants/canonical-redirect", IndexabilityReason.Canonicalised, Canonical);
        Assert.Equal(Url("/plants/moved"), canonical.Canonical?.Url);
        Assert.Equal(301, canonical.Canonical?.StatusCode);
        var back = Inspect("/plants/self-canonical/");
        Assert.True(back.Indexability.Indexable);
        Assert.Equal(TestOutcome.Pass, Test(back, Canonical).Outcome);
        Assert.Equal(Url("/plants/self-canonical"), Test(back, Canonical).Canonical?.Url);
        var nowhere = Inspect("/plants/canonical-broken");
        Assert.True(nowhere.Indexability.Indexable);
        Assert.Equal(TestOutcome.Pass, Test(nowhere, Canonical).Outcome);
        Assert.Equal(new UrlTarget(Id("/plants/gone"), Url("/plants/gone"), 404), Test(nowhere, Canonical).Canonical);

        // A page only status-checked: its HTML wasn't read, so neither its directives nor its verdict are known.
        var embedded = Inspect("/plants/embedded");
        Assert.Null(embedded.Indexability.Indexable);
        Assert.Equal([TestOutcome.Pass, TestOutcome.Pass, TestOutcome.NotChecked, TestOutcome.NotChecked], embedded.Indexability.Tests.Select(test => test.Outcome));
        Assert.Null(embedded.Content);

        // Never fetched, as only a nofollow link leads there: nothing was checked.
        var hidden = Inspect("/hidden/43");
        Assert.Null(hidden.Indexability.Indexable);
        Assert.All(hidden.Indexability.Tests, test => Assert.Equal(TestOutcome.NotChecked, test.Outcome));
        Assert.Null(hidden.Response);
        Assert.Equal(UrlState.Skipped, hidden.Discovery.State);
        Assert.Equal(SkipReason.NoFollow, hidden.Discovery.NotFetchedBecause);
    }

    [Fact]
    public void RedirectsAreFollowedHopByHop()
    {
        var chain = Inspect("/plants/chain-a").Redirects;
        Assert.Equal([Url("/plants/chain-b"), Url("/plants/chain-c"), Url("/plants/thin")], chain.Hops.Select(hop => hop.Url));
        Assert.Equal([301, 301, 200], chain.Hops.Select(hop => hop.StatusCode));
        Assert.All(chain.Hops, hop => Assert.Equal(RedirectVia.Http, hop.Via));
        Assert.Equal([UrlOutcome.Redirect, UrlOutcome.Redirect, UrlOutcome.Success], chain.Hops.Select(hop => hop.Outcome));
        Assert.Equal(new UrlTarget(Id("/plants/thin"), Url("/plants/thin"), 200), chain.Final);
        Assert.False(chain.Loop);
        Assert.False(chain.TooLong);

        var loop = Inspect("/plants/loop-a").Redirects;
        Assert.True(loop.Loop);
        Assert.False(loop.TooLong);
        Assert.Equal([Url("/plants/loop-b"), Url("/plants/loop-a")], loop.Hops.Select(hop => hop.Url));

        // Browsers give up after 20 hops: so does the chain, at a redirect.
        var tooLong = Inspect("/plants/long-chain-0").Redirects;
        Assert.True(tooLong.TooLong);
        Assert.False(tooLong.Loop);
        Assert.Equal(UrlTable.MAX_REDIRECT_HOPS, tooLong.Hops.Count);
        Assert.Equal(Url($"/plants/long-chain-{UrlTable.MAX_REDIRECT_HOPS}"), tooLong.Final.Url);

        // An instant meta refresh is a hop too.
        var refresh = Inspect("/plants/refresh-instant").Redirects;
        Assert.Equal([(Url("/plants/refresh-target"), RedirectVia.MetaRefresh)], refresh.Hops.Select(hop => (hop.Url, hop.Via)));

        // Not moving on: no hops, and the URL itself is where it ends.
        var product = Inspect("/p/7");
        Assert.Empty(product.Redirects.Hops);
        Assert.Equal(new UrlTarget(product.Id, product.Url, 200), product.Redirects.Final);
    }

    [Fact]
    public void ThePageSaysWhatTheChecksJudge()
    {
        var id = Id("/p/7");
        var row = crawl.Reference.Rows[id];
        var product = Read(crawl.Path, id);
        var content = product.Content!;
        Assert.Equal([((string)row["title"]!, DisplayWidth.Measure((string)row["title"]!).Width)], content.Titles.Select(title => (title.Text, title.Length)));
        Assert.Equal([((string)row["description"]!, DisplayWidth.Measure((string)row["description"]!).Width)], content.Descriptions.Select(text => (text.Text, text.Length)));
        Assert.Equal([(string)row["h1"]!], content.H1s.Select(h1 => h1.Text));
        Assert.Equal((long)row["h1Count"]!, content.Headings[0]);
        Assert.Equal(6, content.Headings.Count);
        Assert.Equal((long)row["wordCount"]!, content.WordCount);
        Assert.Equal(0, content.ImagesMissingAlt);
        Assert.Null(content.MetaRefresh);
        Assert.Contains(product.Headers, header => header.Name == "content-type");
        Assert.True(product.SourceStored);
        Assert.Equal(new ContentLimits(30, 60, 70, 155, 200, 1_000, 1_048_576, FromAnalysis: true), product.Limits);

        Assert.True(Inspect("/plants/image-srcset").Content!.ImagesMissingAlt >= 1);
        var delayed = Inspect("/plants/meta-refresh").Content!.MetaRefresh!;
        Assert.Equal(30, delayed.DelaySeconds);
        Assert.EndsWith("/plants/thin", delayed.Url);
        Assert.Contains("noindex", Inspect("/plants/noindex").Content!.Robots);

        // Neither an image nor a page only status-checked has content; an image is no page.
        var image = Inspect("/img/1.jpg");
        Assert.Null(image.Content);
        Assert.Equal(ResourceType.Image, image.Type);
        Assert.False(image.SourceStored);

        // Structured data: each block, its types, or why it doesn't parse.
        Assert.Contains(Inspect("/p/5").StructuredData, block => block is { Valid: true, Error: null, Truncated: false } && block.Types.SequenceEqual(["Product"]));
        Assert.Empty(product.StructuredData);
        var invalid = Assert.Single(Inspect("/plants/jsonld-invalid").StructuredData, block => !block.Valid);
        Assert.NotNull(invalid.Error);
        Assert.Empty(invalid.Types);

        // Hreflang: each annotation, and the URL it names in the crawl, with its status.
        var english = Inspect("/plants/hreflang-en").Hreflang;
        Assert.Equal(
            [("en", Url("/plants/hreflang-en"), 200), ("fr", Url("/plants/hreflang-fr"), 200), ("x-default", Url("/plants/hreflang-en"), 200)],
            english.Select(entry => (entry.Language, entry.Target!.Url, entry.Target.StatusCode ?? 0)));
        Assert.All(english, entry => Assert.True(entry.InHead));
        Assert.Contains(Inspect("/plants/hreflang-broken").Hreflang, entry => entry is { Language: "de", Target.StatusCode: 404 });
    }

    [Fact]
    public void TheDiscoverySaysHowTheCrawlFoundIt()
    {
        var start = Read(crawl.Path, crawl.Reference.Rows.Single(row => (string?)row.Value["source"] == "seed").Key).Discovery;
        Assert.Equal(DiscoverySource.Seed, start.Source);
        Assert.Equal(0, start.Depth);
        Assert.Null(start.FoundOn);
        Assert.Equal(FetchMode.Crawl, start.FetchMode);
        Assert.Equal(UrlState.Done, start.State);
        Assert.Null(start.NotFetchedBecause);

        // A page first found through a link: on which page, and through what kind of link.
        var linked = crawl.Reference.Rows.First(row => (string?)row.Value["source"] == "link" && (string?)row.Value["type"] == "page" && (long?)row.Value["statusCode"] == 200);
        var page = Read(crawl.Path, linked.Key).Discovery;
        Assert.Equal(DiscoverySource.Link, page.Source);
        Assert.Equal(LinkKind.Hyperlink, page.FoundVia);
        Assert.NotNull(page.FoundOn);
        Assert.Equal(crawl.Reference.Rows[page.FoundOn.Id]["url"], page.FoundOn.Url);
        Assert.Equal(linked.Value["depth"], (long?)page.Depth);

        var orphan = Inspect("/orphan/0").Discovery;
        Assert.Equal(DiscoverySource.Sitemap, orphan.Source);
        Assert.Null(orphan.Depth);
    }

    [Fact]
    public void TheIssuesAreTheLastAnalysisOwnAndNameTheOtherUrl()
    {
        foreach (var (id, row) in crawl.Reference.Rows)
        {
            var url = Read(crawl.Path, id);
            var expected = crawl.Reference.Issues.GetValueOrDefault(id) ?? [];
            Assert.Equal(expected.Select(issue => (issue.Issue, issue.Detail, issue.Related)).Order(), url.Issues!.Select(issue => (issue.CheckId, issue.Detail, issue.Related?.Id)).Order());
            Assert.All(url.Issues!, issue => Assert.Equal(IssueCatalog.Find(issue.CheckId)!.Severity, issue.Severity));
            Assert.All(url.Issues!.Where(issue => issue.Related is not null), issue => Assert.Equal(crawl.Reference.Rows[issue.Related!.Id]["url"], issue.Related.Url));

            // Errors first, then warnings, then notices; by check within each.
            Assert.Equal(url.Issues!.OrderBy(issue => issue.Severity).ThenBy(issue => issue.CheckId, StringComparer.Ordinal), url.Issues!);

            // The issues on other URLs that name this one, by check, most first.
            var naming = crawl.Reference.Issues.Where(entry => entry.Key != id).SelectMany(entry => entry.Value).Where(issue => issue.Related == id)
                .GroupBy(issue => issue.Issue).Select(group => new CheckCount(group.Key, group.Count()))
                .OrderByDescending(check => check.Count).ThenBy(check => check.CheckId, StringComparer.Ordinal).ToList();
            Assert.Equal(naming, url.NamedBy!.Checks);
            Assert.Equal(naming.Sum(check => check.Count), url.NamedBy.Total);
        }

        Assert.Contains(Read(crawl.Path, Id("/plants/gone")).NamedBy!.Checks, check => check.CheckId == "canonical-broken");
    }

    /// <summary>
    /// The checks naming a URL, most first: on a copy, as no URL of the synthetic site is named by checks in different
    /// numbers.
    /// </summary>
    [Fact]
    public void TheChecksNamingAUrlComeMostFirst()
    {
        var copy = crawl.Copy();
        var thin = Id("/plants/thin");
        Change(copy, $"""
            INSERT OR IGNORE INTO issues (issue, url_id, detail, related_url_id)
            SELECT 'links-to-redirects', url_id, NULL, {thin} FROM url_summary WHERE url_id <> {thin} ORDER BY url_id LIMIT 1;
            INSERT OR IGNORE INTO issues (issue, url_id, detail, related_url_id)
            SELECT 'hreflang-missing-return', url_id, NULL, {thin} FROM url_summary WHERE url_id <> {thin} ORDER BY url_id DESC LIMIT 6;
            """);
        var expected = TestCrawls.Rows(copy, $"SELECT issue, url_id FROM issues WHERE related_url_id = {thin}").Select(line => line.Split(" | "))
            .Where(parts => parts[1] != thin.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .GroupBy(parts => parts[0]).Select(group => new CheckCount(group.Key, group.Count()))
            .OrderByDescending(check => check.Count).ThenBy(check => check.CheckId, StringComparer.Ordinal).ToList();
        Assert.True(expected.Select(check => check.Count).Distinct().Count() > 1);
        Assert.Equal(expected, Read(copy, thin).NamedBy!.Checks);
    }

    /// <summary>Before any analysis: the crawl's own judgement, the engine's default limits, and no issues or link counts.</summary>
    [Fact]
    public void BeforeAnAnalysisTheCrawlJudgesAlone()
    {
        var copy = crawl.Copy();
        Change(copy, "DELETE FROM analysis");
        foreach (var (id, row) in crawl.Reference.Rows)
        {
            var url = Read(copy, id);
            Assert.Equal(new SummaryState(false, SummaryUnavailableReason.NotAnalyzed), url.Summary);
            Assert.Equal(IndexabilityJudge.Crawl, url.Indexability.JudgedBy);

            // What the analysis judged differently from the crawl is kept beside it, so the verdict is the same.
            Assert.True(Equals(row["indexable"], url.Indexability.Indexable), $"URL {id} ({row["url"]}): {url.Indexability.Indexable}");
            Assert.True(Equals(row["indexability"], url.Indexability.Reason is { } reason ? Name(reason) : null), $"URL {id} ({row["url"]}): {url.Indexability.Reason}");
            Assert.Null(url.Links);
            Assert.Null(url.Issues);
            Assert.Null(url.NamedBy);
            Assert.Equal(Read(crawl.Path, id).Indexability.Tests, url.Indexability.Tests);
        }

        var limits = Read(copy, Id("/p/7")).Limits;
        Assert.False(limits.FromAnalysis);
        Assert.Equal(new AnalysisSettings().TitleMaxLength, limits.TitleMax);
    }

    private UrlDetail Inspect(string path) => Read(crawl.Path, Id(path));

    private static UrlDetail Read(string path, int id) => UrlInspectorReader.Read(path, id) ?? throw new InvalidOperationException($"No URL {id}");

    private IndexabilityTest AssertDecidedBy(string path, IndexabilityReason reason, IndexabilityTestKind kind)
    {
        var verdict = Inspect(path).Indexability;
        Assert.False(verdict.Indexable);
        Assert.Equal(reason, verdict.Reason);
        var failed = verdict.Tests.First(test => test.Outcome == TestOutcome.Fail);
        Assert.Equal(kind, failed.Test);
        return failed;
    }

    private static IndexabilityTest Test(UrlDetail url, IndexabilityTestKind kind) => url.Indexability.Tests.Single(test => test.Test == kind);

    private int Id(string path) => crawl.Reference.Rows.Single(row => (string?)row.Value["url"] == Url(path)).Key;

    private string Url(string path) => crawl.Server.BaseUri.GetLeftPart(UriPartial.Authority) + path;

    private static string Name<TEnum>(TEnum value) where TEnum : struct, Enum => char.ToLowerInvariant(value.ToString()[0]) + value.ToString()[1..];
}
