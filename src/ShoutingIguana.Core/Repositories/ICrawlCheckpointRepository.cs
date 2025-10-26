using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Core.Repositories;

/// <summary>
/// Repository for managing crawl checkpoints for crash recovery.
/// </summary>
public interface ICrawlCheckpointRepository
{
    /// <summary>
    /// Creates a new checkpoint.
    /// </summary>
    Task<CrawlCheckpoint> CreateAsync(CrawlCheckpoint checkpoint);
    
    /// <summary>
    /// Gets the active checkpoint for a project, if any.
    /// </summary>
    Task<CrawlCheckpoint?> GetActiveCheckpointAsync(int projectId);
    
    /// <summary>
    /// Marks all checkpoints for a project as inactive.
    /// </summary>
    Task DeactivateCheckpointsAsync(int projectId);
    
    /// <summary>
    /// Deletes old checkpoints for a project (keeps only the last one).
    /// </summary>
    Task CleanupOldCheckpointsAsync(int projectId);
}

