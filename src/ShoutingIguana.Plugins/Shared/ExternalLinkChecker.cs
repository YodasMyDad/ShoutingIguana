using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace ShoutingIguana.Plugins.Shared;

/// <summary>
/// Thread-safe service for checking external link status with caching.
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
    public async Task<ExternalLinkResult> CheckUrlAsync(string url, CancellationToken cancellationToken = default)
    {
        // Check cache first
        if (_cache.TryGetValue(url, out var cachedResult))
        {
            return cachedResult;
        }

        // Rate limit concurrent requests
        await _rateLimiter.WaitAsync(cancellationToken);
        
        try
        {
            // Double-check cache after acquiring semaphore
            if (_cache.TryGetValue(url, out var cachedResult2))
            {
                return cachedResult2;
            }

            var result = await CheckUrlInternalAsync(url, cancellationToken);
            
            // Cache the result
            _cache.TryAdd(url, result);
            
            return result;
        }
        finally
        {
            _rateLimiter.Release();
        }
    }

    private async Task<ExternalLinkResult> CheckUrlInternalAsync(string url, CancellationToken cancellationToken)
    {
        var result = new ExternalLinkResult { Url = url };
        var startTime = DateTime.UtcNow;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout);

            // Use HEAD request to avoid downloading content
            var request = new HttpRequestMessage(HttpMethod.Head, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (compatible; ShoutingIguana/1.0; +https://github.com/yourrepo)");

            var response = await _httpClient.SendAsync(request, cts.Token);
            
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

        _httpClient?.Dispose();
        _rateLimiter?.Dispose();
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

