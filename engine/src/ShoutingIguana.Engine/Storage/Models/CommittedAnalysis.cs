using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>What <see cref="IssueWriter.Commit"/> stored beside the issues, for the analysis to hand back.</summary>
/// <param name="Counts">Flagged URL counts per issue id.</param>
/// <param name="CrawledUrlCount">The URLs crawled, as the analysis read them.</param>
/// <param name="Score">The score worked out from those counts, or null when no page was read.</param>
internal sealed record CommittedAnalysis(Dictionary<string, long> Counts, long CrawledUrlCount, SiteScore? Score);
