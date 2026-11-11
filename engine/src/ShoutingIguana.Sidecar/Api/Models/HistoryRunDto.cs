using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>One run of a crawl, with the time it spent crawling and the stretches it spent it in: a pause is the gap between two.</summary>
/// <param name="EndedAt">Null for a run that hasn't ended: the one going now, or one cut short when its process died.</param>
internal sealed record HistoryRunDto(DateTime StartedAt, DateTime? EndedAt, long CrawlingMs, IReadOnlyList<HistoryStretchDto> Crawling)
{
    public static HistoryRunDto From(CrawlRun run) => new(run.StartedAt.UtcDateTime, run.EndedAt?.UtcDateTime, (long)run.TimeCrawling.TotalMilliseconds,
        [.. run.Crawling.Select(stretch => new HistoryStretchDto(stretch.From.UtcDateTime, stretch.To.UtcDateTime))]);
}
