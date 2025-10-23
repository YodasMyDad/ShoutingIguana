namespace ShoutingIguana.Core.Models;

public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime LastOpenedUtc { get; set; }
    public string SettingsJson { get; set; } = "{}";
    
    // Navigation properties
    public ICollection<Url> Urls { get; set; } = [];
    public ICollection<Link> Links { get; set; } = [];
    public ICollection<CrawlQueueItem> CrawlQueue { get; set; } = [];
}

