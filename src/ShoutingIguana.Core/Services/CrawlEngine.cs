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
    IPlaywrightService playwrightService) : ICrawlEngine
{
    private readonly ILogger<CrawlEngine> _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IRobotsService _robotsService = robotsService;
    private readonly ILinkExtractor _linkExtractor = linkExtractor;
    private readonly IPlaywrightService _playwrightService = playwrightService;
    
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
            project = await projectRepository.GetByIdAsync(projectId).ConfigureAwait(false);
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
                
                // Discover and enqueue URLs from sitemap.xml if enabled
                if (settings.UseSitemapXml)
                {
                    _logger.LogInformation("Sitemap discovery enabled, searching for sitemaps...");
                    var sitemapService = scope.ServiceProvider.GetRequiredService<ISitemapService>();
                    var sitemapUrls = await sitemapService.DiscoverSitemapUrlsAsync(settings.BaseUrl).ConfigureAwait(false);
                    
                    if (sitemapUrls.Any())
                    {
                        _logger.LogInformation("Discovered {Count} URLs from sitemap(s), enqueueing...", sitemapUrls.Count);
                        int enqueuedCount = 0;
                        
                        foreach (var url in sitemapUrls)
                        {
                            await EnqueueUrlAsync(projectId, url, 0, 900, settings.BaseUrl).ConfigureAwait(false);
                            enqueuedCount++;
                        }
                        
                        _logger.LogInformation("Enqueued {Count} URLs from sitemap discovery", enqueuedCount);
                    }
                    else
                    {
                        _logger.LogInformation("No sitemap URLs discovered");
                    }
                }
            }
            else
            {
                // Queue is not empty - this is a resume from a previous crawl
                // Initialize counters from database to reflect existing state
                _totalDiscovered = await queueRepository.CountQueuedAsync(projectId).ConfigureAwait(false);
                _queueSize = _totalDiscovered;
                _logger.LogInformation("Resuming crawl with {QueueSize} URLs already in queue", _queueSize);
            }
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
        int emptyQueueCount = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            CrawlQueueItem? queueItem;
            
            using (var scope = _serviceProvider.CreateScope())
            {
                var queueRepository = scope.ServiceProvider.GetRequiredService<ICrawlQueueRepository>();
                queueItem = await queueRepository.GetNextItemAsync(projectId).ConfigureAwait(false);
                
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

                // Get user agent for this request
                var userAgent = settings.GetUserAgentString();

                // Check robots.txt
                bool? robotsAllowed = null;
                if (settings.RespectRobotsTxt)
                {
                    var allowed = await _robotsService.IsAllowedAsync(queueItem.Address, userAgent).ConfigureAwait(false);
                    robotsAllowed = allowed;
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
                var (urlData, page, renderedHtml, redirectChain) = await FetchUrlWithPlaywrightAsync(queueItem.Address, userAgent, cancellationToken).ConfigureAwait(false);

                try
                {
                    // Save URL to database
                    var urlEntity = await SaveUrlAsync(projectId, queueItem, urlData, renderedHtml, robotsAllowed).ConfigureAwait(false);

                    // Save redirect chain if present
                    if (redirectChain.Count > 0)
                    {
                        await SaveRedirectChainAsync(urlEntity.Id, redirectChain).ConfigureAwait(false);
                    }

                    // Execute plugin tasks
                    using (var pluginScope = _serviceProvider.CreateScope())
                    {
                        var pluginExecutor = pluginScope.ServiceProvider.GetRequiredService<PluginExecutor>();
                        var headers = urlData.Headers.GroupBy(h => h.Key.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First().Value);
                        await pluginExecutor.ExecuteTasksAsync(urlEntity, page, renderedHtml, headers, settings, userAgent, projectId, cancellationToken).ConfigureAwait(false);
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
    private async Task<(UrlFetchResult result, Microsoft.Playwright.IPage? page, string? html, List<RedirectHop> redirectChain)> FetchUrlWithPlaywrightAsync(string url, string userAgent, CancellationToken cancellationToken)
    {
        Microsoft.Playwright.IPage? page = null;
        string? renderedHtml = null;
        var redirectChain = new List<RedirectHop>();

        try
        {
            // Check cancellation before creating page
            cancellationToken.ThrowIfCancellationRequested();
            
            // Create a new page with the specified user agent (caller becomes responsible for disposal from this point)
            page = await _playwrightService.CreatePageAsync(userAgent).ConfigureAwait(false);
            
            // Navigate to URL
            var response = await page.GotoAsync(url, new Microsoft.Playwright.PageGotoOptions
            {
                WaitUntil = Microsoft.Playwright.WaitUntilState.NetworkIdle,
                Timeout = 30000
            }).ConfigureAwait(false);

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
            renderedHtml = await page.ContentAsync().ConfigureAwait(false);

            // Get headers
            var headers = await response.AllHeadersAsync().ConfigureAwait(false);
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

    private async Task<Url> SaveUrlAsync(int projectId, CrawlQueueItem queueItem, UrlFetchResult fetchResult, string? renderedHtml, bool? robotsAllowed)
    {
        using var scope = _serviceProvider.CreateScope();
        var urlRepository = scope.ServiceProvider.GetRequiredService<IUrlRepository>();
        var hreflangRepository = scope.ServiceProvider.GetRequiredService<IHreflangRepository>();
        var structuredDataRepository = scope.ServiceProvider.GetRequiredService<IStructuredDataRepository>();
        
        // Extract meta information from HTML with headers
        var headers = fetchResult.Headers.GroupBy(h => h.Key.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First().Value);
        var meta = ExtractMetaFromHtml(renderedHtml, headers, queueItem.Address);
        
        var existing = await urlRepository.GetByAddressAsync(projectId, queueItem.Address).ConfigureAwait(false);
        if (existing != null)
        {
            existing.Status = fetchResult.IsSuccess ? UrlStatus.Completed : UrlStatus.Failed;
            existing.HttpStatus = fetchResult.StatusCode;
            existing.ContentType = fetchResult.ContentType;
            existing.ContentLength = fetchResult.ContentLength;
            existing.LastCrawledUtc = DateTime.UtcNow;
            existing.RobotsAllowed = robotsAllowed;
            
            // Basic meta
            existing.Title = meta.Title;
            existing.MetaDescription = meta.MetaDescription;
            existing.CanonicalUrl = meta.CanonicalUrl;
            existing.MetaRobots = meta.MetaRobots;
            existing.RedirectTarget = fetchResult.RedirectTarget;
            
            // Enhanced canonical
            existing.CanonicalHtml = meta.CanonicalHtml;
            existing.CanonicalHttp = meta.CanonicalHttp;
            existing.HasMultipleCanonicals = meta.HasMultipleCanonicals;
            existing.HasCrossDomainCanonical = meta.HasCrossDomainCanonical;
            existing.CanonicalIssues = meta.CanonicalIssues;
            
            // Parsed robots
            existing.RobotsNoindex = meta.RobotsNoindex;
            existing.RobotsNofollow = meta.RobotsNofollow;
            existing.RobotsNoarchive = meta.RobotsNoarchive;
            existing.RobotsNosnippet = meta.RobotsNosnippet;
            existing.RobotsNoimageindex = meta.RobotsNoimageindex;
            existing.RobotsSource = meta.RobotsSource;
            existing.XRobotsTag = meta.XRobotsTag;
            existing.HasRobotsConflict = meta.HasRobotsConflict;
            
            // Language
            existing.HtmlLang = meta.HtmlLang;
            existing.ContentLanguageHeader = meta.ContentLanguageHeader;
            
            // Meta refresh
            existing.HasMetaRefresh = meta.HasMetaRefresh;
            existing.MetaRefreshDelay = meta.MetaRefreshDelay;
            existing.MetaRefreshTarget = meta.MetaRefreshTarget;
            
            // HTTP headers
            existing.CacheControl = meta.CacheControl;
            existing.Vary = meta.Vary;
            existing.ContentEncoding = meta.ContentEncoding;
            existing.LinkHeader = meta.LinkHeader;
            existing.HasHsts = meta.HasHsts;
            
            var updated = await urlRepository.UpdateAsync(existing).ConfigureAwait(false);
            
            // Delete and recreate hreflangs and structured data
            await hreflangRepository.DeleteByUrlIdAsync(updated.Id).ConfigureAwait(false);
            await structuredDataRepository.DeleteByUrlIdAsync(updated.Id).ConfigureAwait(false);
            
            await SaveHreflangsAsync(updated.Id, meta.Hreflangs, hreflangRepository).ConfigureAwait(false);
            await SaveStructuredDataAsync(updated.Id, meta.StructuredData, structuredDataRepository).ConfigureAwait(false);
            
            return updated;
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
            RobotsAllowed = robotsAllowed,
            
            // Basic meta
            Title = meta.Title,
            MetaDescription = meta.MetaDescription,
            CanonicalUrl = meta.CanonicalUrl,
            MetaRobots = meta.MetaRobots,
            RedirectTarget = fetchResult.RedirectTarget,
            
            // Enhanced canonical
            CanonicalHtml = meta.CanonicalHtml,
            CanonicalHttp = meta.CanonicalHttp,
            HasMultipleCanonicals = meta.HasMultipleCanonicals,
            HasCrossDomainCanonical = meta.HasCrossDomainCanonical,
            CanonicalIssues = meta.CanonicalIssues,
            
            // Parsed robots
            RobotsNoindex = meta.RobotsNoindex,
            RobotsNofollow = meta.RobotsNofollow,
            RobotsNoarchive = meta.RobotsNoarchive,
            RobotsNosnippet = meta.RobotsNosnippet,
            RobotsNoimageindex = meta.RobotsNoimageindex,
            RobotsSource = meta.RobotsSource,
            XRobotsTag = meta.XRobotsTag,
            HasRobotsConflict = meta.HasRobotsConflict,
            
            // Language
            HtmlLang = meta.HtmlLang,
            ContentLanguageHeader = meta.ContentLanguageHeader,
            
            // Meta refresh
            HasMetaRefresh = meta.HasMetaRefresh,
            MetaRefreshDelay = meta.MetaRefreshDelay,
            MetaRefreshTarget = meta.MetaRefreshTarget,
            
            // HTTP headers
            CacheControl = meta.CacheControl,
            Vary = meta.Vary,
            ContentEncoding = meta.ContentEncoding,
            LinkHeader = meta.LinkHeader,
            HasHsts = meta.HasHsts
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

        var created = await urlRepository.CreateAsync(url).ConfigureAwait(false);
        
        // Save hreflangs and structured data
        await SaveHreflangsAsync(created.Id, meta.Hreflangs, hreflangRepository).ConfigureAwait(false);
        await SaveStructuredDataAsync(created.Id, meta.StructuredData, structuredDataRepository).ConfigureAwait(false);
        
        return created;
    }
    
    private async Task SaveHreflangsAsync(int urlId, List<HreflangData> hreflangs, IHreflangRepository repository)
    {
        if (hreflangs.Count == 0) return;
        
        var entities = hreflangs.Select(h => new Hreflang
        {
            UrlId = urlId,
            LanguageCode = h.LanguageCode,
            TargetUrl = h.TargetUrl,
            Source = h.Source,
            IsXDefault = h.IsXDefault
        }).ToList();
        
        await repository.CreateBatchAsync(entities).ConfigureAwait(false);
    }
    
    private async Task SaveStructuredDataAsync(int urlId, List<StructuredDataInfo> structuredData, IStructuredDataRepository repository)
    {
        if (structuredData.Count == 0) return;
        
        var entities = structuredData.Select(sd => new StructuredData
        {
            UrlId = urlId,
            Type = sd.Type,
            SchemaType = sd.SchemaType,
            RawData = sd.RawData,
            IsValid = sd.IsValid,
            ValidationErrors = sd.ValidationErrors
        }).ToList();
        
        await repository.CreateBatchAsync(entities).ConfigureAwait(false);
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

    private EnhancedMetaData ExtractMetaFromHtml(string? html, Dictionary<string, string> headers, string currentUrl)
    {
        var result = new EnhancedMetaData();
        
        if (string.IsNullOrEmpty(html))
        {
            return result;
        }

        try
        {
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);

            // Basic meta
            result.Title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim();
            result.MetaDescription = doc.DocumentNode.SelectSingleNode("//meta[@name='description']")?.GetAttributeValue("content", "")?.Trim();
            
            // Enhanced canonical detection
            ExtractCanonicals(doc, headers, currentUrl, result);
            
            // Parse robots directives
            ParseRobotsDirectives(doc, headers, result);
            
            // Language attributes
            result.HtmlLang = doc.DocumentNode.SelectSingleNode("//html")?.GetAttributeValue("lang", "")?.Trim();
            if (headers.TryGetValue("content-language", out var contentLang))
            {
                result.ContentLanguageHeader = contentLang;
            }
            
            // Meta refresh
            ParseMetaRefresh(doc, result);
            
            // Extract HTTP headers
            ExtractSpecialHeaders(headers, result);
            
            // Extract hreflangs
            result.Hreflangs = ExtractHreflangs(doc, headers, currentUrl);
            
            // Extract structured data
            result.StructuredData = ExtractStructuredData(doc);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extracting meta information from HTML");
            return result;
        }
    }
    
    private void ExtractCanonicals(HtmlAgilityPack.HtmlDocument doc, Dictionary<string, string> headers, string currentUrl, EnhancedMetaData result)
    {
        // Extract all HTML canonicals
        var canonicalNodes = doc.DocumentNode.SelectNodes("//link[@rel='canonical']");
        if (canonicalNodes != null)
        {
            result.CanonicalHtml = canonicalNodes.First()?.GetAttributeValue("href", "")?.Trim();
            result.HasMultipleCanonicals = canonicalNodes.Count > 1;
            
            if (!string.IsNullOrEmpty(result.CanonicalHtml))
            {
                result.CanonicalHtml = ResolveUrl(result.CanonicalHtml, currentUrl);
                
                // Check if cross-domain
                try
                {
                    var currentUri = new Uri(currentUrl);
                    var canonicalUri = new Uri(result.CanonicalHtml);
                    result.HasCrossDomainCanonical = !string.Equals(currentUri.Host, canonicalUri.Host, StringComparison.OrdinalIgnoreCase);
                }
                catch (UriFormatException)
                {
                    // Invalid URI format, skip cross-domain check
                }
            }
        }
        
        // Extract HTTP Link header canonical
        if (headers.TryGetValue("link", out var linkHeader))
        {
            result.LinkHeader = linkHeader;
            var canonicalMatch = System.Text.RegularExpressions.Regex.Match(linkHeader, @"<([^>]+)>;\s*rel=[""']?canonical[""']?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (canonicalMatch.Success)
            {
                result.CanonicalHttp = ResolveUrl(canonicalMatch.Groups[1].Value, currentUrl);
            }
        }
        
        // Validate issues
        var issues = new List<string>();
        if (result.HasMultipleCanonicals)
        {
            issues.Add("Multiple canonical tags in HTML");
        }
        if (!string.IsNullOrEmpty(result.CanonicalHtml) && !string.IsNullOrEmpty(result.CanonicalHttp) && 
            !string.Equals(result.CanonicalHtml, result.CanonicalHttp, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("HTML and HTTP canonical differ");
        }
        
        if (issues.Any())
        {
            result.CanonicalIssues = System.Text.Json.JsonSerializer.Serialize(issues);
        }
        
        // Set deprecated field for backward compatibility
        result.CanonicalUrl = result.CanonicalHtml ?? result.CanonicalHttp;
    }
    
    private void ParseRobotsDirectives(HtmlAgilityPack.HtmlDocument doc, Dictionary<string, string> headers, EnhancedMetaData result)
    {
        var metaRobots = doc.DocumentNode.SelectSingleNode("//meta[@name='robots']")?.GetAttributeValue("content", "")?.Trim();
        var xRobotsTag = headers.TryGetValue("x-robots-tag", out var xrt) ? xrt : null;
        
        result.MetaRobots = metaRobots; // Keep for backward compatibility
        result.XRobotsTag = xRobotsTag;
        
        // Parse directives from both sources
        var metaDirectives = ParseRobotsString(metaRobots);
        var httpDirectives = ParseRobotsString(xRobotsTag);
        
        // Apply conflict resolution: most restrictive wins
        result.RobotsNoindex = CombineRobotsFlags(metaDirectives.Noindex, httpDirectives.Noindex);
        result.RobotsNofollow = CombineRobotsFlags(metaDirectives.Nofollow, httpDirectives.Nofollow);
        result.RobotsNoarchive = CombineRobotsFlags(metaDirectives.Noarchive, httpDirectives.Noarchive);
        result.RobotsNosnippet = CombineRobotsFlags(metaDirectives.Nosnippet, httpDirectives.Nosnippet);
        result.RobotsNoimageindex = CombineRobotsFlags(metaDirectives.Noimageindex, httpDirectives.Noimageindex);
        
        // Determine source
        if (!string.IsNullOrEmpty(metaRobots) && !string.IsNullOrEmpty(xRobotsTag))
        {
            result.RobotsSource = "both";
            result.HasRobotsConflict = HasRobotsConflict(metaDirectives, httpDirectives);
        }
        else if (!string.IsNullOrEmpty(metaRobots))
        {
            result.RobotsSource = "meta";
        }
        else if (!string.IsNullOrEmpty(xRobotsTag))
        {
            result.RobotsSource = "http";
        }
    }
    
    private RobotsDirectives ParseRobotsString(string? robotsString)
    {
        var directives = new RobotsDirectives();
        if (string.IsNullOrEmpty(robotsString)) return directives;
        
        var lower = robotsString.ToLowerInvariant();
        
        // Handle special "none" directive (equivalent to "noindex, nofollow")
        // Use word boundary check to avoid false positives
        if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"\bnone\b"))
        {
            directives.Noindex = true;
            directives.Nofollow = true;
            return directives;
        }
        
        // Handle individual directives (use word boundaries for precision)
        directives.Noindex = System.Text.RegularExpressions.Regex.IsMatch(lower, @"\bnoindex\b");
        directives.Nofollow = System.Text.RegularExpressions.Regex.IsMatch(lower, @"\bnofollow\b");
        directives.Noarchive = System.Text.RegularExpressions.Regex.IsMatch(lower, @"\bnoarchive\b");
        directives.Nosnippet = System.Text.RegularExpressions.Regex.IsMatch(lower, @"\bnosnippet\b");
        directives.Noimageindex = System.Text.RegularExpressions.Regex.IsMatch(lower, @"\bnoimageindex\b");
        
        return directives;
    }
    
    private bool? CombineRobotsFlags(bool metaValue, bool httpValue)
    {
        // Most restrictive wins: if either says no, it's no
        if (metaValue || httpValue) return true;
        if (!metaValue && !httpValue) return false;
        return null;
    }
    
    private bool HasRobotsConflict(RobotsDirectives meta, RobotsDirectives http)
    {
        return meta.Noindex != http.Noindex ||
               meta.Nofollow != http.Nofollow ||
               meta.Noarchive != http.Noarchive ||
               meta.Nosnippet != http.Nosnippet ||
               meta.Noimageindex != http.Noimageindex;
    }
    
    private void ParseMetaRefresh(HtmlAgilityPack.HtmlDocument doc, EnhancedMetaData result)
    {
        var metaRefresh = doc.DocumentNode.SelectSingleNode("//meta[@http-equiv='refresh']");
        if (metaRefresh != null)
        {
            var content = metaRefresh.GetAttributeValue("content", "");
            var match = System.Text.RegularExpressions.Regex.Match(content, @"(\d+)\s*;\s*url=(.+?)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var delay))
            {
                result.HasMetaRefresh = true;
                result.MetaRefreshDelay = delay;
                result.MetaRefreshTarget = match.Groups[2].Value.Trim();
            }
        }
    }
    
    private void ExtractSpecialHeaders(Dictionary<string, string> headers, EnhancedMetaData result)
    {
        if (headers.TryGetValue("cache-control", out var cacheControl))
            result.CacheControl = cacheControl;
        
        if (headers.TryGetValue("vary", out var vary))
            result.Vary = vary;
        
        if (headers.TryGetValue("content-encoding", out var encoding))
            result.ContentEncoding = encoding;
        
        if (headers.TryGetValue("strict-transport-security", out var hsts))
            result.HasHsts = true;
    }
    
    private List<HreflangData> ExtractHreflangs(HtmlAgilityPack.HtmlDocument doc, Dictionary<string, string> headers, string currentUrl)
    {
        var hreflangs = new List<HreflangData>();
        
        // Extract from HTML
        var hreflangNodes = doc.DocumentNode.SelectNodes("//link[@rel='alternate'][@hreflang]");
        if (hreflangNodes != null)
        {
            foreach (var node in hreflangNodes)
            {
                var lang = node.GetAttributeValue("hreflang", "");
                var href = node.GetAttributeValue("href", "");
                if (!string.IsNullOrEmpty(lang) && !string.IsNullOrEmpty(href))
                {
                    hreflangs.Add(new HreflangData
                    {
                        LanguageCode = lang,
                        TargetUrl = ResolveUrl(href, currentUrl),
                        Source = "html",
                        IsXDefault = lang.Equals("x-default", StringComparison.OrdinalIgnoreCase)
                    });
                }
            }
        }
        
        // Extract from HTTP Link header
        if (headers.TryGetValue("link", out var linkHeader))
        {
            var hreflangMatches = System.Text.RegularExpressions.Regex.Matches(linkHeader, 
                @"<([^>]+)>;\s*rel=[""']?alternate[""']?;\s*hreflang=[""']?([^""';,\s]+)[""']?", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            foreach (System.Text.RegularExpressions.Match match in hreflangMatches)
            {
                hreflangs.Add(new HreflangData
                {
                    LanguageCode = match.Groups[2].Value,
                    TargetUrl = ResolveUrl(match.Groups[1].Value, currentUrl),
                    Source = "http",
                    IsXDefault = match.Groups[2].Value.Equals("x-default", StringComparison.OrdinalIgnoreCase)
                });
            }
        }
        
        return hreflangs;
    }
    
    private List<StructuredDataInfo> ExtractStructuredData(HtmlAgilityPack.HtmlDocument doc)
    {
        var result = new List<StructuredDataInfo>();
        
        // Extract JSON-LD
        var jsonLdNodes = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (jsonLdNodes != null)
        {
            foreach (var node in jsonLdNodes)
            {
                var json = node.InnerText?.Trim();
                if (!string.IsNullOrEmpty(json))
                {
                    try
                    {
                        using var jsonDoc = System.Text.Json.JsonDocument.Parse(json);
                        var type = jsonDoc.RootElement.TryGetProperty("@type", out var typeEl) ? typeEl.GetString() : "Unknown";
                        
                        result.Add(new StructuredDataInfo
                        {
                            Type = "json-ld",
                            SchemaType = type ?? "Unknown",
                            RawData = json,
                            IsValid = true
                        });
                    }
                    catch (System.Text.Json.JsonException)
                    {
                        result.Add(new StructuredDataInfo
                        {
                            Type = "json-ld",
                            SchemaType = "Unknown",
                            RawData = json,
                            IsValid = false,
                            ValidationErrors = "Invalid JSON"
                        });
                    }
                }
            }
        }
        
        return result;
    }
    
    private string ResolveUrl(string url, string baseUrl)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var absoluteUri))
            {
                return absoluteUri.ToString();
            }
            
            if (Uri.TryCreate(new Uri(baseUrl), url, out var resolvedUri))
            {
                return resolvedUri.ToString();
            }
            
            return url;
        }
        catch (UriFormatException)
        {
            // Invalid URI format, return as-is
            return url;
        }
    }

    private async Task ProcessLinksAsync(int projectId, Url fromUrl, string htmlContent, string currentPageUrl, int currentDepth, string projectBaseUrl)
    {
        var extractedLinks = await _linkExtractor.ExtractLinksAsync(htmlContent, currentPageUrl).ConfigureAwait(false);
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
                var toUrl = await urlRepository.GetByAddressAsync(projectId, link.Url).ConfigureAwait(false);
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
                    toUrl = await urlRepository.CreateAsync(toUrl).ConfigureAwait(false);
                }

                // Parse rel attribute
                var rel = link.RelAttribute ?? string.Empty;
                var isNofollow = rel.Contains("nofollow", StringComparison.OrdinalIgnoreCase);
                var isUgc = rel.Contains("ugc", StringComparison.OrdinalIgnoreCase);
                var isSponsored = rel.Contains("sponsored", StringComparison.OrdinalIgnoreCase);
                
                // Create link relationship
                var linkEntity = new Link
                {
                    ProjectId = projectId,
                    FromUrlId = fromUrl.Id,
                    ToUrlId = toUrl.Id,
                    AnchorText = link.AnchorText,
                    LinkType = link.LinkType,
                    RelAttribute = link.RelAttribute,
                    IsNofollow = isNofollow,
                    IsUgc = isUgc,
                    IsSponsored = isSponsored
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
            var existing = await urlRepository.GetByAddressAsync(projectId, url).ConfigureAwait(false);
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
    
    private class EnhancedMetaData
    {
        public string? Title { get; set; }
        public string? MetaDescription { get; set; }
        public string? CanonicalUrl { get; set; } // Deprecated
        public string? MetaRobots { get; set; } // Deprecated
        
        // Enhanced canonical
        public string? CanonicalHtml { get; set; }
        public string? CanonicalHttp { get; set; }
        public bool HasMultipleCanonicals { get; set; }
        public bool HasCrossDomainCanonical { get; set; }
        public string? CanonicalIssues { get; set; }
        
        // Parsed robots
        public bool? RobotsNoindex { get; set; }
        public bool? RobotsNofollow { get; set; }
        public bool? RobotsNoarchive { get; set; }
        public bool? RobotsNosnippet { get; set; }
        public bool? RobotsNoimageindex { get; set; }
        public string? RobotsSource { get; set; }
        public string? XRobotsTag { get; set; }
        public bool HasRobotsConflict { get; set; }
        
        // Language
        public string? HtmlLang { get; set; }
        public string? ContentLanguageHeader { get; set; }
        
        // Meta refresh
        public bool HasMetaRefresh { get; set; }
        public int? MetaRefreshDelay { get; set; }
        public string? MetaRefreshTarget { get; set; }
        
        // HTTP headers
        public string? CacheControl { get; set; }
        public string? Vary { get; set; }
        public string? ContentEncoding { get; set; }
        public string? LinkHeader { get; set; }
        public bool HasHsts { get; set; }
        
        // Collections
        public List<HreflangData> Hreflangs { get; set; } = new();
        public List<StructuredDataInfo> StructuredData { get; set; } = new();
    }
    
    private class RobotsDirectives
    {
        public bool Noindex { get; set; }
        public bool Nofollow { get; set; }
        public bool Noarchive { get; set; }
        public bool Nosnippet { get; set; }
        public bool Noimageindex { get; set; }
    }
    
    private class HreflangData
    {
        public string LanguageCode { get; set; } = string.Empty;
        public string TargetUrl { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public bool IsXDefault { get; set; }
    }
    
    private class StructuredDataInfo
    {
        public string Type { get; set; } = string.Empty;
        public string SchemaType { get; set; } = string.Empty;
        public string RawData { get; set; } = string.Empty;
        public bool IsValid { get; set; }
        public string? ValidationErrors { get; set; }
    }
}

