using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;

namespace ShoutingIguana.Data.Repositories;

public class CrawlCheckpointRepository(
    IShoutingIguanaDbContext context,
    ILogger<CrawlCheckpointRepository> logger) : ICrawlCheckpointRepository
{
    private readonly IShoutingIguanaDbContext _context = context;
    private readonly ILogger<CrawlCheckpointRepository> _logger = logger;

    public async Task<CrawlCheckpoint> CreateAsync(CrawlCheckpoint checkpoint)
    {
        _context.CrawlCheckpoints.Add(checkpoint);
        await _context.SaveChangesAsync();
        _logger.LogDebug("Created checkpoint {Id} for project {ProjectId}", checkpoint.Id, checkpoint.ProjectId);
        return checkpoint;
    }

    public async Task<CrawlCheckpoint?> GetActiveCheckpointAsync(int projectId)
    {
        return await _context.CrawlCheckpoints
            .Where(c => c.ProjectId == projectId && c.IsActive)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task DeactivateCheckpointsAsync(int projectId)
    {
        var checkpoints = await _context.CrawlCheckpoints
            .Where(c => c.ProjectId == projectId && c.IsActive)
            .ToListAsync();

        foreach (var checkpoint in checkpoints)
        {
            checkpoint.IsActive = false;
            checkpoint.Status = "Completed";
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Deactivated {Count} checkpoints for project {ProjectId}", checkpoints.Count, projectId);
    }

    public async Task CleanupOldCheckpointsAsync(int projectId)
    {
        // Keep only the last 5 checkpoints
        var allCheckpoints = await _context.CrawlCheckpoints
            .Where(c => c.ProjectId == projectId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        var toDelete = allCheckpoints.Skip(5).ToList();
        
        if (toDelete.Count > 0)
        {
            _context.CrawlCheckpoints.RemoveRange(toDelete);
            await _context.SaveChangesAsync();
            _logger.LogDebug("Cleaned up {Count} old checkpoints for project {ProjectId}", toDelete.Count, projectId);
        }
    }
}

