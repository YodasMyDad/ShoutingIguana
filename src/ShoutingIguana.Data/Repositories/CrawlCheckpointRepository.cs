using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;

namespace ShoutingIguana.Data.Repositories;

public class CrawlCheckpointRepository(
    IShoutingIguanaDbContext context,
    ILogger<CrawlCheckpointRepository> logger) : ICrawlCheckpointRepository
{
    public async Task<CrawlCheckpoint> CreateAsync(CrawlCheckpoint checkpoint)
    {
        context.CrawlCheckpoints.Add(checkpoint);
        await context.SaveChangesAsync().ConfigureAwait(false);
        logger.LogDebug("Created checkpoint {Id} for project {ProjectId}", checkpoint.Id, checkpoint.ProjectId);
        return checkpoint;
    }

    public async Task<CrawlCheckpoint?> GetActiveCheckpointAsync(int projectId)
    {
        return await context.CrawlCheckpoints
            .Where(c => c.ProjectId == projectId && c.IsActive)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync().ConfigureAwait(false);
    }

    public async Task DeactivateCheckpointsAsync(int projectId)
    {
        var checkpoints = await context.CrawlCheckpoints
            .Where(c => c.ProjectId == projectId && c.IsActive)
            .ToListAsync().ConfigureAwait(false);

        foreach (var checkpoint in checkpoints)
        {
            checkpoint.IsActive = false;
            checkpoint.Status = "Completed";
        }

        await context.SaveChangesAsync().ConfigureAwait(false);
        logger.LogInformation("Deactivated {Count} checkpoints for project {ProjectId}", checkpoints.Count, projectId);
    }

    public async Task CleanupOldCheckpointsAsync(int projectId)
    {
        // Keep only the last 5 checkpoints
        var allCheckpoints = await context.CrawlCheckpoints
            .Where(c => c.ProjectId == projectId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync().ConfigureAwait(false);

        var toDelete = allCheckpoints.Skip(5).ToList();
        
        if (toDelete.Count > 0)
        {
            context.CrawlCheckpoints.RemoveRange(toDelete);
            await context.SaveChangesAsync().ConfigureAwait(false);
            logger.LogDebug("Cleaned up {Count} old checkpoints for project {ProjectId}", toDelete.Count, projectId);
        }
    }
}

