using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Core.Configuration;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;

namespace ShoutingIguana.Core.Services;

public class CrawlEngine(
    ILogger<CrawlEngine> logger,
    IServiceProvider serviceProvider,
    IRobotsService robotsService,
    ILinkExtractor linkExtractor,
    IHttpClientFactory httpClientFactory,
    IPlaywrightService playwrightService,
    IPluginRegistry pluginRegistry) : ICrawlEngine
{
    private readonly ILogger<CrawlEngine> _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IRobotsService _robotsService = robotsService;
    private readonly ILinkExtractor _linkExtractor = linkExtractor;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IPlaywrightService _playwrightService = playwrightService;
    private readonly IPluginRegistry _pluginRegistry = pluginRegistry;
    
    private CancellationTokenSource? _cts;
    private Task? _crawlTask;
    private readonly ConcurrentDictionary<string, DateTime> _lastCrawlTime = new();
    
    // Performance counters
    private int _urlsCrawled;
    private int _totalDiscovered;
    private int _activeWorkers;
    private int _errorCount;
    private int _queueSize; // Cached queue size
    private Stopwatch? _stopwatch;
    private string? _lastCrawledUrl;
    private int _lastCrawledStatus;

    public bool IsCrawling { get; private set; }
    public event EventHandler<CrawlProgressEventArgs>? ProgressUpdated;

    public Task StartCrawlAsync(int projectId, CancellationToken cancellationToken = default)
    {
        if (IsCrawling)
        {
            _logger.LogWarning("Crawl is already running");
            return Task.CompletedTask;
        }

        IsCrawling = true;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _urlsCrawled = 0;
        _totalDiscovered = 0;
        _activeWorkers = 0;
        _errorCount = 0;
        _queueSize = 0;
        _stopwatch = Stopwatch.StartNew();

        _crawlTask = Task.Run(async () =>
        {
            try
            {
                await RunCrawlAsync(projectId, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Crawl was cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Crawl failed with error");
            }
            finally
            {
                IsCrawling = false;
                _stopwatch?.Stop();
                
                // Send final progress update so UI knows crawl has finished
                SendProgressUpdate(projectId);
            }
        }, _cts.Token);
        
        return Task.CompletedTask;
    }

    public async Task StopCrawlAsync()
    {
        if (!IsCrawling || _cts == null)
            return;

        _logger.LogInformation("Stopping crawl...");
        _cts.Cancel();

        if (_crawlTask != null)
        {
            try
            {
                await _crawlTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                _crawlTask = null;
            }
        }
    }

    private async Task RunCrawlAsync(int projectId, CancellationToken cancellationToken)
    {
        Project? project;
        ProjectSettings settings;
        int queueSize;
        
        using (var scope = _serviceProvider.CreateScope())
        {
            var projectRepository = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
            project = await projectRepository.GetByIdAsync(projectId);
            if (project == null)
            {
                _logger.LogError("Project {ProjectId} not found", projectId);
                return;
            }

            settings = System.Text.Json.JsonSerializer.Deserialize<ProjectSettings>(project.SettingsJson)
                ?? new ProjectSettings();

            // Seed the queue with base URL if empty
            var queueRepository = scope.ServiceProvider.GetRequiredService<ICrawlQueueRepository>();
            queueSize = await queueRepository.CountQueuedAsync(projectId).ConfigureAwait(false);
            if (queueSize == 0)
            {
                _logger.LogInformation("Seeding queue with base URL: {BaseUrl}", settings.BaseUrl);
                await EnqueueUrlAsync(projectId, settings.BaseUrl, 0, 1000, settings.BaseUrl).ConfigureAwait(false);
            }

            // Initialize total discovered count from current queue size
            // This ensures progress percentage stays at or below 100%
            _totalDiscovered = await queueRepository.CountQueuedAsync(projectId).ConfigureAwait(false);
            _queueSize = _totalDiscovered;
        }

        _logger.LogInformation("Starting {WorkerCount} workers for project {ProjectId}. Total discovered: {TotalDiscovered}", 
            settings.ConcurrentRequests, projectId, _totalDiscovered);

        // Create worker tasks (without progress reporter)
        var workers = new List<Task>();
        for (int i = 0; i < settings.ConcurrentRequests; i++)
        {
            workers.Add(WorkerAsync(projectId, settings, cancellationToken));
        }

        // Progress reporting task (separate so we can control it)
        var progressTask = ReportProgressAsync(projectId, cancellationToken);

        // Wait for all worker tasks to complete (not including progress reporter)
        await Task.WhenAll(workers).ConfigureAwait(false);
        
        _logger.LogInformation("All workers completed. Crawled {UrlsCrawled} URLs, discovered {TotalDiscovered} total", 
            _urlsCrawled, _totalDiscovered);

        // Send final progress update before cancelling progress reporter
        SendProgressUpdate(projectId);
        
        // Cancel the progress reporter and wait for it
        _cts?.Cancel();
        try
        {
            await progressTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
    }

    private async Task WorkerAsync(int projectId, ProjectSettings settings, CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        httpClient.DefaultRequestHeaders.Add("User-Agent", settings.UserAgent);

        int emptyQueueCount = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            CrawlQueueItem? queueItem;
            
            using (var scope = _serviceProvider.CreateScope())
            {
                var queueRepository = scope.ServiceProvider.GetRequiredService<ICrawlQueueRepository>();
                queueItem = await queueRepository.GetNextItemAsync(projectId);
                
                // Update cached queue size
                if (queueItem != null)
                {
                    Interlocked.Decrement(ref _queueSize);
                }
            }
            
            if (queueItem == null)
            {
                // No more items in queue
                emptyQueueCount++;
                if (emptyQueueCount == 1)
                {
                    _logger.LogInformation("Worker found empty queue, waiting for items...");
                }
                else if (emptyQueueCount >= 5)
                {
                    _logger.LogWarning("Queue has been empty for {Count} attempts. Active workers: {Workers}, Crawled: {Crawled}", 
                        emptyQueueCount, _activeWorkers, _urlsCrawled);
                    if (_activeWorkers == 0)
                    {
                        _logger.LogInformation("No active workers and empty queue, stopping worker");
                        break; // Exit if no work being done
                    }
                }
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                continue;
            }

            emptyQueueCount = 0; // Reset counter when we find work
            _logger.LogDebug("Worker picked up URL: {Url} (Depth: {Depth})", queueItem.Address, queueItem.Depth);

            // Check if we've reached max URLs
            if (_urlsCrawled >= settings.MaxUrlsToCrawl)
            {
                break;
            }

            try
            {
                Interlocked.Increment(ref _activeWorkers);

                // Update queue item state
                using (var scope = _serviceProvider.CreateScope())
                {
                    var queueRepository = scope.ServiceProvider.GetRequiredService<ICrawlQueueRepository>();
                    queueItem.State = QueueState.InProgress;
                    await queueRepository.UpdateAsync(queueItem).ConfigureAwait(false);
                }

                // Enforce politeness delay
                await EnforcePolitenessDelayAsync(queueItem.HostKey, settings.CrawlDelaySeconds, cancellationToken).ConfigureAwait(false);

                // Check robots.txt
                if (settings.RespectRobotsTxt)
                {
                    var allowed = await _robotsService.IsAllowedAsync(queueItem.Address, settings.UserAgent).ConfigureAwait(false);
                    if (!allowed)
                    {
                        _logger.LogInformation("URL blocked by robots.txt: {Url}", queueItem.Address);
                        using (var scope = _serviceProvider.CreateScope())
                        {
                            var queueRepository = scope.ServiceProvider.GetRequiredService<ICrawlQueueRepository>();
                            queueItem.State = QueueState.Completed;
                            await queueRepository.UpdateAsync(queueItem).ConfigureAwait(false);
                        }
                        continue;
                    }
                }

                // Fetch URL with Playwright
                var (urlData, page, renderedHtml, redirectChain) = await FetchUrlWithPlaywrightAsync(queueItem.Address, cancellationToken).ConfigureAwait(false);

                try
                {
                    // Save URL to database
                    var urlEntity = await SaveUrlAsync(projectId, queueItem, urlData, renderedHtml).ConfigureAwait(false);

                    // Save redirect chain if present
                    if (redirectChain.Count > 0)
                    {
                        await SaveRedirectChainAsync(urlEntity.Id, redirectChain).ConfigureAwait(false);
                    }

                    // Execute plugin tasks
                    using (var pluginScope = _serviceProvider.CreateScope())
                    {
                        var pluginExecutor = pluginScope.ServiceProvider.GetRequiredService<PluginExecutor>();
                        var headers = urlData.Headers.GroupBy(h => h.Key).ToDictionary(g => g.Key, g => g.First().Value);
                        await pluginExecutor.ExecuteTasksAsync(urlEntity, page, renderedHtml, headers, settings, projectId, cancellationToken).ConfigureAwait(false);
                    }

                    // Extract and enqueue links if successful and within depth
                    if (urlData.IsSuccess && urlData.IsHtml && queueItem.Depth < settings.MaxCrawlDepth)
                    {
                        await ProcessLinksAsync(projectId, urlEntity, renderedHtml ?? "", queueItem.Address, queueItem.Depth, settings.BaseUrl).ConfigureAwait(false);
                    }

                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var queueRepository = scope.ServiceProvider.GetRequiredService<ICrawlQueueRepository>();
                        queueItem.State = QueueState.Completed;
                        await queueRepository.UpdateAsync(queueItem).ConfigureAwait(false);
                    }

                    Interlocked.Increment(ref _urlsCrawled);
                    
                    // Increment error counter if fetch failed
                    if (!urlData.IsSuccess)
                    {
                        Interlocked.Increment(ref _errorCount);
                    }
                    
                    // Update last crawled URL for progress display
                    _lastCrawledUrl = queueItem.Address;
                    _lastCrawledStatus = urlData.StatusCode;
                }
                finally
                {
                    // Clean up page and its context to prevent memory leaks
                    // This is the single place where pages are cleaned up
                    if (page != null)
                    {
                        try
                        {
                            await _playwrightService.ClosePageAsync(page).ConfigureAwait(false);
                        }
                        catch (Exception cleanupEx)
                        {
                            _logger.LogWarning(cleanupEx, "Error cleaning up page for {Url}", queueItem.Address);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error crawling {Url}", queueItem.Address);
                using (var scope = _serviceProvider.CreateScope())
                {
                    var queueRepository = scope.ServiceProvider.GetRequiredService<ICrawlQueueRepository>();
                    queueItem.State = QueueState.Failed;
                    await queueRepository.UpdateAsync(queueItem).ConfigureAwait(false);
                }
                Interlocked.Increment(ref _errorCount);
                
                // Update last crawled URL for progress display
                _lastCrawledUrl = queueItem.Address;
                _lastCrawledStatus = 0; // Indicate failure
            }
            finally
            {
                Interlocked.Decrement(ref _activeWorkers);
            }
        }
    }

    /// <summary>
    /// Fetches a URL using Playwright. 
    /// OWNERSHIP: The caller is ALWAYS responsible for disposing the returned page via ClosePageAsync(),
    /// even if an error occurs. The page is returned in both success and error cases.
    /// </summary>
    private async Task<(UrlFetchResult result, Microsoft.Playwright.IPage? page, string? html, List<RedirectHop> redirectChain)> FetchUrlWithPlaywrightAsync(string url, CancellationToken cancellationToken)
    {
        Microsoft.Playwright.IPage? page = null;
        string? renderedHtml = null;
        var redirectChain = new List<RedirectHop>();

        try
        {
            // Check cancellation before creating page
            cancellationToken.ThrowIfCancellationRequested();
            
            // Create a new page (caller becomes responsible for disposal from this point)
            page = await _playwrightService.CreatePageAsync();
            
            // Navigate to URL
            var response = await page.GotoAsync(url, new Microsoft.Playwright.PageGotoOptions
            {
                WaitUntil = Microsoft.Playwright.WaitUntilState.NetworkIdle,
                Timeout = 30000
            });

            if (response == null)
            {
                return (new UrlFetchResult
                {
                    StatusCode = 0,
                    IsSuccess = false,
                    ErrorMessage = "No response from page"
                }, page, null, redirectChain);
            }

            // Capture redirect chain
            redirectChain = ExtractRedirectChain(response);

            // Get rendered HTML
            renderedHtml = await page.ContentAsync();

            // Get headers
            var headers = await response.AllHeadersAsync();
            var headerList = headers.Select(h => new KeyValuePair<string, string>(h.Key, h.Value)).ToList();

            var result = new UrlFetchResult
            {
                StatusCode = response.Status,
                IsSuccess = response.Ok,
                ContentType = headers.ContainsKey("content-type") ? headers["content-type"] : null,
                Headers = headerList,
                IsHtml = true,
                Content = renderedHtml,
                RedirectTarget = redirectChain.Count > 0 ? redirectChain.Last().ToUrl : null
            };

            return (result, page, renderedHtml, redirectChain);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching URL with Playwright: {Url}", url);
            
            // Return error result WITH the page - caller owns disposal in all cases
            // This ensures single ownership and prevents double-disposal attempts
            return (new UrlFetchResult
            {
                StatusCode = 0,
                IsSuccess = false,
                ErrorMessage = ex.Message
            }, page, null, redirectChain);
        }
    }

    private List<RedirectHop> ExtractRedirectChain(Microsoft.Playwright.IResponse response)
    {
        var chain = new List<RedirectHop>();
        
        try
        {
            // Walk the redirect chain backwards
            var request = response.Request;
            var position = 0;
            
            // Build chain by following RedirectedFrom
            var requests = new List<Microsoft.Playwright.IRequest>();
            var currentRequest = request;
            
            while (currentRequest.RedirectedFrom != null)
            {
                requests.Insert(0, currentRequest.RedirectedFrom);
                currentRequest = currentRequest.RedirectedFrom;
            }
            
            // Now build the redirect chain
            for (int i = 0; i < requests.Count; i++)
            {
                var req = requests[i];
                var nextReq = i < requests.Count - 1 ? requests[i + 1] : request;
                
                chain.Add(new RedirectHop
                {
                    FromUrl = req.Url,
                    ToUrl = nextReq.Url,
                    StatusCode = 301, // We don't have exact status codes from the chain, so assume 301
                    Position = position++
                });
            }
            
            if (chain.Count > 0)
            {
                _logger.LogDebug("Captured redirect chain with {Count} hops: {FromUrl} -> {ToUrl}", 
                    chain.Count, chain.First().FromUrl, chain.Last().ToUrl);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extracting redirect chain");
        }
        
        return chain;
    }

    private async Task<UrlFetchResult> FetchUrlAsync(HttpClient httpClient, string url, CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.GetAsync(url, cancellationToken);
            
            var result = new UrlFetchResult
            {
                StatusCode = (int)response.StatusCode,
                IsSuccess = response.IsSuccessStatusCode,
                ContentType = response.Content.Headers.ContentType?.MediaType,
                ContentLength = response.Content.Headers.ContentLength
            };

            // Extract headers
            result.Headers = response.Headers
                .Concat(response.Content.Headers)
                .SelectMany(h => h.Value.Select(v => new KeyValuePair<string, string>(h.Key, v)))
                .ToList();

            // Only download content if it's HTML and successful
            if (result.IsSuccess && result.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true)
            {
                result.Content = await response.Content.ReadAsStringAsync(cancellationToken);
                result.IsHtml = true;
            }

            return result;
        }
        catch (HttpRequestException ex)
        {
            // Handle connection errors (DNS failure, connection refused, etc.)
            _logger.LogWarning("Connection error for {Url}: {Message}", url, ex.Message);
            return new UrlFetchResult
            {
                StatusCode = 0, // Use 0 to indicate connection failure
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout
            _logger.LogWarning("Timeout fetching {Url}", url);
            return new UrlFetchResult
            {
                StatusCode = 0,
                IsSuccess = false,
                ErrorMessage = "Request timeout"
            };
        }
    }

    private async Task<Url> SaveUrlAsync(int projectId, CrawlQueueItem queueItem, UrlFetchResult fetchResult, string? renderedHtml)
    {
        using var scope = _serviceProvider.CreateScope();
        var urlRepository = scope.ServiceProvider.GetRequiredService<IUrlRepository>();
        
        // Extract meta information from HTML
        var (title, metaDescription, canonical, metaRobots) = ExtractMetaFromHtml(renderedHtml);
        
        var existing = await urlRepository.GetByAddressAsync(projectId, queueItem.Address);
        if (existing != null)
        {
            existing.Status = fetchResult.IsSuccess ? UrlStatus.Completed : UrlStatus.Failed;
            existing.HttpStatus = fetchResult.StatusCode;
            existing.ContentType = fetchResult.ContentType;
            existing.ContentLength = fetchResult.ContentLength;
            existing.LastCrawledUtc = DateTime.UtcNow;
            existing.Title = title;
            existing.MetaDescription = metaDescription;
            existing.CanonicalUrl = canonical;
            existing.MetaRobots = metaRobots;
            existing.RedirectTarget = fetchResult.RedirectTarget;
            
            return await urlRepository.UpdateAsync(existing);
        }

        var uri = new Uri(queueItem.Address);
        var url = new Url
        {
            ProjectId = projectId,
            Address = queueItem.Address,
            NormalizedUrl = NormalizeUrl(queueItem.Address),
            Scheme = uri.Scheme,
            Host = uri.Host,
            Path = uri.PathAndQuery,
            Depth = queueItem.Depth,
            FirstSeenUtc = DateTime.UtcNow,
            LastCrawledUtc = DateTime.UtcNow,
            Status = fetchResult.IsSuccess ? UrlStatus.Completed : UrlStatus.Failed,
            HttpStatus = fetchResult.StatusCode,
            ContentType = fetchResult.ContentType,
            ContentLength = fetchResult.ContentLength,
            Title = title,
            MetaDescription = metaDescription,
            CanonicalUrl = canonical,
            MetaRobots = metaRobots,
            RedirectTarget = fetchResult.RedirectTarget
        };

        // Save headers
        foreach (var header in fetchResult.Headers)
        {
            url.Headers.Add(new Header
            {
                Name = header.Key,
                Value = header.Value
            });
        }

        return await urlRepository.CreateAsync(url);
    }

    private async Task SaveRedirectChainAsync(int urlId, List<RedirectHop> redirectChain)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var redirectRepository = scope.ServiceProvider.GetRequiredService<IRedirectRepository>();
            
            foreach (var hop in redirectChain)
            {
                var redirect = new Redirect
                {
                    UrlId = urlId,
                    FromUrl = hop.FromUrl,
                    ToUrl = hop.ToUrl,
                    StatusCode = hop.StatusCode,
                    Position = hop.Position
                };
                
                await redirectRepository.CreateAsync(redirect).ConfigureAwait(false);
            }
            
            _logger.LogDebug("Saved {Count} redirect hops for URL ID {UrlId}", redirectChain.Count, urlId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving redirect chain for URL ID {UrlId}", urlId);
        }
    }

    private (string? title, string? metaDescription, string? canonical, string? metaRobots) ExtractMetaFromHtml(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return (null, null, null, null);
        }

        try
        {
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);

            var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim();
            var metaDescription = doc.DocumentNode.SelectSingleNode("//meta[@name='description']")?.GetAttributeValue("content", "")?.Trim();
            var canonical = doc.DocumentNode.SelectSingleNode("//link[@rel='canonical']")?.GetAttributeValue("href", "")?.Trim();
            var metaRobots = doc.DocumentNode.SelectSingleNode("//meta[@name='robots']")?.GetAttributeValue("content", "")?.Trim();

            return (
                string.IsNullOrEmpty(title) ? null : title,
                string.IsNullOrEmpty(metaDescription) ? null : metaDescription,
                string.IsNullOrEmpty(canonical) ? null : canonical,
                string.IsNullOrEmpty(metaRobots) ? null : metaRobots
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extracting meta information from HTML");
            return (null, null, null, null);
        }
    }

    private async Task ProcessLinksAsync(int projectId, Url fromUrl, string htmlContent, string currentPageUrl, int currentDepth, string projectBaseUrl)
    {
        var extractedLinks = await _linkExtractor.ExtractLinksAsync(htmlContent, currentPageUrl);
        var projectBaseUri = new Uri(projectBaseUrl);

        foreach (var link in extractedLinks)
        {
            // Only follow hyperlinks to other pages
            if (link.LinkType == LinkType.Hyperlink)
            {
                // Only enqueue links from the same domain as the project's base URL
                if (Uri.TryCreate(link.Url, UriKind.Absolute, out var linkUri))
                {
                    if (IsSameDomain(projectBaseUri, linkUri))
                    {
                        await EnqueueUrlAsync(projectId, link.Url, currentDepth + 1, 100, projectBaseUrl).ConfigureAwait(false);
                    }
                    else
                    {
                        _logger.LogDebug("Skipping external link: {Url} (external domain: {FromDomain})", link.Url, linkUri.Host);
                    }
                }
            }

            // Save link relationships to Links table
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var urlRepository = scope.ServiceProvider.GetRequiredService<IUrlRepository>();
                var linkRepository = scope.ServiceProvider.GetRequiredService<ILinkRepository>();
                
                // Get or create the target URL
                var toUrl = await urlRepository.GetByAddressAsync(projectId, link.Url);
                if (toUrl == null)
                {
                    // URL hasn't been crawled yet, create a pending entry
                    var uri = new Uri(link.Url);
                    toUrl = new Url
                    {
                        ProjectId = projectId,
                        Address = link.Url,
                        NormalizedUrl = NormalizeUrl(link.Url),
                        Scheme = uri.Scheme,
                        Host = uri.Host,
                        Path = uri.PathAndQuery,
                        Depth = currentDepth + 1,
                        FirstSeenUtc = DateTime.UtcNow,
                        Status = UrlStatus.Pending,
                        DiscoveredFromUrlId = fromUrl.Id
                    };
                    toUrl = await urlRepository.CreateAsync(toUrl);
                }

                // Create link relationship
                var linkEntity = new Link
                {
                    ProjectId = projectId,
                    FromUrlId = fromUrl.Id,
                    ToUrlId = toUrl.Id,
                    AnchorText = link.AnchorText,
                    LinkType = link.LinkType
                };
                await linkRepository.CreateAsync(linkEntity).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save link from {FromUrl} to {ToUrl}", fromUrl.Address, link.Url);
            }
        }
    }

    private async Task EnqueueUrlAsync(int projectId, string url, int depth, int priority, string projectBaseUrl)
    {
        try
        {
            // Double-check domain filtering before enqueueing
            if (!Uri.TryCreate(url, UriKind.Absolute, out var urlUri))
            {
                _logger.LogDebug("Invalid URL format, skipping: {Url}", url);
                return;
            }

            var projectBaseUri = new Uri(projectBaseUrl);
            if (!IsSameDomain(projectBaseUri, urlUri))
            {
                _logger.LogDebug("URL from different domain, skipping: {Url} (expected: {BaseDomain})", url, projectBaseUri.Host);
                return;
            }

            // Skip binary and media files that shouldn't be crawled with a browser
            if (ShouldSkipBinaryFile(urlUri))
            {
                _logger.LogDebug("Skipping binary/media file: {Url}", url);
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var urlRepository = scope.ServiceProvider.GetRequiredService<IUrlRepository>();
            var queueRepository = scope.ServiceProvider.GetRequiredService<ICrawlQueueRepository>();
            
            // Check if URL already exists
            var existing = await urlRepository.GetByAddressAsync(projectId, url);
            if (existing != null)
            {
                _logger.LogDebug("URL already exists, skipping: {Url}", url);
                return;
            }

            var hostKey = urlUri.Host;

            var queueItem = new CrawlQueueItem
            {
                ProjectId = projectId,
                Address = url,
                Priority = priority,
                Depth = depth,
                HostKey = hostKey,
                EnqueuedUtc = DateTime.UtcNow,
                State = QueueState.Queued
            };

            await queueRepository.EnqueueAsync(queueItem).ConfigureAwait(false);
            Interlocked.Increment(ref _totalDiscovered);
            Interlocked.Increment(ref _queueSize);
            _logger.LogInformation("✓ Enqueued: {Url} (Depth: {Depth})", url, depth);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue URL: {Url}", url);
        }
    }

    private async Task EnforcePolitenessDelayAsync(string hostKey, double delaySeconds, CancellationToken cancellationToken)
    {
        if (_lastCrawlTime.TryGetValue(hostKey, out var lastTime))
        {
            var elapsed = DateTime.UtcNow - lastTime;
            var requiredDelay = TimeSpan.FromSeconds(delaySeconds);
            if (elapsed < requiredDelay)
            {
                var waitTime = requiredDelay - elapsed;
                await Task.Delay(waitTime, cancellationToken).ConfigureAwait(false);
            }
        }

        _lastCrawlTime[hostKey] = DateTime.UtcNow;
        
        // Clean up old entries periodically to prevent unbounded growth
        if (_lastCrawlTime.Count > 1000)
        {
            var oldEntries = _lastCrawlTime
                .Where(kvp => DateTime.UtcNow - kvp.Value > TimeSpan.FromMinutes(10))
                .Select(kvp => kvp.Key)
                .ToList();
            
            foreach (var key in oldEntries)
            {
                _lastCrawlTime.TryRemove(key, out _);
            }
        }
    }

    private async Task ReportProgressAsync(int projectId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                SendProgressUpdate(projectId);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error reporting progress");
            }
        }
    }

    private void SendProgressUpdate(int projectId)
    {
        try
        {
            // Check memory usage and restart browser if needed
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var memoryMB = process.PrivateMemorySize64 / 1024 / 1024;
            
            // Get memory limit from settings (default 1536 MB)
            var memoryLimit = 1536; // Default, will be overridden if settings are available
            
            if (memoryMB > memoryLimit)
            {
                _logger.LogWarning("Memory usage is high: {MemoryMB} MB (threshold: {Threshold} MB)", memoryMB, memoryLimit);
                
                // STAGE 2 LIMITATION: No automatic browser restart during crawl
                // Restarting the browser mid-crawl is complex and could cause data loss or crashes:
                // - Active pages would need to be tracked and coordinated
                // - In-progress plugin tasks could fail
                // - Page disposal race conditions could occur
                // 
                // MITIGATION: 
                // - Limit concurrent pages to 2 (reduces memory pressure)
                // - Aggressive page disposal after each URL completes
                // - Memory warning logged for user awareness
                // 
                // WORKAROUND FOR USERS:
                // If crawling very large sites (>1000 URLs), consider:
                // 1. Reducing max URLs per crawl session
                // 2. Crawling in batches
                // 3. Restarting the application between large crawls
                //
                // TODO STAGE 3: Implement graceful browser restart with pause/resume coordination
                // This requires the full pause/resume infrastructure from Stage 3
                
                _logger.LogWarning("Consider stopping the crawl and restarting the application if memory usage becomes problematic");
            }

            var args = new CrawlProgressEventArgs
            {
                UrlsCrawled = _urlsCrawled,
                TotalDiscovered = _totalDiscovered,
                QueueSize = _queueSize, // Use cached queue size instead of database query
                ActiveWorkers = _activeWorkers,
                ErrorCount = _errorCount,
                Elapsed = _stopwatch?.Elapsed ?? TimeSpan.Zero,
                LastCrawledUrl = _lastCrawledUrl != null 
                    ? $"{(_lastCrawledStatus >= 200 && _lastCrawledStatus < 300 ? "✓" : "✗")} {_lastCrawledUrl} ({GetStatusDescription(_lastCrawledStatus)})"
                    : null
            };

            ProgressUpdated?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error sending progress update");
        }
    }

    private static string NormalizeUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.GetLeftPart(UriPartial.Path).ToLowerInvariant();
        }
        catch
        {
            return url.ToLowerInvariant();
        }
    }

    private static string GetStatusDescription(int statusCode)
    {
        return statusCode switch
        {
            0 => "Connection Failed",
            200 => "200 OK",
            201 => "201 Created",
            204 => "204 No Content",
            301 => "301 Moved Permanently",
            302 => "302 Found",
            304 => "304 Not Modified",
            400 => "400 Bad Request",
            401 => "401 Unauthorized",
            403 => "403 Forbidden",
            404 => "404 Not Found",
            500 => "500 Internal Server Error",
            502 => "502 Bad Gateway",
            503 => "503 Service Unavailable",
            _ => $"{statusCode}"
        };
    }

    private static bool IsSameDomain(Uri baseUri, Uri targetUri)
    {
        // Compare host names (including subdomains)
        // Allow www and non-www to be considered the same domain
        var baseHost = baseUri.Host.ToLowerInvariant();
        var targetHost = targetUri.Host.ToLowerInvariant();

        // Remove www. prefix for comparison
        var baseHostWithoutWww = baseHost.StartsWith("www.") ? baseHost.Substring(4) : baseHost;
        var targetHostWithoutWww = targetHost.StartsWith("www.") ? targetHost.Substring(4) : targetHost;

        return baseHostWithoutWww == targetHostWithoutWww;
    }

    private static readonly string[] BinaryFileExtensions =
    [
        // Video files
        ".mp4", ".avi", ".mov", ".wmv", ".flv", ".mkv", ".webm", ".m4v", ".mpg", ".mpeg",
        // Audio files
        ".mp3", ".wav", ".ogg", ".m4a", ".aac", ".flac", ".wma",
        // Documents
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        // Archives
        ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2",
        // Images (large formats that are typically not pages)
        ".psd", ".ai", ".svg",
        // Executables and binaries
        ".exe", ".dll", ".so", ".dylib", ".bin", ".dmg", ".iso",
        // Fonts
        ".ttf", ".otf", ".woff", ".woff2", ".eot"
    ];

    private static bool ShouldSkipBinaryFile(Uri uri)
    {
        var path = uri.AbsolutePath.ToLowerInvariant();
        return BinaryFileExtensions.Any(ext => path.EndsWith(ext));
    }

    private class UrlFetchResult
    {
        public int StatusCode { get; set; }
        public bool IsSuccess { get; set; }
        public string? ContentType { get; set; }
        public long? ContentLength { get; set; }
        public string? Content { get; set; }
        public bool IsHtml { get; set; }
        public string? ErrorMessage { get; set; }
        public List<KeyValuePair<string, string>> Headers { get; set; } = [];
        public string? RedirectTarget { get; set; }
    }

    private class RedirectHop
    {
        public string FromUrl { get; set; } = string.Empty;
        public string ToUrl { get; set; } = string.Empty;
        public int StatusCode { get; set; }
        public int Position { get; set; }
    }
}

