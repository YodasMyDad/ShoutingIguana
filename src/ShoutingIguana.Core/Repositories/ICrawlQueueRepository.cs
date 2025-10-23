using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Core.Repositories;

public interface ICrawlQueueRepository
{
    Task<CrawlQueueItem?> GetNextItemAsync(int projectId);
    Task<IEnumerable<CrawlQueueItem>> GetQueuedItemsAsync(int projectId, int count = 10);
    Task<CrawlQueueItem> EnqueueAsync(CrawlQueueItem item);
    Task<CrawlQueueItem> UpdateAsync(CrawlQueueItem item);
    Task<int> CountQueuedAsync(int projectId);
    Task ClearQueueAsync(int projectId);
}

