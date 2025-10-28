using Microsoft.EntityFrameworkCore;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;

namespace ShoutingIguana.Data.Repositories;

public class CrawlQueueRepository(IShoutingIguanaDbContext context) : ICrawlQueueRepository
{
    private readonly IShoutingIguanaDbContext _context = context;

    public async Task<CrawlQueueItem?> GetNextItemAsync(int projectId)
    {
        return await _context.CrawlQueue
            .Where(q => q.ProjectId == projectId && q.State == QueueState.Queued)
            .OrderByDescending(q => q.Priority)
            .ThenBy(q => q.EnqueuedUtc)
            .FirstOrDefaultAsync().ConfigureAwait(false);
    }

    public async Task<IEnumerable<CrawlQueueItem>> GetQueuedItemsAsync(int projectId, int count = 10)
    {
        return await _context.CrawlQueue
            .Where(q => q.ProjectId == projectId && q.State == QueueState.Queued)
            .OrderByDescending(q => q.Priority)
            .ThenBy(q => q.EnqueuedUtc)
            .Take(count)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<CrawlQueueItem?> GetByAddressAsync(int projectId, string address)
    {
        return await _context.CrawlQueue
            .FirstOrDefaultAsync(q => q.ProjectId == projectId && q.Address == address)
            .ConfigureAwait(false);
    }

    public async Task<CrawlQueueItem> EnqueueAsync(CrawlQueueItem item)
    {
        _context.CrawlQueue.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        return item;
    }

    public async Task<CrawlQueueItem> CreateAsync(CrawlQueueItem item)
    {
        _context.CrawlQueue.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        return item;
    }

    public async Task<CrawlQueueItem> UpdateAsync(CrawlQueueItem item)
    {
        _context.Entry(item).State = EntityState.Modified;
        await _context.SaveChangesAsync().ConfigureAwait(false);
        return item;
    }

    public async Task<int> CountQueuedAsync(int projectId)
    {
        return await _context.CrawlQueue
            .CountAsync(q => q.ProjectId == projectId && q.State == QueueState.Queued).ConfigureAwait(false);
    }

    public async Task ClearQueueAsync(int projectId)
    {
        var items = await _context.CrawlQueue
            .Where(q => q.ProjectId == projectId && q.State == QueueState.Queued)
            .ToListAsync().ConfigureAwait(false);
        
        _context.CrawlQueue.RemoveRange(items);
        await _context.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task<int> CountByStateAsync(int projectId, QueueState state)
    {
        return await _context.CrawlQueue
            .CountAsync(q => q.ProjectId == projectId && q.State == state).ConfigureAwait(false);
    }

    public async Task ResetInProgressItemsAsync(int projectId)
    {
        var items = await _context.CrawlQueue
            .Where(q => q.ProjectId == projectId && q.State == QueueState.InProgress)
            .ToListAsync().ConfigureAwait(false);
        
        foreach (var item in items)
        {
            item.State = QueueState.Queued;
        }
        
        await _context.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task DeleteAllByProjectIdAsync(int projectId)
    {
        var items = await _context.CrawlQueue
            .Where(q => q.ProjectId == projectId)
            .ToListAsync().ConfigureAwait(false);
        
        _context.CrawlQueue.RemoveRange(items);
        await _context.SaveChangesAsync().ConfigureAwait(false);
    }
}

