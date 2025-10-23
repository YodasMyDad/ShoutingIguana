namespace ShoutingIguana.Core.Models;

public class CrawlQueueItem
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Address { get; set; } = string.Empty;
    public int Priority { get; set; }
    public int Depth { get; set; }
    public string HostKey { get; set; } = string.Empty;
    public DateTime EnqueuedUtc { get; set; }
    public QueueState State { get; set; }
    
    // Navigation properties
    public Project Project { get; set; } = null!;
}

public enum QueueState
{
    Queued = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3
}

