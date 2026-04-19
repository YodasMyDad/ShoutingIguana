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
        await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Double-check cache after acquiring semaphore
            if (_cache.TryGetValue(cacheKey, out var cachedResult2))
            {
                return cachedResult2;
            }

            var result = await CheckUrlInternalAsync(url, userAgent, cancellationToken).ConfigureAwait(false);
            
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
            // Handle protocol-relative URLs (//example.com/path)
            // These should be treated as https:// URLs
            if (url.StartsWith("//"))
            {
                url = "https:" + url;
            }
            
            // Validate URL scheme - only support http and https
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                result.StatusCode = 0;
                result.IsSuccess = false;
                result.ErrorMessage = "Invalid URL format";
                result.ResponseTime = DateTime.UtcNow - startTime;
                return result;
            }

            if (uri.Scheme != "http" && uri.Scheme != "https")
            {
                result.StatusCode = 0;
                result.IsSuccess = false;
                result.ErrorMessage = $"Unsupported URL scheme: {uri.Scheme}";
                result.ResponseTime = DateTime.UtcNow - startTime;
                _logger.LogDebug("Skipping external link check for unsupported scheme: {Url}", url);
                return result;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout);

            // First attempt: HEAD to avoid downloading content
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, url);
            AddBrowserHeaders(headRequest, userAgent);

            var response = await _httpClient.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            try
            {
                var status = (int)response.StatusCode;

                // Many hosts reject HEAD (405) or return 403/401 to HEAD but allow GET.
                // Retry once with GET to avoid false positives.
                if (status == 405 || status == 403 || status == 401)
                {
                    response.Dispose();

                    using var getRequest = new HttpRequestMessage(HttpMethod.Get, url);
                    AddBrowserHeaders(getRequest, userAgent);

                    response = await _httpClient.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                    status = (int)response.StatusCode;
                }

                result.StatusCode = status;
                result.IsSuccess = response.IsSuccessStatusCode;
                result.ResponseTime = DateTime.UtcNow - startTime;
            }
            finally
            {
                response.Dispose();
            }
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

    private static void AddBrowserHeaders(HttpRequestMessage request, string userAgent)
    {
        request.Headers.Add("User-Agent", userAgent);
        request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
        request.Headers.Add("Accept-Language", "en-US,en;q=0.9");
        request.Headers.Add("Accept-Encoding", "gzip, deflate, br");
        request.Headers.Add("DNT", "1");
        request.Headers.Add("Connection", "keep-alive");
        request.Headers.Add("Upgrade-Insecure-Requests", "1");
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

