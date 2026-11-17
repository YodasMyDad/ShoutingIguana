using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Analysis;

/// <summary>
/// Whether a crawl judged a URL for a check: the fix loop's line between "fixed" and "not checked". A URL a check flagged
/// once and doesn't flag now is fixed only if the crawl read it in a way the check can judge; else it wasn't checked, and
/// is never counted as fixed. The comparison of two crawls draws it for each row, and a recheck for each URL it judges.
/// </summary>
public static class CheckJudging
{
    private static readonly HashSet<string> RESPONSE_CODES = new(StringComparer.Ordinal)
    {
        IssueCatalog.INTERNAL_CLIENT_ERROR.Id, IssueCatalog.INTERNAL_SERVER_ERROR.Id, IssueCatalog.INTERNAL_UNREACHABLE.Id,
        IssueCatalog.INTERNAL_BLOCKED.Id
    };

    /// <summary>
    /// SQL over <c>urls u LEFT JOIN responses r ON r.url_id = u.id</c>: the URL's <see cref="UrlReading"/>, as its number.
    /// A URL a recheck let go is no longer found.
    /// </summary>
    public static readonly string READING = $"""
        CASE
            WHEN u.state = {(int)UrlState.Skipped} AND u.skip_reason = {(int)SkipReason.NoLongerLinked} THEN {(int)UrlReading.NotFound}
            WHEN r.url_id IS NULL OR {AnalysisReader.NOT_CHECKED} OR r.outcome = {(int)FetchOutcome.RobotsDisallowed} THEN {(int)UrlReading.NotRead}
            WHEN {CrawlSchema.PAGE} THEN {(int)UrlReading.Page}
            WHEN r.outcome IN ({(int)FetchOutcome.Success}, {(int)FetchOutcome.Redirect}, {(int)FetchOutcome.ClientError}, {(int)FetchOutcome.ServerError}) THEN {(int)UrlReading.Answered}
            ELSE {(int)UrlReading.Failed}
        END
        """;

    /// <summary>What <paramref name="check"/> needs of a URL to judge it.</summary>
    public static UrlJudging Of(IssueDefinition check) =>
        RESPONSE_CODES.Contains(check.Id) ? UrlJudging.Responses
        : check.Subject == IssueSubject.Page ? UrlJudging.Pages
        : UrlJudging.Urls;

    /// <summary>Whether a crawl that made <paramref name="reading"/> of a URL judged it for a check that needs <paramref name="judging"/>.</summary>
    public static bool Judges(UrlJudging judging, UrlReading reading) => judging switch
    {
        UrlJudging.Responses => reading is UrlReading.Page or UrlReading.Answered or UrlReading.Failed,
        UrlJudging.Urls => reading is UrlReading.Page or UrlReading.Answered,
        _ => reading is UrlReading.Page
    };
}
