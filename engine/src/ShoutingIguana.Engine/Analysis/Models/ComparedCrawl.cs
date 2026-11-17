using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>What <see cref="CrawlComparability"/> compares of one crawl: what it was asked to do, how it ended, and its analysis.</summary>
/// <param name="Options">The options it ran with, as stored (<see cref="Storage.CrawlDatabase.ReadOptions"/>).</param>
/// <param name="CrawlUpdatedAt">The crawl's stamp now (<see cref="CrawlFileSummary.UpdatedAt"/>), which says whether its analysis still stands for it.</param>
/// <param name="Coverage">What its score covers, or null when it has no score: its status then says whether it read all it set out to.</param>
public sealed record ComparedCrawl(CrawlOptions Options, CrawlStatus Status, DateTimeOffset CrawlUpdatedAt, AnalysisInfo Analysis, ScoreCoverage? Coverage);
