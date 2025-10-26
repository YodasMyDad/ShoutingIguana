using System.ComponentModel.DataAnnotations;

namespace ShoutingIguana.Core.Models;

/// <summary>
/// Represents a checkpoint in the crawl process for crash recovery.
/// </summary>
public class CrawlCheckpoint
{
    [Key]
    public int Id { get; set; }
    
    public int ProjectId { get; set; }
    
    /// <summary>
    /// When the checkpoint was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// Number of URLs crawled at checkpoint.
    /// </summary>
    public int UrlsCrawled { get; set; }
    
    /// <summary>
    /// Number of errors at checkpoint.
    /// </summary>
    public int ErrorCount { get; set; }
    
    /// <summary>
    /// Queue size at checkpoint.
    /// </summary>
    public int QueueSize { get; set; }
    
    /// <summary>
    /// Last crawled URL.
    /// </summary>
    public string? LastCrawledUrl { get; set; }
    
    /// <summary>
    /// Crawl status at checkpoint.
    /// </summary>
    public string Status { get; set; } = "InProgress";
    
    /// <summary>
    /// Crawl elapsed time in seconds.
    /// </summary>
    public double ElapsedSeconds { get; set; }
    
    /// <summary>
    /// Indicates if this checkpoint is active (crawl is in progress).
    /// </summary>
    public bool IsActive { get; set; }
    
    public Project? Project { get; set; }
}

