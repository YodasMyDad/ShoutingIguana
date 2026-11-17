using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>The crawl a library item is compared with by default (the fix loop): the newest earlier crawl of its site that can be.</summary>
/// <param name="Score">Its score in brief, or null when it has none.</param>
/// <param name="Comparable">How far the two compare, as <c>/changes</c> says it.</param>
/// <param name="NeedsAnalysis">The two analyses were made by other rules (or the previous crawl changed since its own), so the comparison is counts only until it is analysed again.</param>
internal sealed record PreviousCrawlDto(string Id, DateTime? StartedAt, SiteScoreSummary? Score, Comparability Comparable, bool NeedsAnalysis);
