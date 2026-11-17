using System.Text.Json.Nodes;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// Whether two crawls of a site can be compared fairly: each difference named by its code, with what it was then and is
/// now, and how far it lets the comparison go.
/// </summary>
public sealed class CrawlComparabilityTests
{
    private static readonly DateTimeOffset STAMP = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly CrawlOptions OPTIONS = new() { StartUrl = "https://www.example.com/" };

    /// <summary>
    /// The options fingerprint a list of crawls keeps: the same for options that differ only in what is noted (the client,
    /// the timeouts) or never differs (speed, stored HTML), and another for any option that makes a comparison partial.
    /// </summary>
    [Fact]
    public void TheOptionsFingerprintChangesWithWhatMakesAComparisonPartialOnly()
    {
        var fingerprint = CrawlComparability.OptionsFingerprint(OPTIONS);
        Assert.Equal(fingerprint, CrawlComparability.OptionsFingerprint(OPTIONS with { }));
        Assert.Equal(fingerprint, CrawlComparability.OptionsFingerprint(OPTIONS with
        {
            Concurrency = 32, StoreHtml = true, UserAgent = UserAgentPreset.Custom, CustomUserAgent = "Other/1.0", RequestTimeoutSeconds = 99, MaxRetries = 0,
            ExtraHeaders = new Dictionary<string, string> { ["X-Test"] = "1" }
        }));

        // The start URL isn't in it: a summary has its own.
        Assert.Equal(fingerprint, CrawlComparability.OptionsFingerprint(OPTIONS with { StartUrl = "https://www.example.com/blog/" }));
        CrawlOptions[] partial =
        [
            OPTIONS with { StayInStartFolder = !OPTIONS.StayInStartFolder }, OPTIONS with { IncludeSubdomains = !OPTIONS.IncludeSubdomains },
            OPTIONS with { FollowNofollow = !OPTIONS.FollowNofollow }, OPTIONS with { CrawlCanonicals = !OPTIONS.CrawlCanonicals },
            OPTIONS with { CrawlHreflang = !OPTIONS.CrawlHreflang }, OPTIONS with { CrawlPagination = !OPTIONS.CrawlPagination },
            OPTIONS with { MaxUrls = 5_000 }, OPTIONS with { MaxDepth = 3 }, OPTIONS with { CheckResources = !OPTIONS.CheckResources },
            OPTIONS with { CheckExternalLinks = !OPTIONS.CheckExternalLinks }, OPTIONS with { DiscoverSitemaps = !OPTIONS.DiscoverSitemaps },
            OPTIONS with { ExtraSitemaps = ["https://www.example.com/more.xml"] }, OPTIONS with { RespectRobotsTxt = !OPTIONS.RespectRobotsTxt }
        ];
        Assert.All(partial, options =>
        {
            Assert.Equal(Comparability.Partial, CrawlComparability.Judge(Crawl(), Crawl(options)).Comparable);
            Assert.NotEqual(fingerprint, CrawlComparability.OptionsFingerprint(options));
        });
        Assert.Equal(partial.Length, partial.Select(CrawlComparability.OptionsFingerprint).Distinct().Count());
    }

    [Fact]
    public void TwoCrawlsAlikeAreComparedFully()
    {
        var verdict = CrawlComparability.Judge(Crawl(), Crawl());
        Assert.Equal(Comparability.Full, verdict.Comparable);
        Assert.Empty(verdict.Differences);
        Assert.Empty(verdict.PreviousNeedsAnalysis);
    }

    /// <summary>Speed and whether HTML was stored change how long a crawl took, not what it found.</summary>
    [Fact]
    public void SpeedAndStoredHtmlNeverDiffer()
    {
        var faster = OPTIONS with { Concurrency = 32, ExternalConcurrency = 16, MaxRequestsPerSecond = 3, RespectCrawlDelay = true, StoreHtml = true };
        Assert.Empty(CrawlComparability.Judge(Crawl(), Crawl(faster)).Differences);
    }

    /// <summary>Another scheme or host: no URL can be matched, so counts only. Another path: in part.</summary>
    [Fact]
    public void TheStartUrlsOriginMakesItCountsOnlyAndItsPathPartial()
    {
        var https = CrawlComparability.Judge(Crawl(OPTIONS with { StartUrl = "http://www.example.com/" }), Crawl());
        Assert.Equal(Comparability.CountsOnly, https.Comparable);
        Assert.Equal([Difference(ComparisonDifferenceCode.StartOrigin, null, "http://www.example.com", "https://www.example.com")], Plain(https));

        var bare = CrawlComparability.Judge(Crawl(OPTIONS with { StartUrl = "https://example.com/" }), Crawl());
        Assert.Equal((Comparability.CountsOnly, ComparisonDifferenceCode.StartOrigin), (bare.Comparable, Assert.Single(bare.Differences).Code));

        var blog = CrawlComparability.Judge(Crawl(OPTIONS with { StartUrl = "https://www.example.com/blog/" }), Crawl());
        Assert.Equal(Comparability.Partial, blog.Comparable);
        Assert.Equal([Difference(ComparisonDifferenceCode.StartPath, null, "/blog/", "/")], Plain(blog));
    }

    /// <summary>What the crawl followed, its limits and what it fetched make it partial; the client and the timeouts are only noted.</summary>
    [Fact]
    public void EachOptionThatMattersIsNamed()
    {
        var then = OPTIONS with
        {
            StayInStartFolder = false, IncludeSubdomains = true, FollowNofollow = true, CrawlCanonicals = false, CrawlHreflang = true, CrawlPagination = true,
            MaxUrls = 5_000, MaxDepth = 4,
            CheckResources = false, CheckExternalLinks = false, DiscoverSitemaps = false, ExtraSitemaps = ["https://www.example.com/b.xml", "https://www.example.com/a.xml"],
            RespectRobotsTxt = false,
            UserAgent = UserAgentPreset.Custom, CustomUserAgent = "Bot/1.0", AcceptLanguage = "fr", ExtraHeaders = new Dictionary<string, string> { ["X-Key"] = "" },
            UseCookies = false,
            RequestTimeoutSeconds = 5, MaxRetries = 0
        };
        var verdict = CrawlComparability.Judge(Crawl(then), Crawl());
        Assert.Equal(Comparability.Partial, verdict.Comparable);
        Assert.Equal(
        [
            Difference(ComparisonDifferenceCode.Scope, "stayInStartFolder", false, true),
            Difference(ComparisonDifferenceCode.Scope, "includeSubdomains", true, false),
            Difference(ComparisonDifferenceCode.Scope, "followNofollow", true, false),
            Difference(ComparisonDifferenceCode.Scope, "crawlCanonicals", false, true),
            Difference(ComparisonDifferenceCode.Scope, "crawlHreflang", true, false),
            Difference(ComparisonDifferenceCode.Scope, "crawlPagination", true, false),
            Difference(ComparisonDifferenceCode.Limits, "maxUrls", 5_000, null),
            Difference(ComparisonDifferenceCode.Limits, "maxDepth", 4, null),
            Difference(ComparisonDifferenceCode.Fetching, "checkResources", false, true),
            Difference(ComparisonDifferenceCode.Fetching, "checkExternalLinks", false, true),
            Difference(ComparisonDifferenceCode.Fetching, "discoverSitemaps", false, true),
            Difference(ComparisonDifferenceCode.Fetching, "extraSitemaps", new JsonArray("https://www.example.com/a.xml", "https://www.example.com/b.xml"), new JsonArray()),
            Difference(ComparisonDifferenceCode.Fetching, "respectRobotsTxt", false, true),
            Difference(ComparisonDifferenceCode.Client, "userAgent", "Bot/1.0", "shoutingIguana"),
            Difference(ComparisonDifferenceCode.Client, "acceptLanguage", "fr", "en-US,en;q=0.9"),
            Difference(ComparisonDifferenceCode.Client, "headers", new JsonArray("x-key"), new JsonArray()),
            Difference(ComparisonDifferenceCode.Client, "useCookies", false, true),
            Difference(ComparisonDifferenceCode.Timeouts, "requestTimeoutSeconds", 5, 20),
            Difference(ComparisonDifferenceCode.Timeouts, "maxRetries", 0, 2)
        ], Plain(verdict));

        // Only noted: the comparison is full.
        var client = CrawlComparability.Judge(Crawl(OPTIONS with { UserAgent = UserAgentPreset.GooglebotSmartphone, MaxRetries = 5 }), Crawl());
        Assert.Equal(Comparability.Full, client.Comparable);
        Assert.Equal([Difference(ComparisonDifferenceCode.Client, "userAgent", "googlebotSmartphone", "shoutingIguana"), Difference(ComparisonDifferenceCode.Timeouts, "maxRetries", 5, 2)],
            Plain(client));

        // Header names compare as HTTP does, whatever their case; their values aren't stored.
        var headers = new Dictionary<string, string> { ["X-Key"] = "" };
        Assert.Empty(CrawlComparability.Judge(Crawl(OPTIONS with { ExtraHeaders = headers }), Crawl(OPTIONS with { ExtraHeaders = new Dictionary<string, string> { ["x-key"] = "" } })).Differences);
    }

    /// <summary>A crawl that didn't read all it set out to (as its score says, or without one, as its status says) makes it partial, saying which.</summary>
    [Fact]
    public void ACrawlStoppedEarlyIsNamedBySide()
    {
        var stopped = Crawl(coverage: Coverage(complete: false));
        Assert.Equal([Difference(ComparisonDifferenceCode.StoppedEarly, side: ComparisonSide.Then)], Plain(CrawlComparability.Judge(stopped, Crawl())));
        Assert.Equal([Difference(ComparisonDifferenceCode.StoppedEarly, side: ComparisonSide.Now)], Plain(CrawlComparability.Judge(Crawl(), stopped)));
        Assert.Equal([Difference(ComparisonDifferenceCode.StoppedEarly, side: ComparisonSide.Both)], Plain(CrawlComparability.Judge(stopped, stopped)));
        Assert.Equal(Comparability.Partial, CrawlComparability.Judge(stopped, Crawl()).Comparable);

        // No score (no page read): its status says.
        Assert.Equal(ComparisonSide.Now, Assert.Single(CrawlComparability.Judge(Crawl(), Crawl(status: CrawlStatus.Failed, scored: false)).Differences).Side);
        Assert.Empty(CrawlComparability.Judge(Crawl(), Crawl(scored: false)).Differences);
    }

    /// <summary>One read fewer than half the pages the other did: most of its rows read as not checked.</summary>
    [Fact]
    public void VeryDifferentSizesAreNamed()
    {
        Assert.Equal([Difference(ComparisonDifferenceCode.Coverage, null, 100L, 49L)], Plain(CrawlComparability.Judge(Crawl(pages: 100), Crawl(pages: 49))));
        Assert.Empty(CrawlComparability.Judge(Crawl(pages: 100), Crawl(pages: 50)).Differences);
        Assert.Equal(Comparability.Partial, CrawlComparability.Judge(Crawl(pages: 49), Crawl(pages: 100)).Comparable);
    }

    /// <summary>
    /// Analyses made by other rules (an analysis version, checks, a score version, settings) are counts only, and the
    /// previous one needs analysing again; one whose crawl was continued since needs it too.
    /// </summary>
    [Fact]
    public void AnalysesByOtherRulesAreCountsOnlyUntilThePreviousIsAnalysedAgain()
    {
        var older = Crawl(analysis: Analysis() with { AnalysisVersion = AnalysisFreshness.VERSION - 1, ScoreVersion = ScoreCalculator.VERSION - 1, ChecksFingerprint = "0123456789abcdef" });
        var verdict = CrawlComparability.Judge(older, Crawl());
        Assert.Equal(Comparability.CountsOnly, verdict.Comparable);
        Assert.Equal([OutOfDateReason.EngineChanged], verdict.PreviousNeedsAnalysis);
        Assert.Equal(
        [
            Difference(ComparisonDifferenceCode.Rules, "analysisVersion", AnalysisFreshness.VERSION - 1, AnalysisFreshness.VERSION),
            Difference(ComparisonDifferenceCode.Rules, "checksFingerprint", "0123456789abcdef", AnalysisFreshness.ChecksFingerprint),
            Difference(ComparisonDifferenceCode.Rules, "scoreVersion", ScoreCalculator.VERSION - 1, ScoreCalculator.VERSION)
        ], Plain(verdict));

        var settings = CrawlComparability.Judge(Crawl(analysis: Analysis() with { Settings = new AnalysisSettings { MinWordCount = 10 } }), Crawl());
        Assert.Equal(Comparability.CountsOnly, settings.Comparable);
        Assert.Equal([OutOfDateReason.SettingsChanged], settings.PreviousNeedsAnalysis);
        Assert.Equal([Difference(ComparisonDifferenceCode.Rules, "settings")], Plain(settings));

        // Continued since its analysis: its issues aren't of what it read, so it needs analysing, though its rules are this one's.
        var continued = CrawlComparability.Judge(Crawl(updatedAt: STAMP.AddMinutes(5)), Crawl());
        Assert.Equal(Comparability.CountsOnly, continued.Comparable);
        Assert.Equal([OutOfDateReason.CrawlChanged], continued.PreviousNeedsAnalysis);
        Assert.Empty(continued.Differences);

        // This crawl's own analysis being older is a difference of rules too: the previous one isn't like it.
        Assert.Equal([OutOfDateReason.EngineChanged], CrawlComparability.Judge(Crawl(), older).PreviousNeedsAnalysis);
    }

    /// <param name="scored">Whether the analysis has a score, whose coverage is <paramref name="coverage"/> (complete when not given).</param>
    private static ComparedCrawl Crawl(CrawlOptions? options = null, CrawlStatus status = CrawlStatus.Completed, DateTimeOffset? updatedAt = null, AnalysisInfo? analysis = null,
        long pages = 100, ScoreCoverage? coverage = null, bool scored = true) =>
        new(options ?? OPTIONS, status, updatedAt ?? STAMP, (analysis ?? Analysis()) with { PagesAnalyzed = pages }, scored ? coverage ?? Coverage(complete: true) : null);

    private static AnalysisInfo Analysis() => new(STAMP.AddMinutes(1), "2.0.0", 100, TimeSpan.FromSeconds(1))
    {
        Settings = new AnalysisSettings(),
        CrawlUpdatedAt = STAMP,
        AnalysisVersion = AnalysisFreshness.VERSION,
        ChecksFingerprint = AnalysisFreshness.ChecksFingerprint,
        ScoreVersion = ScoreCalculator.VERSION
    };

    private static ScoreCoverage Coverage(bool complete) => new(100, 120, 80, 0, complete, complete ? null : ScoreIncompleteReason.Stopped, 0);

    private static string Difference(ComparisonDifferenceCode code, string? field = null, JsonNode? then = null, JsonNode? now = null, ComparisonSide? side = null) =>
        $"{code} {field} {side} {then?.ToJsonString() ?? "null"} {now?.ToJsonString() ?? "null"}";

    private static List<string> Plain(ComparabilityVerdict verdict) =>
        [.. verdict.Differences.Select(difference => Difference(difference.Code, difference.Field, difference.Then, difference.Now, difference.Side))];
}
