using System.Security.Cryptography;
using System.Text;
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

    /// <summary>The options compared, each with its code, its name in the API and its value as the comparison reads it.</summary>
    private static readonly (ComparisonDifferenceCode Code, string Field, Func<CrawlOptions, JsonNode?> Value)[] OPTIONS =
    [
        (ComparisonDifferenceCode.Scope, "stayInStartFolder", options => options.StayInStartFolder),
        (ComparisonDifferenceCode.Scope, "includeSubdomains", options => options.IncludeSubdomains),
        (ComparisonDifferenceCode.Scope, "followNofollow", options => options.FollowNofollow),
        (ComparisonDifferenceCode.Scope, "crawlCanonicals", options => options.CrawlCanonicals),
        (ComparisonDifferenceCode.Scope, "crawlHreflang", options => options.CrawlHreflang),
        (ComparisonDifferenceCode.Scope, "crawlPagination", options => options.CrawlPagination),

        // 0 is no limit, which the API says as none.
        (ComparisonDifferenceCode.Limits, "maxUrls", options => options.MaxUrls > 0 ? options.MaxUrls : null),
        (ComparisonDifferenceCode.Limits, "maxDepth", options => options.MaxDepth > 0 ? options.MaxDepth : null),

        (ComparisonDifferenceCode.Fetching, "checkResources", options => options.CheckResources),
        (ComparisonDifferenceCode.Fetching, "checkExternalLinks", options => options.CheckExternalLinks),
        (ComparisonDifferenceCode.Fetching, "discoverSitemaps", options => options.DiscoverSitemaps),
        (ComparisonDifferenceCode.Fetching, "extraSitemaps", options => Texts(options.ExtraSitemaps, StringComparer.Ordinal)),
        (ComparisonDifferenceCode.Fetching, "respectRobotsTxt", options => options.RespectRobotsTxt),
        (ComparisonDifferenceCode.Fetching, "browserFallback", options => options.BrowserFallback),
        (ComparisonDifferenceCode.Fetching, "httpLogins", options => Texts(options.HttpLogins.Select(login => login.Host + "|" + login.Username), StringComparer.OrdinalIgnoreCase)),
        (ComparisonDifferenceCode.Fetching, "cookies", options => Texts(options.Cookies.Select(cookie => cookie.Domain + "|" + cookie.Name), StringComparer.OrdinalIgnoreCase)),
        (ComparisonDifferenceCode.Fetching, "formLogin", options => options.FormLogin?.Url),

        // Header values aren't stored, so only their names can differ, which HTTP reads whatever their case.
        (ComparisonDifferenceCode.Client, "userAgent", options => options.UserAgent == UserAgentPreset.Custom
            ? options.CustomUserAgent
            : JsonNamingPolicy.CamelCase.ConvertName(options.UserAgent.ToString())),
        (ComparisonDifferenceCode.Client, "acceptLanguage", options => options.AcceptLanguage),
        (ComparisonDifferenceCode.Client, "headers", options => Texts(options.ExtraHeaders.Keys.Select(name => name.ToLowerInvariant()), StringComparer.Ordinal)),
        (ComparisonDifferenceCode.Client, "useCookies", options => options.UseCookies),

        (ComparisonDifferenceCode.Timeouts, "requestTimeoutSeconds", options => options.RequestTimeoutSeconds),
        (ComparisonDifferenceCode.Timeouts, "maxRetries", options => options.MaxRetries)
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

        foreach (var (code, field, value) in OPTIONS)
        {
            var (was, @is) = (value(then.Options), value(now.Options));
            if (!JsonNode.DeepEquals(was, @is))
            {
                differences.Add(new ComparisonDifference(code, field, Then: was, Now: @is));
            }
        }

        var (completeThen, completeNow) = (IsComplete(then), IsComplete(now));
        if (!completeThen || !completeNow)
        {
            var side = completeThen ? ComparisonSide.Now : completeNow ? ComparisonSide.Then : ComparisonSide.Both;
            differences.Add(new ComparisonDifference(ComparisonDifferenceCode.StoppedEarly, Side: side));
        }

        var (pagesThen, pagesNow) = (then.Analysis.PagesAnalyzed, now.Analysis.PagesAnalyzed);
        if (CoverageDiffers(pagesThen, pagesNow))
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
    public static IReadOnlyList<OutOfDateReason> PreviousNeedsAnalysis(ComparedCrawl then, ComparedCrawl now) =>
        PreviousNeedsAnalysis(then.Analysis, then.CrawlUpdatedAt, now.Analysis);

    /// <summary>
    /// The options that make a comparison partial when they differ (the scope, the limits and what is fetched), as the
    /// comparison reads them, in one text: two crawls whose fingerprints are the same differ in none of them. A list of
    /// crawls keeps it with each crawl's summary, so it can say how far two crawls compare without opening either.
    /// </summary>
    public static string OptionsFingerprint(CrawlOptions options)
    {
        JsonArray values = [.. OPTIONS.Where(option => PARTIAL.Contains(option.Code)).Select(option => (JsonNode?)new JsonArray(option.Field, option.Value(options)))];
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(values.ToJsonString())), 0, 16);
    }

    /// <summary>
    /// A crawl (<paramref name="now"/>) compared with another crawl of the site (<paramref name="then"/>) at the check level,
    /// from their summaries alone: how far they compare, what the other needs to be like for like, the score's change, and
    /// each check's state by its counts. Agrees with <see cref="Storage.CrawlComparisonReader.ReadSummary"/> on all of them
    /// but a check at 0 now, which only the rows can show fixed: here, as there without its rows, it is fewer when this
    /// crawl may not have read what the other flagged (it stopped early, or read under half the pages), else fixed. Null
    /// when either was never analysed.
    /// </summary>
    public static SummaryComparison? FromSummaries(CrawlFileSummary then, CrawlFileSummary now)
    {
        if (then is not { Analysis: { } analysisThen, CheckCounts: { } countsThen } || now is not { Analysis: { } analysisNow, CheckCounts: { } countsNow })
        {
            return null;
        }

        var needsAnalysis = PreviousNeedsAnalysis(analysisThen, then.UpdatedAt, analysisNow);
        var (originThen, pathThen) = Split(then.StartUrl);
        var (originNow, pathNow) = Split(now.StartUrl);
        var (completeThen, completeNow) = (IsComplete(then.Status, analysisThen.Score), IsComplete(now.Status, analysisNow.Score));
        var coverage = CoverageDiffers(analysisThen.PagesAnalyzed, analysisNow.PagesAnalyzed);
        var comparable = !string.Equals(originThen, originNow, StringComparison.OrdinalIgnoreCase) || needsAnalysis.Count > 0 ? Comparability.CountsOnly
            : !string.Equals(pathThen, pathNow, StringComparison.Ordinal) || then.OptionsFingerprint is null || then.OptionsFingerprint != now.OptionsFingerprint ||
              !completeThen || !completeNow || coverage ? Comparability.Partial
            : Comparability.Full;

        var unsure = !completeNow || coverage && analysisNow.PagesAnalyzed < analysisThen.PagesAnalyzed;
        var ran = countsThen.Keys.Concat(countsNow.Keys).ToHashSet(StringComparer.Ordinal);
        List<CheckCountChange> checks =
        [
            .. FixFirstOrder.Of(countsNow)
                .Concat(IssueCatalog.All.Select(check => check.Id))
                .Concat(ran.Order(StringComparer.Ordinal))
                .Where(ran.Contains)
                .Distinct(StringComparer.Ordinal)
                .Select(checkId =>
                {
                    long? countThen = countsThen.TryGetValue(checkId, out var was) ? was : null;
                    long? countNow = countsNow.TryGetValue(checkId, out var @is) ? @is : null;
                    return new CheckCountChange(checkId, Storage.CrawlComparisonReader.StateOf(countThen, countNow, rows: null, unsure), countThen, countNow);
                })
        ];

        var scoreChange = analysisThen.Score is { } scoreThen && analysisNow.Score is { } scoreNow && analysisThen.ScoreVersion is { } version &&
                          version == analysisNow.ScoreVersion
            ? scoreNow.Value - scoreThen.Value
            : (int?)null;
        return new SummaryComparison(comparable, needsAnalysis, scoreChange, !completeThen || !completeNow, checks);
    }

    private static IReadOnlyList<OutOfDateReason> PreviousNeedsAnalysis(AnalysisInfo analysis, DateTimeOffset crawlUpdatedAt, AnalysisInfo now)
    {
        List<OutOfDateReason> reasons = [];
        if (analysis.CrawlUpdatedAt is { } seen ? seen != crawlUpdatedAt : crawlUpdatedAt > analysis.AnalyzedAt)
        {
            reasons.Add(OutOfDateReason.CrawlChanged);
        }

        if (EngineDiffers(analysis, now).Any())
        {
            reasons.Add(OutOfDateReason.EngineChanged);
        }

        if (!SameSettings(analysis, now))
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

    /// <summary>As <see cref="IsComplete(ComparedCrawl)"/>, from a score's headline: its partial is its coverage's incomplete.</summary>
    private static bool IsComplete(CrawlStatus status, SiteScoreSummary? score) => score is { } scored ? !scored.Partial : status == CrawlStatus.Completed;

    /// <summary>One read fewer than half the pages the other did.</summary>
    private static bool CoverageDiffers(long pagesThen, long pagesNow) => Math.Min(pagesThen, pagesNow) * 2 < Math.Max(pagesThen, pagesNow);

    /// <summary>A start URL's origin (scheme, host and port) and the rest; one that isn't a URL is all origin.</summary>
    private static (string Origin, string Path) Split(string startUrl) =>
        Uri.TryCreate(startUrl, UriKind.Absolute, out var uri) && uri.IsAbsoluteUri
            ? (uri.GetLeftPart(UriPartial.Authority), uri.PathAndQuery)
            : (startUrl, string.Empty);

    private static JsonArray Texts(IEnumerable<string> values, StringComparer comparer) =>
        new(values.Distinct(comparer).Order(comparer).Select(value => (JsonNode?)value).ToArray());
}
