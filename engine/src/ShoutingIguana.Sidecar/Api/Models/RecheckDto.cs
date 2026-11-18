using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>One recheck of a crawl, as its file keeps it (the fix loop): times in UTC, its result once settled.</summary>
/// <param name="CrawlingMs">Its run's own time spent crawling; null when that isn't kept.</param>
/// <param name="Result">Null until the analysis after it settles it; never worked out again after.</param>
internal sealed record RecheckDto(
    int Id,
    RecheckStatus Status,
    DateTime StartedAt,
    DateTime? EndedAt,
    DateTime? SettledAt,
    long? CrawlingMs,
    string? CheckId,
    IssueGroupKey? Group,
    bool UrlList,
    IReadOnlyList<RecheckKind> Kinds,
    int Judged,
    int Fetched,
    long? CountBefore,
    RecheckResult? Result,
    RecheckAnswerCounts? Answers)
{
    public static RecheckDto From(RecheckRecord recheck) => new(recheck.Id, recheck.Status, recheck.StartedAt.UtcDateTime, recheck.EndedAt?.UtcDateTime,
        recheck.SettledAt?.UtcDateTime, recheck.CrawlingMs, recheck.CheckId, recheck.Group, recheck.UrlList, recheck.Kinds, recheck.Judged, recheck.Fetched,
        recheck.CountBefore, recheck.Result, recheck.Answers);
}
