using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Core.Configuration;

namespace ShoutingIguana.Core.Services;

/// <summary>
/// Implementation of IAppSettingsService that stores settings in JSON file.
/// </summary>
public class AppSettingsService(ILogger<AppSettingsService> logger) : IAppSettingsService
{
    private readonly ILogger<AppSettingsService> _logger = logger;
    private readonly string _settingsPath = GetSettingsPath();
    private readonly object _lock = new();

    public BrowserSettings BrowserSettings { get; private set; } = new();
    public CrawlSettings CrawlSettings { get; set; } = new();
    private List<RecentProject> _recentProjects = new();

    public async Task LoadAsync()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = await File.ReadAllTextAsync(_settingsPath).ConfigureAwait(false);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    BrowserSettings = settings.Browser ?? new BrowserSettings();
                    CrawlSettings = settings.Crawl ?? new CrawlSettings();
                    lock (_lock)
                    {
                        _recentProjects = settings.RecentProjects ?? new List<RecentProject>();
                    }
                }
                _logger.LogInformation("Settings loaded from: {Path}", _settingsPath);
            }
            else
            {
                _logger.LogInformation("Settings file not found, using defaults");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading settings");
        }
    }

    public async Task SaveAsync()
    {
        try
        {
            List<RecentProject> recentProjectsCopy;
            lock (_lock)
            {
                recentProjectsCopy = new List<RecentProject>(_recentProjects);
            }

            var settings = new AppSettings
            {
                Browser = BrowserSettings,
                Crawl = CrawlSettings,
                RecentProjects = recentProjectsCopy
            };

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(_settingsPath, json).ConfigureAwait(false);
            _logger.LogInformation("Settings saved to: {Path}", _settingsPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving settings");
        }
    }

    public void MarkBrowserInstalled()
    {
        BrowserSettings.IsBrowserInstalled = true;
        BrowserSettings.LastInstalledUtc = DateTime.UtcNow;
    }

    public void AddRecentProject(string name, string filePath)
    {
        lock (_lock)
        {
            // Remove if already exists (to update timestamp and move to top)
            _recentProjects.RemoveAll(p => string.Equals(p.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

            // Add to beginning of list
            _recentProjects.Insert(0, new RecentProject
            {
                Name = name,
                FilePath = filePath,
                LastOpenedUtc = DateTime.UtcNow
            });

            // Keep only the 5 most recent
            if (_recentProjects.Count > 5)
            {
                _recentProjects = _recentProjects.Take(5).ToList();
            }

            _logger.LogDebug("Added recent project: {Name} at {FilePath}", name, filePath);
        }
    }

    public List<RecentProject> GetRecentProjects()
    {
        lock (_lock)
        {
            // Return a copy to prevent external modification
            return new List<RecentProject>(_recentProjects);
        }
    }

    public void RemoveRecentProject(string filePath)
    {
        lock (_lock)
        {
            var removed = _recentProjects.RemoveAll(p => string.Equals(p.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                _logger.LogDebug("Removed recent project: {FilePath}", filePath);
            }
        }
    }

    private static string GetSettingsPath()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appDataPath, "ShoutingIguana", "settings.json");
    }

    private class AppSettings
    {
        public BrowserSettings? Browser { get; set; }
        public CrawlSettings? Crawl { get; set; }
        public List<RecentProject>? RecentProjects { get; set; }
    }
}

