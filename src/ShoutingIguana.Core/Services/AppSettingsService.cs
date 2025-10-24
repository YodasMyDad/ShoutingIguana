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

    public BrowserSettings BrowserSettings { get; private set; } = new();
    public CrawlSettings CrawlSettings { get; set; } = new();

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
            var settings = new AppSettings
            {
                Browser = BrowserSettings,
                Crawl = CrawlSettings
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

    private static string GetSettingsPath()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appDataPath, "ShoutingIguana", "settings.json");
    }

    private class AppSettings
    {
        public BrowserSettings? Browser { get; set; }
        public CrawlSettings? Crawl { get; set; }
    }
}

