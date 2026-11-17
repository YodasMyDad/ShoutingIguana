using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One recheck of a crawl, as its file keeps it (<see cref="RecheckReader"/>).</summary>
/// <param name="Id">Its number in the file, from 1.</param>
/// <param name="CrawlingMs">The time its run spent crawling, or null when that isn't kept.</param>
/// <param name="Judged">The URLs it gives a result for.</param>
/// <param name="Fetched">The requests it made.</param>
/// <param name="CountBefore">The check's count before it, or null without a check.</param>
/// <param name="Result">What it found, once the analysis after it settled it; null until then.</param>
public sealed record RecheckRecord(
    int Id,
    RecheckStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    DateTimeOffset? SettledAt,
    long? CrawlingMs,
    string? CheckId,
    IssueGroupKey? Group,
    bool UrlList,
    IReadOnlyList<RecheckKind> Kinds,
    int Judged,
    int Fetched,
    long? CountBefore,
    RecheckResult? Result);
