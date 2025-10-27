using ShoutingIguana.Core.Configuration;

namespace ShoutingIguana.Core.Services;

/// <summary>
/// Service for managing application settings.
/// </summary>
public interface IAppSettingsService
{
    BrowserSettings BrowserSettings { get; }
    CrawlSettings CrawlSettings { get; set; }
    
    Task LoadAsync();
    Task SaveAsync();
    
    void MarkBrowserInstalled();
    
    // Recent Projects
    void AddRecentProject(string name, string filePath);
    List<RecentProject> GetRecentProjects();
    void RemoveRecentProject(string filePath);
}

