using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>When an analysis stops standing: the crawl changed, the engine judges differently, or the thresholds moved.</summary>
public sealed class AnalysisFreshnessTests
{
    private static readonly DateTimeOffset CRAWLED = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AnAnalysisOfTheCrawlAsItIsWithThisEngineAndTheseSettingsStands() =>
        Assert.Empty(AnalysisFreshness.Check(Current(), CRAWLED, new AnalysisSettings()));

    /// <summary>The crawl's own stamp is compared, not the clock: one that went back is caught, and the analysis's time doesn't matter.</summary>
    [Fact]
    public void AnyChangeToTheCrawlsStampMeansTheCrawlChanged()
    {
        Assert.Equal([OutOfDateReason.CrawlChanged], AnalysisFreshness.Check(Current(), CRAWLED.AddMilliseconds(1), new AnalysisSettings()));
        Assert.Equal([OutOfDateReason.CrawlChanged], AnalysisFreshness.Check(Current(), CRAWLED.AddHours(-1), new AnalysisSettings()));
        Assert.Empty(AnalysisFreshness.Check(Current() with { AnalyzedAt = CRAWLED.AddHours(-2) }, CRAWLED, new AnalysisSettings()));
    }

    /// <summary>An engine that didn't store the stamp leaves the times to compare.</summary>
    [Fact]
    public void WithoutTheStampTheCrawlChangedIfItWasStampedAfterTheAnalysis()
    {
        var old = Current() with { CrawlUpdatedAt = null };
        Assert.Contains(OutOfDateReason.CrawlChanged, AnalysisFreshness.Check(old, old.AnalyzedAt.AddSeconds(1), new AnalysisSettings()));
        Assert.DoesNotContain(OutOfDateReason.CrawlChanged, AnalysisFreshness.Check(old, old.AnalyzedAt, new AnalysisSettings()));
    }

    /// <summary>
    /// Version 12 measures titles and descriptions in pixels: an analysis 11 stored judged them in characters, so its
    /// counts, details and URL summary are out of date.
    /// </summary>
    [Fact]
    public void AnAnalysisThatCountedCharactersIsOutOfDate()
    {
        Assert.Equal([OutOfDateReason.EngineChanged], AnalysisFreshness.Check(Current() with { AnalysisVersion = 11 }, CRAWLED, new AnalysisSettings()));
        Assert.False((Current() with { AnalysisVersion = 11 }).HasUrlSummary);
    }

    /// <summary>
    /// Version 13 is the new checks': a Refresh header refreshes as the meta tag does, and canonicals to another site, from a
    /// later page to the first, and round a loop are reported as such, not as canonicalised. An analysis 12 stored is out of date.
    /// </summary>
    [Fact]
    public void AnAnalysisFromBeforeTheNewChecksIsOutOfDate()
    {
        Assert.Equal(16, AnalysisFreshness.VERSION);
        Assert.Equal([OutOfDateReason.EngineChanged], AnalysisFreshness.Check(Current() with { AnalysisVersion = 12 }, CRAWLED, new AnalysisSettings()));
    }

    /// <summary>
    /// Version 14 is the new sitemap checks' (batch 4): a sitemap entry on another scheme than its sitemap is reported on
    /// another host. An analysis 13 stored is out of date.
    /// </summary>
    [Fact]
    public void AnAnalysisFromBeforeTheSitemapChecksIsOutOfDate()
    {
        Assert.Equal([OutOfDateReason.EngineChanged], AnalysisFreshness.Check(Current() with { AnalysisVersion = 13 }, CRAWLED, new AnalysisSettings()));
    }

    /// <summary>
    /// Version 15 writes one note for each reason checks weren't run, with the checks totalled, where 14 wrote one for each
    /// collection of facts a page's parse lacked (five notes worded the same on a real crawl). An analysis 14 stored is out
    /// of date.
    /// </summary>
    [Fact]
    public void AnAnalysisFromBeforeOneNoteForEachReasonIsOutOfDate()
    {
        Assert.Equal([OutOfDateReason.EngineChanged], AnalysisFreshness.Check(Current() with { AnalysisVersion = 14 }, CRAWLED, new AnalysisSettings()));
    }

    /// <summary>
    /// Version 16 names each URL <c>protocol-relative-links</c> counts as the issue's targets, and doesn't flag an HTTPS page
    /// the home page's HSTS covers. An analysis 15 stored is out of date.
    /// </summary>
    [Fact]
    public void AnAnalysisFromBeforeProtocolRelativeTargetsIsOutOfDate()
    {
        Assert.Equal([OutOfDateReason.EngineChanged], AnalysisFreshness.Check(Current() with { AnalysisVersion = 15 }, CRAWLED, new AnalysisSettings()));
        Assert.Empty(AnalysisFreshness.Check(Current() with { AnalysisVersion = 16 }, CRAWLED, new AnalysisSettings()));
    }

    [Fact]
    public void AnotherAnalysisVersionOrOtherChecksMeanTheEngineChanged()
    {
        Assert.Equal([OutOfDateReason.EngineChanged], AnalysisFreshness.Check(Current() with { AnalysisVersion = AnalysisFreshness.VERSION - 1 }, CRAWLED, new AnalysisSettings()));
        Assert.Equal([OutOfDateReason.EngineChanged], AnalysisFreshness.Check(Current() with { ChecksFingerprint = "0123456789abcdef" }, CRAWLED, new AnalysisSettings()));

        // Version 7 called a site hidden from search whatever its home page said, passed blocked-by-robots on a crawl that
        // ignored robots.txt, and gave a redirecting canonical no final URL: what it stored isn't what this engine would.
        Assert.Equal([OutOfDateReason.EngineChanged], AnalysisFreshness.Check(Current() with { AnalysisVersion = 7 }, CRAWLED, new AnalysisSettings()));

        // Version 9 was written by two lines of work before they were merged: the check audit's and the evidence's. Either
        // stored what the other judged differently, so both are out of date.
        Assert.Equal([OutOfDateReason.EngineChanged], AnalysisFreshness.Check(Current() with { AnalysisVersion = 9 }, CRAWLED, new AnalysisSettings()));

        // Version 10 called a 429 a block (internal-blocked, then a warning) and counted the URLs a site kept rate limiting,
        // or that were on a private address, among those whose site had stopped answering.
        Assert.Equal([OutOfDateReason.EngineChanged], AnalysisFreshness.Check(Current() with { AnalysisVersion = 10 }, CRAWLED, new AnalysisSettings()));

        // A score worked out by another version of the formula, or none at all, isn't the score the crawl would get now.
        Assert.Equal([OutOfDateReason.EngineChanged], AnalysisFreshness.Check(Current() with { ScoreVersion = ScoreCalculator.VERSION + 1 }, CRAWLED, new AnalysisSettings()));
        Assert.Equal([OutOfDateReason.EngineChanged], AnalysisFreshness.Check(Current() with { ScoreVersion = null }, CRAWLED, new AnalysisSettings()));

        // What an engine from before the analysis stored any of this leaves: only the time and the settings.
        var beforeTheSummary = new AnalysisInfo(CRAWLED.AddMinutes(1), "2.0.0-alpha.1", 10, TimeSpan.FromSeconds(1)) { Settings = new AnalysisSettings() };
        Assert.Equal([OutOfDateReason.EngineChanged], AnalysisFreshness.Check(beforeTheSummary, CRAWLED, new AnalysisSettings()));
    }

    [Fact]
    public void OtherThresholdsMeanTheSettingsChanged()
    {
        var analysis = Current() with { Settings = new AnalysisSettings { TitleMaxPixels = 658 } };
        Assert.Equal([OutOfDateReason.SettingsChanged], AnalysisFreshness.Check(analysis, CRAWLED, new AnalysisSettings()));
        Assert.Empty(AnalysisFreshness.Check(analysis, CRAWLED, new AnalysisSettings { TitleMaxPixels = 658 }));

        // Settings that couldn't be read back are no settings: they match none.
        Assert.Equal([OutOfDateReason.SettingsChanged], AnalysisFreshness.Check(Current() with { Settings = null }, CRAWLED, new AnalysisSettings()));
    }

    [Fact]
    public void EveryReasonIsGivenAtOnce() => Assert.Equal(
        [OutOfDateReason.CrawlChanged, OutOfDateReason.EngineChanged, OutOfDateReason.SettingsChanged],
        AnalysisFreshness.Check(Current() with { AnalysisVersion = null, Settings = null }, CRAWLED.AddDays(1), new AnalysisSettings()));

    /// <summary>The checks' ids, categories and severities count; their order in the catalog and their wording don't.</summary>
    [Fact]
    public void TheChecksFingerprintFollowsWhatTheChecksAre()
    {
        var checks = IssueCatalog.All.ToList();
        var fingerprint = AnalysisFreshness.Fingerprint(checks);
        Assert.Equal(AnalysisFreshness.ChecksFingerprint, fingerprint);
        Assert.Matches("^[0-9a-f]{16}$", fingerprint);

        Assert.Equal(fingerprint, AnalysisFreshness.Fingerprint(Enumerable.Reverse(checks)));
        Assert.Equal(fingerprint, AnalysisFreshness.Fingerprint(checks.Select(check => check with
        {
            Title = "Renamed", Description = "", Fix = "", Why = "Different.", LearnMore = null
        })));

        // Nor does what the score makes of a check: that is the score version's to say.
        Assert.Equal(fingerprint, AnalysisFreshness.Fingerprint(checks.Select(check => check with { SiteWide = !check.SiteWide })));

        Assert.NotEqual(fingerprint, AnalysisFreshness.Fingerprint(checks.Skip(1)));
        Assert.NotEqual(fingerprint, AnalysisFreshness.Fingerprint([.. checks, checks[0] with { Id = "a-new-check" }]));
        Assert.NotEqual(fingerprint, AnalysisFreshness.Fingerprint(checks.Select((check, index) =>
            index == 0 ? check with { Severity = check.Severity == IssueSeverity.Error ? IssueSeverity.Notice : IssueSeverity.Error } : check)));
        Assert.NotEqual(fingerprint, AnalysisFreshness.Fingerprint(checks.Select((check, index) =>
            index == 0 ? check with { Category = check.Category == IssueCategory.Links ? IssueCategory.Html : IssueCategory.Links } : check)));
    }

    /// <summary>An analysis as this engine stores it, of the crawl stamped at <see cref="CRAWLED"/>.</summary>
    private static AnalysisInfo Current() => new(CRAWLED.AddMinutes(1), EngineInfo.Version, 10, TimeSpan.FromSeconds(1))
    {
        IsPartial = false,
        Settings = new AnalysisSettings(),
        CrawlUpdatedAt = CRAWLED,
        AnalysisVersion = AnalysisFreshness.VERSION,
        ChecksFingerprint = AnalysisFreshness.ChecksFingerprint,
        ScoreVersion = ScoreCalculator.VERSION
    };
}
