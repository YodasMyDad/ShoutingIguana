using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using System.Runtime.Versioning;

namespace ShoutingIguana.Core.Services.NuGet;

/// <summary>
/// Implementation of IFeedConfigurationService that stores feeds in a JSON file with encrypted credentials.
/// </summary>
[SupportedOSPlatform("windows")]
public class FeedConfigurationService(ILogger<FeedConfigurationService> logger) : IFeedConfigurationService
{
    private readonly ILogger<FeedConfigurationService> _logger = logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<NuGetFeed>? _cachedFeeds;

    private static string GetFeedsFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appDir = Path.Combine(appData, "ShoutingIguana");
        Directory.CreateDirectory(appDir);
        return Path.Combine(appDir, "feeds.json");
    }

    public async Task<IReadOnlyList<NuGetFeed>> GetFeedsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_cachedFeeds != null)
            {
                return _cachedFeeds.AsReadOnly();
            }

            var filePath = GetFeedsFilePath();
            
            if (!File.Exists(filePath))
            {
                // Return default feeds
                _cachedFeeds =
                [
                    new NuGetFeed
                    {
                        Name = "nuget.org",
                        Url = "https://api.nuget.org/v3/index.json",
                        Enabled = true
                    }
                ];
                
                // Save defaults
                await SaveFeedsAsync(_cachedFeeds);
                
                return _cachedFeeds.AsReadOnly();
            }

            var json = await File.ReadAllTextAsync(filePath);
            var feeds = JsonSerializer.Deserialize<List<FeedData>>(json) ?? [];

            _cachedFeeds = feeds.Select(f => new NuGetFeed
            {
                Name = f.Name,
                Url = f.Url,
                Username = f.Username,
                Password = f.EncryptedPassword != null ? Decrypt(f.EncryptedPassword) : null,
                Enabled = f.Enabled
            }).ToList();

            return _cachedFeeds.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load feeds configuration");
            
            // Return default on error
            _cachedFeeds =
            [
                new NuGetFeed
                {
                    Name = "nuget.org",
                    Url = "https://api.nuget.org/v3/index.json",
                    Enabled = true
                }
            ];
            
            return _cachedFeeds.AsReadOnly();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AddFeedAsync(NuGetFeed feed)
    {
        await _lock.WaitAsync();
        try
        {
            // Ensure cached feeds are loaded
            if (_cachedFeeds == null)
            {
                _lock.Release();
                await GetFeedsAsync(); // This will acquire and release the lock
                await _lock.WaitAsync();
            }
            
            var feeds = _cachedFeeds!.ToList();
            
            if (feeds.Any(f => f.Name.Equals(feed.Name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Feed with name '{feed.Name}' already exists");
            }

            feeds.Add(feed);
            await SaveFeedsAsync(feeds);
            
            _cachedFeeds = feeds;
            _logger.LogInformation("Added feed: {FeedName}", feed.Name);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveFeedAsync(string name)
    {
        await _lock.WaitAsync();
        try
        {
            // Ensure cached feeds are loaded
            if (_cachedFeeds == null)
            {
                _lock.Release();
                await GetFeedsAsync(); // This will acquire and release the lock
                await _lock.WaitAsync();
            }
            
            var feeds = _cachedFeeds!.ToList();
            var feed = feeds.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            
            if (feed == null)
            {
                _logger.LogWarning("Feed not found: {FeedName}", name);
                return;
            }

            feeds.Remove(feed);
            await SaveFeedsAsync(feeds);
            
            _cachedFeeds = feeds;
            _logger.LogInformation("Removed feed: {FeedName}", name);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateFeedAsync(NuGetFeed feed)
    {
        await _lock.WaitAsync();
        try
        {
            // Ensure cached feeds are loaded
            if (_cachedFeeds == null)
            {
                _lock.Release();
                await GetFeedsAsync(); // This will acquire and release the lock
                await _lock.WaitAsync();
            }
            
            var feeds = _cachedFeeds!.ToList();
            var index = feeds.FindIndex(f => f.Name.Equals(feed.Name, StringComparison.OrdinalIgnoreCase));
            
            if (index < 0)
            {
                throw new InvalidOperationException($"Feed with name '{feed.Name}' not found");
            }

            feeds[index] = feed;
            await SaveFeedsAsync(feeds);
            
            _cachedFeeds = feeds;
            _logger.LogInformation("Updated feed: {FeedName}", feed.Name);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SaveFeedsAsync(List<NuGetFeed> feeds)
    {
        var feedData = feeds.Select(f => new FeedData
        {
            Name = f.Name,
            Url = f.Url,
            Username = f.Username,
            EncryptedPassword = f.Password != null ? Encrypt(f.Password) : null,
            Enabled = f.Enabled
        }).ToList();

        var json = JsonSerializer.Serialize(feedData, new JsonSerializerOptions { WriteIndented = true });
        var filePath = GetFeedsFilePath();
        
        await File.WriteAllTextAsync(filePath, json);
    }

    private static string Encrypt(string plainText)
    {
        // Use DPAPI (Data Protection API) for Windows
        var bytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    private static string Decrypt(string encryptedText)
    {
        var bytes = Convert.FromBase64String(encryptedText);
        var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }

    private class FeedData
    {
        public required string Name { get; init; }
        public required string Url { get; init; }
        public string? Username { get; init; }
        public string? EncryptedPassword { get; init; }
        public bool Enabled { get; init; }
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}

