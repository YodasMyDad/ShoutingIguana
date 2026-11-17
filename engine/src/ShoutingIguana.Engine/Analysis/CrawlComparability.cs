using System.Text.Json;
using System.Text.Json.Nodes;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Analysis;

/// <summary>
/// Whether two crawls of a site can be compared fairly, and what differs if not (the fix loop): each difference named by
/// its code, so the UI can say it in its own words, and how far the comparison goes because of it. A crawl started at
/// another scheme or host can't have a URL matched, and analyses made by other rules can't have a row matched either
/// (every rule change would read as new or fixed), so both are compared by their counts only; a crawl that covered less
/// (another path, scope, limit or set of fetches, stopped early, or far smaller) is compared in part, the rows it didn't
/// read not compared; the client and the timeouts are only noted. Speed and whether HTML was stored never differ: they
/// change how long a crawl took, not what it found. Pure: it reads nothing.
/// </summary>
public static class CrawlComparability
{
    private static readonly HashSet<ComparisonDifferenceCode> PARTIAL =
    [
        ComparisonDifferenceCode.StartPath, ComparisonDifferenceCode.Scope, ComparisonDifferenceCode.Limits, ComparisonDifferenceCode.Fetching,
        ComparisonDifferenceCode.StoppedEarly, ComparisonDifferenceCode.Coverage
    ];

    /// <summary>How far a crawl (<paramref name="now"/>) can be compared with another crawl of the site (<paramref name="then"/>), and why.</summary>
    public static ComparabilityVerdict Judge(ComparedCrawl then, ComparedCrawl now)
    {
        var needsAnalysis = PreviousNeedsAnalysis(then, now);
        List<ComparisonDifference> differences = [];
        var (originThen, pathThen) = Split(then.Options.StartUrl);
        var (originNow, pathNow) = Split(now.Options.StartUrl);
        if (!string.Equals(originThen, originNow, StringComparison.OrdinalIgnoreCase))
        {
            differences.Add(new ComparisonDifference(ComparisonDifferenceCode.StartOrigin, Then: originThen, Now: originNow));
        }
        else if (!string.Equals(pathThen, pathNow, StringComparison.Ordinal))
        {
            differences.Add(new ComparisonDifference(ComparisonDifferenceCode.StartPath, Then: pathThen, Now: pathNow));
        }

        var (optionsThen, optionsNow) = (then.Options, now.Options);
        void Option(ComparisonDifferenceCode code, string field, Func<CrawlOptions, JsonNode?> value)
        {
            var (was, @is) = (value(optionsThen), value(optionsNow));
            if (!JsonNode.DeepEquals(was, @is))
            {
                differences.Add(new ComparisonDifference(code, field, Then: was, Now: @is));
            }
        }

        Option(ComparisonDifferenceCode.Scope, "stayInStartFolder", options => options.StayInStartFolder);
        Option(ComparisonDifferenceCode.Scope, "includeSubdomains", options => options.IncludeSubdomains);
        Option(ComparisonDifferenceCode.Scope, "followNofollow", options => options.FollowNofollow);
        Option(ComparisonDifferenceCode.Scope, "crawlCanonicals", options => options.CrawlCanonicals);
        Option(ComparisonDifferenceCode.Scope, "crawlHreflang", options => options.CrawlHreflang);
        Option(ComparisonDifferenceCode.Scope, "crawlPagination", options => options.CrawlPagination);

        // 0 is no limit, which the API says as none.
        Option(ComparisonDifferenceCode.Limits, "maxUrls", options => options.MaxUrls > 0 ? options.MaxUrls : null);
        Option(ComparisonDifferenceCode.Limits, "maxDepth", options => options.MaxDepth > 0 ? options.MaxDepth : null);

        Option(ComparisonDifferenceCode.Fetching, "checkResources", options => options.CheckResources);
        Option(ComparisonDifferenceCode.Fetching, "checkExternalLinks", options => options.CheckExternalLinks);
        Option(ComparisonDifferenceCode.Fetching, "discoverSitemaps", options => options.DiscoverSitemaps);
        Option(ComparisonDifferenceCode.Fetching, "extraSitemaps", options => Texts(options.ExtraSitemaps, StringComparer.Ordinal));
        Option(ComparisonDifferenceCode.Fetching, "respectRobotsTxt", options => options.RespectRobotsTxt);

        // Header values aren't stored, so only their names can differ, which HTTP reads whatever their case.
        Option(ComparisonDifferenceCode.Client, "userAgent", options => options.UserAgent == UserAgentPreset.Custom
            ? options.CustomUserAgent
            : JsonNamingPolicy.CamelCase.ConvertName(options.UserAgent.ToString()));
        Option(ComparisonDifferenceCode.Client, "acceptLanguage", options => options.AcceptLanguage);
        Option(ComparisonDifferenceCode.Client, "headers", options => Texts(options.ExtraHeaders.Keys.Select(name => name.ToLowerInvariant()), StringComparer.Ordinal));
        Option(ComparisonDifferenceCode.Client, "useCookies", options => options.UseCookies);

        Option(ComparisonDifferenceCode.Timeouts, "requestTimeoutSeconds", options => options.RequestTimeoutSeconds);
        Option(ComparisonDifferenceCode.Timeouts, "maxRetries", options => options.MaxRetries);

        var (completeThen, completeNow) = (IsComplete(then), IsComplete(now));
        if (!completeThen || !completeNow)
        {
            var side = completeThen ? ComparisonSide.Now : completeNow ? ComparisonSide.Then : ComparisonSide.Both;
            differences.Add(new ComparisonDifference(ComparisonDifferenceCode.StoppedEarly, Side: side));
        }

        var (pagesThen, pagesNow) = (then.Analysis.PagesAnalyzed, now.Analysis.PagesAnalyzed);
        if (Math.Min(pagesThen, pagesNow) * 2 < Math.Max(pagesThen, pagesNow))
        {
            differences.Add(new ComparisonDifference(ComparisonDifferenceCode.Coverage, Then: pagesThen, Now: pagesNow));
        }

        differences.AddRange(Rules(then.Analysis, now.Analysis));

        var comparable = differences.Any(difference => difference.Code is ComparisonDifferenceCode.StartOrigin) || needsAnalysis.Count > 0 ? Comparability.CountsOnly
            : differences.Any(difference => PARTIAL.Contains(difference.Code)) ? Comparability.Partial
            : Comparability.Full;
        return new ComparabilityVerdict(comparable, [.. differences.OrderBy(difference => difference.Code)], needsAnalysis);
    }

    /// <summary>
    /// Why the other crawl's analysis (<paramref name="then"/>) isn't like this crawl's: made by another analysis version,
    /// set of checks or version of the score (<see cref="OutOfDateReason.EngineChanged"/>), or with other settings
    /// (<see cref="OutOfDateReason.SettingsChanged"/>); or the other crawl was continued since its analysis
    /// (<see cref="OutOfDateReason.CrawlChanged"/>), so its issues aren't of what it read. Analysing it again mends each.
    /// </summary>
    public static IReadOnlyList<OutOfDateReason> PreviousNeedsAnalysis(ComparedCrawl then, ComparedCrawl now)
    {
        List<OutOfDateReason> reasons = [];
        var analysis = then.Analysis;
        if (analysis.CrawlUpdatedAt is { } seen ? seen != then.CrawlUpdatedAt : then.CrawlUpdatedAt > analysis.AnalyzedAt)
        {
            reasons.Add(OutOfDateReason.CrawlChanged);
        }

        if (EngineDiffers(analysis, now.Analysis).Any())
        {
            reasons.Add(OutOfDateReason.EngineChanged);
        }

        if (!SameSettings(analysis, now.Analysis))
        {
            reasons.Add(OutOfDateReason.SettingsChanged);
        }

        return reasons;
    }

    /// <summary>The analyses' rules that differ, each a <see cref="ComparisonDifferenceCode.Rules"/> difference.</summary>
    private static IEnumerable<ComparisonDifference> Rules(AnalysisInfo then, AnalysisInfo now)
    {
        foreach (var difference in EngineDiffers(then, now))
        {
            yield return difference;
        }

        if (!SameSettings(then, now))
        {
            yield return new ComparisonDifference(ComparisonDifferenceCode.Rules, "settings");
        }
    }

    private static IEnumerable<ComparisonDifference> EngineDiffers(AnalysisInfo then, AnalysisInfo now)
    {
        if (then.AnalysisVersion != now.AnalysisVersion)
        {
            yield return new ComparisonDifference(ComparisonDifferenceCode.Rules, "analysisVersion", Then: then.AnalysisVersion, Now: now.AnalysisVersion);
        }

        if (!string.Equals(then.ChecksFingerprint, now.ChecksFingerprint, StringComparison.Ordinal))
        {
            yield return new ComparisonDifference(ComparisonDifferenceCode.Rules, "checksFingerprint", Then: then.ChecksFingerprint, Now: now.ChecksFingerprint);
        }

        if (then.ScoreVersion != now.ScoreVersion)
        {
            yield return new ComparisonDifference(ComparisonDifferenceCode.Rules, "scoreVersion", Then: then.ScoreVersion, Now: now.ScoreVersion);
        }
    }

    /// <summary>Settings that can't be read (edited by hand, say) are none, so they differ from any, as freshness has them.</summary>
    private static bool SameSettings(AnalysisInfo then, AnalysisInfo now) =>
        then.Settings is { } was && now.Settings is { } @is && AnalysisFreshness.Canonical(was) == AnalysisFreshness.Canonical(@is);

    /// <summary>The crawl read all it set out to: as its score says, or without a score, it finished.</summary>
    private static bool IsComplete(ComparedCrawl crawl) => crawl.Coverage?.Complete ?? crawl.Status == CrawlStatus.Completed;

    /// <summary>A start URL's origin (scheme, host and port) and the rest; one that isn't a URL is all origin.</summary>
    private static (string Origin, string Path) Split(string startUrl) =>
        Uri.TryCreate(startUrl, UriKind.Absolute, out var uri) && uri.IsAbsoluteUri
            ? (uri.GetLeftPart(UriPartial.Authority), uri.PathAndQuery)
            : (startUrl, string.Empty);

    private static JsonArray Texts(IEnumerable<string> values, StringComparer comparer) =>
        new(values.Distinct(comparer).Order(comparer).Select(value => (JsonNode?)value).ToArray());
}
