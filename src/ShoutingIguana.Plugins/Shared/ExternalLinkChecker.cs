using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace ShoutingIguana.Plugins.Shared;

/// <summary>
/// Thread-safe service for checking external link status with caching.
/// Cache is keyed by both URL and User-Agent to support multiple projects with different UA settings.
/// </summary>
public class ExternalLinkChecker : IDisposable
{
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, ExternalLinkResult> _cache = new();
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _rateLimiter;
    private readonly HttpClient _httpClient;
    private bool _disposed;

    public ExternalLinkChecker(ILogger logger, TimeSpan? timeout = null, int maxConcurrent = 5)
    {
        _logger = logger;
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
        _rateLimiter = new SemaphoreSlim(maxConcurrent);
        _httpClient = new HttpClient { Timeout = _timeout };
    }

    /// <summary>
    /// Checks an external URL and returns cached result if available.
    /// </summary>
    /// <param name="url">The URL to check</param>
    /// <param name="userAgent">User-Agent string to use for the request (respects project settings)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task<ExternalLinkResult> CheckUrlAsync(string url, string userAgent, CancellationToken cancellationToken = default)
    {
        // Create cache key combining URL and User-Agent
        // This ensures different projects with different UA settings don't share cache
        var cacheKey = $"{url}|{userAgent}";
        
        // Check cache first
        if (_cache.TryGetValue(cacheKey, out var cachedResult))
        {
            return cachedResult;
        }

        // Rate limit concurrent requests
        await _rateLimiter.WaitAsync(cancellationToken);
        
        try
        {
            // Double-check cache after acquiring semaphore
            if (_cache.TryGetValue(cacheKey, out var cachedResult2))
            {
                return cachedResult2;
            }

            var result = await CheckUrlInternalAsync(url, userAgent, cancellationToken);
            
            // Cache the result
            _cache.TryAdd(cacheKey, result);
            
            return result;
        }
        finally
        {
            _rateLimiter.Release();
        }
    }

    private async Task<ExternalLinkResult> CheckUrlInternalAsync(string url, string userAgent, CancellationToken cancellationToken)
    {
        var result = new ExternalLinkResult { Url = url };
        var startTime = DateTime.UtcNow;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout);

            // Use HEAD request to avoid downloading content
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            
            // Use the project's configured User-Agent (respects Chrome/Firefox/Edge/Safari/Random setting)
            request.Headers.Add("User-Agent", userAgent);
            request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            request.Headers.Add("Accept-Language", "en-US,en;q=0.9");
            request.Headers.Add("Accept-Encoding", "gzip, deflate, br");
            request.Headers.Add("DNT", "1");
            request.Headers.Add("Connection", "keep-alive");
            request.Headers.Add("Upgrade-Insecure-Requests", "1");

            using var response = await _httpClient.SendAsync(request, cts.Token);
            
            result.StatusCode = (int)response.StatusCode;
            result.IsSuccess = response.IsSuccessStatusCode;
            result.ResponseTime = DateTime.UtcNow - startTime;
        }
        catch (TaskCanceledException)
        {
            result.StatusCode = 0;
            result.IsSuccess = false;
            result.ErrorMessage = "Request timeout";
            result.ResponseTime = DateTime.UtcNow - startTime;
        }
        catch (HttpRequestException ex)
        {
            result.StatusCode = 0;
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
            result.ResponseTime = DateTime.UtcNow - startTime;
            _logger.LogDebug(ex, "Error checking external URL: {Url}", url);
        }
        catch (Exception ex)
        {
            result.StatusCode = 0;
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
            result.ResponseTime = DateTime.UtcNow - startTime;
            _logger.LogWarning(ex, "Unexpected error checking external URL: {Url}", url);
        }

        return result;
    }

    /// <summary>
    /// Clears the cache.
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _httpClient.Dispose();
        _rateLimiter.Dispose();
        _disposed = true;
    }
}

public class ExternalLinkResult
{
    public string Url { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public bool IsSuccess { get; set; }
    public TimeSpan ResponseTime { get; set; }
    public string? ErrorMessage { get; set; }
}

