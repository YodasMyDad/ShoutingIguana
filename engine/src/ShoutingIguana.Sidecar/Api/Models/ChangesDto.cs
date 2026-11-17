using System.Text.Json.Nodes;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A crawl compared with another crawl of the site (<c>GET /api/crawls/{id}/changes</c>, the fix loop).</summary>
/// <param name="Differences">Each <c>{ code, field?, side?, then?, now? }</c>: a field, side or value only where the difference has one.</param>
/// <param name="Score">The score then and now, with every check that took points on either side; null when either has none.</param>
/// <param name="Checks">Every check that ran on either side, in this crawl's fix-first order and then catalog order.</param>
internal sealed record ChangesDto(
    string CrawlId,
    ComparedCrawlDto With,
    Comparability Comparable,
    JsonArray Differences,
    IReadOnlyList<OutOfDateReason> PreviousNeedsAnalysis,
    ScoreChange? Score,
    ComparisonTotalsDto Totals,
    IReadOnlyList<CheckChangeDto> Checks);
