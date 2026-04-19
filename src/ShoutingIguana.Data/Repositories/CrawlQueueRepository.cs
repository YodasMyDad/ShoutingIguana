using Microsoft.EntityFrameworkCore;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;

namespace ShoutingIguana.Data.Repositories;

public class CrawlQueueRepository(IShoutingIguanaDbContext context) : ICrawlQueueRepository
{
    public async Task<CrawlQueueItem?> GetNextItemAsync(int projectId)
    {
        // Two-step atomic claim: pick the next Queued row, then UPDATE it to InProgress
        // with a `State = Queued` predicate so only one worker wins per row. SQLite
        // serialises writes, so the losing worker's UPDATE reports zero rows affected
        // and retries. Without this, concurrent workers would dequeue the same row.
        const int maxAttempts = 3;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var candidateId = await context.CrawlQueue
                .AsNoTracking()
                .Where(q => q.ProjectId == projectId && q.State == QueueState.Queued)
                .OrderByDescending(q => q.Priority)
                .ThenBy(q => q.EnqueuedUtc)
                .Select(q => (int?)q.Id)
                .FirstOrDefaultAsync().ConfigureAwait(false);

            if (candidateId == null)
            {
                return null;
            }

            var claimed = await context.Database.ExecuteSqlInterpolatedAsync(
                $@"UPDATE CrawlQueue
                   SET State = {(int)QueueState.InProgress}
                   WHERE Id = {candidateId} AND State = {(int)QueueState.Queued}"
            ).ConfigureAwait(false);

            if (claimed == 0)
            {
                continue;
            }

            return await context.CrawlQueue
                .FirstOrDefaultAsync(q => q.Id == candidateId).ConfigureAwait(false);
        }
        return null;
    }

    public async Task<IEnumerable<CrawlQueueItem>> GetQueuedItemsAsync(int projectId, int count = 10)
    {
        return await context.CrawlQueue
            .Where(q => q.ProjectId == projectId && q.State == QueueState.Queued)
            .OrderByDescending(q => q.Priority)
            .ThenBy(q => q.EnqueuedUtc)
            .Take(count)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<List<CrawlQueueItem>> GetPagedQueuedItemsAsync(int projectId, int skip, int take)
    {
        return await context.CrawlQueue
            .AsNoTracking()
            .Where(q => q.ProjectId == projectId && q.State == QueueState.Queued)
            .OrderByDescending(q => q.Priority)
            .ThenBy(q => q.EnqueuedUtc)
            .ThenBy(q => q.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<CrawlQueueItem?> GetByAddressAsync(int projectId, string address)
    {
        return await context.CrawlQueue
            .FirstOrDefaultAsync(q => q.ProjectId == projectId && q.Address == address)
            .ConfigureAwait(false);
    }

    public async Task<CrawlQueueItem> EnqueueAsync(CrawlQueueItem item)
    {
        context.CrawlQueue.Add(item);
        await context.SaveChangesAsync().ConfigureAwait(false);
        return item;
    }

    public async Task<CrawlQueueItem> CreateAsync(CrawlQueueItem item)
    {
        context.CrawlQueue.Add(item);
        await context.SaveChangesAsync().ConfigureAwait(false);
        return item;
    }

    public async Task<CrawlQueueItem> UpdateAsync(CrawlQueueItem item)
    {
        context.Entry(item).State = EntityState.Modified;
        await context.SaveChangesAsync().ConfigureAwait(false);
        return item;
    }

    public async Task<int> CountQueuedAsync(int projectId)
    {
        return await context.CrawlQueue
            .CountAsync(q => q.ProjectId == projectId && q.State == QueueState.Queued).ConfigureAwait(false);
    }

    public async Task ClearQueueAsync(int projectId)
    {
        var items = await context.CrawlQueue
            .Where(q => q.ProjectId == projectId && q.State == QueueState.Queued)
            .ToListAsync().ConfigureAwait(false);
        
        context.CrawlQueue.RemoveRange(items);
        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task<int> CountByStateAsync(int projectId, QueueState state)
    {
        return await context.CrawlQueue
            .CountAsync(q => q.ProjectId == projectId && q.State == state).ConfigureAwait(false);
    }

    public async Task ResetInProgressItemsAsync(int projectId)
    {
        var items = await context.CrawlQueue
            .Where(q => q.ProjectId == projectId && q.State == QueueState.InProgress)
            .ToListAsync().ConfigureAwait(false);
        
        foreach (var item in items)
        {
            item.State = QueueState.Queued;
        }
        
        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task DeleteAllByProjectIdAsync(int projectId)
    {
        var items = await context.CrawlQueue
            .Where(q => q.ProjectId == projectId)
            .ToListAsync().ConfigureAwait(false);
        
        context.CrawlQueue.RemoveRange(items);
        await context.SaveChangesAsync().ConfigureAwait(false);
    }
}

