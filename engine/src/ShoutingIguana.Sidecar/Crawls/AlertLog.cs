using System.Collections.Concurrent;
using ShoutingIguana.Sidecar.Api.Models;

namespace ShoutingIguana.Sidecar.Crawls;

/// <summary>
/// Every crawl's alerts since the sidecar started, oldest first, for a UI that wasn't listening when they came. At most
/// <c>limitPerCrawl</c> per crawl are kept, dropping the oldest, so a long-running sidecar's memory stays flat.
/// </summary>
internal sealed class AlertLog(int limitPerCrawl = AlertLog.DEFAULT_LIMIT)
{
    public const int DEFAULT_LIMIT = 1_000;

    private readonly ConcurrentDictionary<string, Queue<AlertDto>> _alerts = new(StringComparer.Ordinal);

    public void Add(AlertDto alert)
    {
        var alerts = _alerts.GetOrAdd(alert.CrawlId, _ => new Queue<AlertDto>());
        lock (alerts)
        {
            alerts.Enqueue(alert);
            if (alerts.Count > limitPerCrawl)
            {
                alerts.Dequeue();
            }
        }
    }

    public IReadOnlyList<AlertDto> For(string crawlId)
    {
        if (!_alerts.TryGetValue(crawlId, out var alerts))
        {
            return [];
        }

        lock (alerts)
        {
            return [.. alerts];
        }
    }

    public void Forget(string crawlId) => _alerts.TryRemove(crawlId, out _);
}
