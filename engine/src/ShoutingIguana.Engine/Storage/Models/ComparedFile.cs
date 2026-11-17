using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One crawl file of a comparison as its snapshot has it: its analysis, options and status, and each check's severity as it ran.</summary>
/// <param name="Severities">The severity each check that ran was stored with (<c>issue_types.severity</c>).</param>
internal sealed record ComparedFile(AnalysisResults Results, CrawlOptions Options, CrawlStatus Status, IReadOnlyDictionary<string, IssueSeverity> Severities)
{
    public bool Ran(string checkId) => Results.Counts.ContainsKey(checkId);

    public long? Count(string checkId) => Results.Counts.TryGetValue(checkId, out var count) ? count : null;

    public ComparedCrawl Compared => new(Options, Status, Results.CrawlUpdatedAt, Results.Analysis, Results.Score?.Covers);
}
