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
    private readonly ManualResetEventSlim _pauseEvent = new(initialState: true); // Initially not paused
    
    // Performance counters
    private int _urlsCrawled;
    private int _urlsAnalyzed;
    private int _totalDiscovered;
    private int _activeWorkers;
    private int _errorCount;
    private int _queueSize; // Cached queue size
    private CrawlPhase _currentPhase;
    private Stopwatch? _stopwatch;
    private TimeSpan _pausedTime = TimeSpan.Zero;
    private DateTime? _pauseStartTime;
    private string? _lastCrawledUrl;
    private int _lastCrawledStatus;
    private int _currentProjectId;
    private int _isPaused; // Use int for thread-safe access (0 = false, 1 = true)
    private int _isCrawling; // Use int for thread-safe access (0 = false, 1 = true)
    
    // Adaptive page loading strategy
    private int _networkIdleSuccessCount;
    private int _networkIdleFailureCount;
    private int _useFastLoadingMode; // 0 = false, 1 = true (thread-safe)

    public bool IsCrawling => Interlocked.CompareExchange(ref _isCrawling, 0, 0) == 1;
    public bool IsPaused => Interlocked.CompareExchange(ref _isPaused, 0, 0) == 1;
    public event EventHandler<CrawlProgressEventArgs>? ProgressUpdated;

    public Task StartCrawlAsync(int projectId, bool resumeFromCheckpoint = false, CancellationToken cancellationToken = default)
    {
        if (IsCrawling)
        {
            _logger.LogWarning("Crawl is already running");
            return Task.CompletedTask;
        }

        Interlocked.Exchange(ref _isCrawling, 1);
        Interlocked.Exchange(ref _isPaused, 0);
        _currentProjectId = projectId;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _urlsCrawled = 0;
        _urlsAnalyzed = 0;
        _totalDiscovered = 0;
        _activeWorkers = 0;
        _errorCount = 0;
        _queueSize = 0;
        _currentPhase = CrawlPhase.Discovery;
        _pausedTime = TimeSpan.Zero;
        
        // Reset adaptive loading strategy for new crawl
        _networkIdleSuccessCount = 0;
        _networkIdleFailureCount = 0;
        Interlocked.Exchange(ref _useFastLoadingMode, 0);
        _pauseStartTime = null;
        _pauseEvent.Set(); // Ensure not paused
        _stopwatch = Stopwatch.StartNew();

        _crawlTask = Task.Run(async () =>
        {
            try
            {
                await RunCrawlAsync(projectId, resumeFromCheckpoint, _cts.Token).ConfigureAwait(false);
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
                Interlocked.Exchange(ref _isCrawling, 0);
                Interlocked.Exchange(ref _isPaused, 0);
                _stopwatch?.Stop();
                
                // Deactivate checkpoints (crawl complete)
                await DeactivateCheckpointsAsync(projectId).ConfigureAwait(false);
                
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
        
        // Resume if paused so workers can exit
        if (IsPaused)
        {
            _pauseEvent.Set();
            Interlocked.Exchange(ref _isPaused, 0);
        }
        
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

    public Task PauseCrawlAsync()
    {
        if (!IsCrawling || IsPaused)
        {
            _logger.LogWarning("Cannot pause: crawl is not running or already paused");
            return Task.CompletedTask;
        }

        _logger.LogInformation("Pausing crawl...");
        Interlocked.Exchange(ref _isPaused, 1);
        _pauseStartTime = DateTime.UtcNow;
        _pauseEvent.Reset(); // Signal workers to pause
        
        // Pause stopwatch
        _stopwatch?.Stop();
        
        SendProgressUpdate(_currentProjectId);
        
        return Task.CompletedTask;
    }

    public Task ResumeCrawlAsync()
    {
        if (!IsCrawling || !IsPaused)
        {
            _logger.LogWarning("Cannot resume: crawl is not paused");
            return Task.CompletedTask;
        }

        _logger.LogInformation("Resuming crawl...");
        
        // Calculate paused time
        if (_pauseStartTime.HasValue)
        {
            _pausedTime += DateTime.UtcNow - _pauseStartTime.Value;
            _pauseStartTime = null;
        }
        
        Interlocked.Exchange(ref _isPaused, 0);
        _pauseEvent.Set(); // Signal workers to resume
        
        // Resume stopwatch
        _stopwatch?.Start();
        
        SendProgressUpdate(_currentProjectId);
        
        return Task.CompletedTask;
    }

    public async Task<ShoutingIguana.Core.Models.CrawlCheckpoint?> GetActiveCheckpointAsync(int projectId)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var checkpointRepo = scope.ServiceProvider.GetRequiredService<ICrawlCheckpointRepository>();
            return await checkpointRepo.GetActiveCheckpointAsync(projectId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for active checkpoint");
            return null;
        }
    }

    private async Task RunCrawlAsync(int projectId, bool resumeFromCheckpoint, CancellationToken cancellationToken)
    {
        Project? project;
        ProjectSettings settings;
        ProxySettings? globalProxySettings;
        int checkpointInterval;
        
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
            
            // Fallback: if BaseUrl is missing from settings JSON, use Project.BaseUrl
            if (string.IsNullOrWhiteSpace(settings.BaseUrl) && !string.IsNullOrWhiteSpace(project.BaseUrl))
            {
                _logger.LogWarning("BaseUrl missing from ProjectSettings JSON, using Project.BaseUrl as fallback");
                settings.BaseUrl = project.BaseUrl;
            }
            
            // Get app settings once (avoid repeated service lookups in worker loop)
            var appSettings = scope.ServiceProvider.GetRequiredService<IAppSettingsService>();
            globalProxySettings = appSettings.CrawlSettings.GlobalProxy;
            checkpointInterval = appSettings.CrawlSettings.CheckpointInterval;

            var urlRepository = scope.ServiceProvider.GetRequiredService<IUrlRepository>();
            var queueRepository = scope.ServiceProvider.GetRequiredService<ICrawlQueueRepository>();
            
            if (resumeFromCheckpoint)
            {
                // Resuming from checkpoint - preserve existing data
                var existingQueueCount = await queueRepository.CountQueuedAsync(projectId).ConfigureAwait(false);
                _logger.LogInformation("Resuming crawl from checkpoint with {QueueCount} URLs in queue", existingQueueCount);
                _totalDiscovered = existingQueueCount;
                _queueSize = existingQueueCount;
            }
            else
            {
                // Starting fresh: check for existing data and clear it
                var existingUrlCount = await urlRepository.CountByProjectIdAsync(projectId).ConfigureAwait(false);
                var existingQueueCount = await queueRepository.CountQueuedAsync(projectId).ConfigureAwait(false);
                
                if (existingUrlCount > 0 || existingQueueCount > 0)
                {
                    _logger.LogInformation("Detected existing crawl data ({UrlCount} URLs, {QueueCount} queued). Clearing all data for fresh crawl...", 
                        existingUrlCount, existingQueueCount);
                    await ClearProjectCrawlDataAsync(projectId).ConfigureAwait(false);
                }
                
                _logger.LogInformation("Seeding queue with base URL: {BaseUrl}", settings.BaseUrl);
                await EnqueueUrlAsync(projectId, settings.BaseUrl, 0, 1000, settings.BaseUrl, settings.MaxUrlsToCrawl, allowRecrawl: true).ConfigureAwait(false);
            }
            
            // Discover and enqueue URLs from sitemap.xml if enabled (only when starting fresh, not when resuming)
            // Run this in parallel to avoid blocking the start of the crawl
            if (!resumeFromCheckpoint && settings.UseSitemapXml)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        _logger.LogInformation("Sitemap discovery enabled, searching for sitemaps...");
                        
                        // Create a new scope for this background task
                        using var sitemapScope = _serviceProvider.CreateScope();
                        var sitemapService = sitemapScope.ServiceProvider.GetRequiredService<ISitemapService>();
                        var sitemapUrls = await sitemapService.DiscoverSitemapUrlsAsync(settings.BaseUrl).ConfigureAwait(false);
                        
                        // Check cancellation before processing results
                        if (cancellationToken.IsCancellationRequested)
                        {
                            _logger.LogInformation("Sitemap discovery cancelled before enqueueing URLs");
                            return;
                        }
                        
                        if (sitemapUrls.Any())
                        {
                            _logger.LogInformation("Sitemap discovery completed. Found {Count} URLs from sitemap(s), enqueueing...", sitemapUrls.Count);
                            int enqueuedCount = 0;
                            
                            foreach (var url in sitemapUrls)
                            {
                                // Check cancellation periodically during enqueueing
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    _logger.LogInformation("Sitemap discovery cancelled after enqueueing {Count} of {Total} URLs", enqueuedCount, sitemapUrls.Count);
                                    break;
                                }
                                
                                await EnqueueUrlAsync(projectId, url, 0, 900, settings.BaseUrl, settings.MaxUrlsToCrawl, allowRecrawl: true).ConfigureAwait(false);
                                enqueuedCount++;
                            }
                            
                            if (!cancellationToken.IsCancellationRequested)
                            {
                                _logger.LogInformation("Enqueued {Count} URLs from sitemap discovery", enqueuedCount);
                            }
                        }
                        else
                        {
                            _logger.LogInformation("Sitemap discovery completed. No sitemap URLs discovered");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("Sitemap discovery was cancelled");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during sitemap discovery");
                    }
                }, cancellationToken);
            }
        }

        _logger.LogInformation("=== PHASE 1: DISCOVERY ===");
        _logger.LogInformation("Starting {WorkerCount} workers for project {ProjectId}. Total discovered: {TotalDiscovered}", 
            settings.ConcurrentRequests, projectId, _totalDiscovered);

        // Create worker tasks (without progress reporter)
        var workers = new List<Task>();
        for (int i = 0; i < settings.ConcurrentRequests; i++)
        {
            workers.Add(WorkerAsync(projectId, settings, globalProxySettings, checkpointInterval, cancellationToken));
        }

        // Progress reporting task (separate so we can control it)
        var progressTask = ReportProgressAsync(projectId, cancellationToken);

        // Wait for all worker tasks to complete (not including progress reporter)
        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
            _logger.LogInformation("Phase 1 complete. Crawled {UrlsCrawled} URLs, discovered {TotalDiscovered} total", 
                _urlsCrawled, _totalDiscovered);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Phase 1 stopped early. Crawled {UrlsCrawled} URLs before stopping. Proceeding to analyze crawled URLs", _urlsCrawled);
        }
        
        // PHASE 2: Analysis - Execute plugins on all crawled URLs
        // Use CancellationToken.None to ensure plugins always run on crawled URLs, even if Phase 1 was stopped
        _logger.LogInformation("=== PHASE 2: ANALYSIS ===");
        _currentPhase = CrawlPhase.Analysis;
        _urlsAnalyzed = 0;
        SendProgressUpdate(projectId);
        
        await RunAnalysisPhaseAsync(projectId, settings, CancellationToken.None).ConfigureAwait(false);
        
        _logger.LogInformation("Phase 2 complete. Analyzed {UrlsAnalyzed} URLs", _urlsAnalyzed);

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

    private async Task WorkerAsync(int projectId, ProjectSettings settings, ProxySettings? globalProxySettings, int checkpointInterval, CancellationToken cancellationToken)
    {
        int emptyQueueCount = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            // Wait if paused
            _pauseEvent.Wait(cancellationToken);
            
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

            // Get user agent for this request (needed for robots.txt check)
            var userAgent = settings.GetUserAgentString();

            // Detect if this is an external URL (marked with depth=-1)
            bool isExternalUrl = queueItem.Depth == -1;

            // Enforce politeness delay (skip robots.txt crawl-delay check for external URLs)
            if (!isExternalUrl)
            {
                await EnforcePolitenessDelayAsync(queueItem.HostKey, queueItem.Address, settings.CrawlDelaySeconds, userAgent, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // For external URLs, use a simple fixed delay per host without checking robots.txt
                await EnforceHostDelayAsync(queueItem.HostKey, 0.5).ConfigureAwait(false); // 500ms between external requests to same host
            }
            
            // Determine which proxy settings to use (project override or global)
            var proxySettings = settings.ProxyOverride ?? globalProxySettings;

            // Check robots.txt (skip for external URLs - we're only checking status, not crawling)
            bool? robotsAllowed = null;
            if (settings.RespectRobotsTxt && !isExternalUrl)
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

            // Determine fetch strategy: lightweight HTTP for static resources, Playwright for HTML pages
            // External URLs always use lightweight HTTP
            bool isStaticResource = IsStaticResource(queueItem.Address);
            
            UrlFetchResult urlData;
            Microsoft.Playwright.IPage? page = null;
            string? renderedHtml = null;
            List<RedirectHop> redirectChain = [];

            if (isStaticResource || isExternalUrl)
            {
                // Lightweight fetch for CSS, JS, images, and external URLs (no browser needed)
                urlData = await FetchStaticResourceAsync(queueItem.Address, userAgent, proxySettings, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Full Playwright fetch for HTML pages
                (urlData, page, renderedHtml, redirectChain) = await FetchUrlWithPlaywrightAsync(queueItem.Address, userAgent, proxySettings, cancellationToken).ConfigureAwait(false);
            }

                try
                {
                    // Save URL to database
                    var urlEntity = await SaveUrlAsync(projectId, queueItem, urlData, renderedHtml, robotsAllowed).ConfigureAwait(false);

                    // Save redirect chain if present
                    if (redirectChain.Count > 0)
                    {
                        await SaveRedirectChainAsync(urlEntity.Id, redirectChain).ConfigureAwait(false);
                    }

                // Extract and save links (for link graph and discovery)
                // Pass the page so we can capture diagnostic metadata
                // Skip link extraction for external URLs (we don't want to crawl the entire internet)
                if (urlData.IsSuccess && urlData.IsHtml && queueItem.Depth < settings.MaxCrawlDepth && !isExternalUrl)
                {
                    await ProcessLinksAsync(projectId, urlEntity, renderedHtml ?? "", queueItem.Address, queueItem.Depth, settings.BaseUrl, settings.MaxUrlsToCrawl, page).ConfigureAwait(false);
                }

                    // PHASE 1: Plugin execution is deferred to Phase 2 (Analysis)
                    // This eliminates race conditions where plugins run before resources are crawled

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
                    
                    // Save checkpoint at configured intervals
                    // Note: Multiple workers may trigger checkpoint simultaneously at same interval.
                    // This is acceptable - database handles concurrent inserts gracefully.
                    if (checkpointInterval > 0 && _urlsCrawled % checkpointInterval == 0)
                    {
                        await SaveCheckpointAsync(projectId).ConfigureAwait(false);
                    }
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
            catch (OperationCanceledException)
            {
                // Expected when crawl is stopped - don't log as error
                _logger.LogDebug("Crawling {Url} was cancelled", queueItem.Address);
                using (var scope = _serviceProvider.CreateScope())
                {
                    var queueRepository = scope.ServiceProvider.GetRequiredService<ICrawlQueueRepository>();
                    queueItem.State = QueueState.Queued; // Reset to queued for potential resume
                    await queueRepository.UpdateAsync(queueItem).ConfigureAwait(false);
                }
                throw; // Re-throw to stop worker
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
    /// Phase 2: Analysis - Execute plugins on all crawled URLs.
    /// </summary>
    private async Task RunAnalysisPhaseAsync(int projectId, Configuration.ProjectSettings settings, CancellationToken cancellationToken)
    {
        // Query only URL IDs to minimize memory usage (avoids loading all RenderedHtml content)
        List<int> urlIdsToAnalyze;
        using (var scope = _serviceProvider.CreateScope())
        {
            var urlRepository = scope.ServiceProvider.GetRequiredService<IUrlRepository>();
            urlIdsToAnalyze = await urlRepository.GetCompletedUrlIdsAsync(projectId).ConfigureAwait(false);
        }
        
        _logger.LogInformation("Found {Count} URLs to analyze", urlIdsToAnalyze.Count);
        
        if (urlIdsToAnalyze.Count == 0)
        {
            _logger.LogWarning("No URLs to analyze in Phase 2");
            return;
        }
        
        // Create a queue of URL IDs to process
        var analysisQueue = new System.Collections.Concurrent.ConcurrentQueue<int>(urlIdsToAnalyze);
        
        // Create analysis worker tasks
        var workers = new List<Task>();
        // Limit to 4 workers max - analysis is memory-intensive, not CPU-bound
        // Each worker holds ~4MB (HTML + DOM tree), so 4 workers = ~16MB vs 64MB+ with 16 cores
        int workerCount = Math.Min(4, settings.ConcurrentRequests);
        
        for (int i = 0; i < workerCount; i++)
        {
            workers.Add(AnalysisWorkerAsync(projectId, settings, analysisQueue, cancellationToken));
        }
        
        // Wait for all analysis workers to complete
        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    /// <summary>
    /// Worker for Phase 2 analysis - processes URLs from the analysis queue.
    /// </summary>
    private async Task AnalysisWorkerAsync(
        int projectId, 
        Configuration.ProjectSettings settings, 
        System.Collections.Concurrent.ConcurrentQueue<int> analysisQueue,
        CancellationToken cancellationToken)
    {
        int urlsProcessed = 0;
        
        while (!cancellationToken.IsCancellationRequested)
        {
            // Wait if paused
            _pauseEvent.Wait(cancellationToken);
            
            if (!analysisQueue.TryDequeue(out int urlId))
            {
                // No more URLs to analyze
                break;
            }
            
            UrlAnalysisDto? urlData = null;
            string? renderedHtml = null;
            
            try
            {
                // Load URL metadata WITHOUT the huge RenderedHtml field (memory optimization)
                using (var scope = _serviceProvider.CreateScope())
                {
                    var urlRepository = scope.ServiceProvider.GetRequiredService<IUrlRepository>();
                    urlData = await urlRepository.GetForAnalysisAsync(urlId).ConfigureAwait(false);
                }
                
                if (urlData == null)
                {
                    _logger.LogWarning("URL ID {UrlId} not found for analysis", urlId);
                    continue;
                }
                
                // Increment active workers AFTER we've confirmed URL exists
                Interlocked.Increment(ref _activeWorkers);
                
                _logger.LogDebug("Analyzing URL: {Url}", urlData.Address);
                
                // Load HTML separately (only when needed, and keeps memory footprint smaller)
                using (var scope = _serviceProvider.CreateScope())
                {
                    var urlRepository = scope.ServiceProvider.GetRequiredService<IUrlRepository>();
                    renderedHtml = await urlRepository.GetRenderedHtmlAsync(urlId).ConfigureAwait(false);
                }
                
                // Extract headers from loaded entity
                var headers = urlData.Headers
                    .GroupBy(h => h.Name.ToLowerInvariant())
                    .ToDictionary(g => g.Key, g => g.First().Value);
                
                // Execute plugin tasks with saved HTML (no live browser page needed)
                using (var pluginScope = _serviceProvider.CreateScope())
                {
                    var pluginExecutor = pluginScope.ServiceProvider.GetRequiredService<PluginExecutor>();
                    var userAgent = settings.GetUserAgentString();
                    
                    // Pass NULL for page - plugins will work from saved HTML
                    await pluginExecutor.ExecuteTasksAsync(
                        urlData, 
                        page: null,  // No live browser page in Phase 2
                        renderedHtml, 
                        headers, 
                        settings, 
                        userAgent, 
                        projectId, 
                        cancellationToken).ConfigureAwait(false);
                }
                
                Interlocked.Increment(ref _urlsAnalyzed);
                urlsProcessed++;
                
                // Update last analyzed URL for progress display
                _lastCrawledUrl = urlData.Address;
                _lastCrawledStatus = urlData.HttpStatus ?? 0;
                
                // Explicit cleanup to help GC reclaim memory immediately
                renderedHtml = null;
                urlData = null;
                
                // Periodic garbage collection to prevent memory buildup (every 50 URLs)
                // More aggressive than Phase 1 since plugins can accumulate significant data
                if (urlsProcessed % 50 == 0)
                {
                    GC.Collect(2, GCCollectionMode.Aggressive, true, true);
                    _logger.LogDebug("Performed GC after analyzing {Count} URLs in this worker", urlsProcessed);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Analysis cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing URL ID {UrlId}", urlId);
                Interlocked.Increment(ref _errorCount);
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
    private async Task<(UrlFetchResult result, Microsoft.Playwright.IPage? page, string? html, List<RedirectHop> redirectChain)> FetchUrlWithPlaywrightAsync(string url, string userAgent, ProxySettings? proxySettings, CancellationToken cancellationToken)
    {
        Microsoft.Playwright.IPage? page = null;
        string? renderedHtml = null;
        var redirectChain = new List<RedirectHop>();

        try
        {
            // Check cancellation before creating page
            cancellationToken.ThrowIfCancellationRequested();
            
            // Create a new page with the specified user agent and proxy (caller becomes responsible for disposal from this point)
            page = await _playwrightService.CreatePageAsync(userAgent, proxySettings).ConfigureAwait(false);
            
            // Smart adaptive page loading
            Microsoft.Playwright.IResponse? response = null;
            bool useFastMode = Interlocked.CompareExchange(ref _useFastLoadingMode, 0, 0) == 1;
            
            if (!useFastMode)
            {
                // Try NetworkIdle with SHORT timeout (5s instead of 30s)
                try
                {
                    response = await page.GotoAsync(url, new Microsoft.Playwright.PageGotoOptions
                    {
                        WaitUntil = Microsoft.Playwright.WaitUntilState.NetworkIdle,
                        Timeout = 5000 // 5 seconds
                    }).ConfigureAwait(false);
                    
                    Interlocked.Increment(ref _networkIdleSuccessCount);
                    _logger.LogDebug("Page loaded with NetworkIdle for {Url}", url);
                }
                catch (Exception ex) when (ex.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
                {
                    Interlocked.Increment(ref _networkIdleFailureCount);
                    
                    // Check if we should switch to fast mode (3+ failures, no successes)
                    // Use volatile reads for thread-safe access
                    int failureCount = Interlocked.CompareExchange(ref _networkIdleFailureCount, 0, 0);
                    int successCount = Interlocked.CompareExchange(ref _networkIdleSuccessCount, 0, 0);
                    
                    if (failureCount >= 3 && successCount == 0)
                    {
                        Interlocked.Exchange(ref _useFastLoadingMode, 1);
                        _logger.LogInformation("Detected site with continuous background activity. Switching to fast loading mode for better performance.");
                    }
                    else
                    {
                        _logger.LogDebug("NetworkIdle timeout for {Url}, using DOMContentLoaded", url);
                    }
                    
                    response = await LoadWithDOMContentLoadedAsync(page, url, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                // Fast loading mode (DOMContentLoaded + grace period)
                response = await LoadWithDOMContentLoadedAsync(page, url, cancellationToken).ConfigureAwait(false);
            }

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
            
            // Check if this is a redirect loop error
            bool isRedirectLoop = ex.Message.Contains("ERR_TOO_MANY_REDIRECTS", StringComparison.OrdinalIgnoreCase);
            
            // Return error result WITH the page - caller owns disposal in all cases
            // This ensures single ownership and prevents double-disposal attempts
            return (new UrlFetchResult
            {
                StatusCode = 0,
                IsSuccess = false,
                ErrorMessage = ex.Message,
                IsRedirectLoop = isRedirectLoop
            }, page, null, redirectChain);
        }
    }

    /// <summary>
    /// Creates an HttpClient configured with proxy settings and browser-like headers.
    /// </summary>
    private HttpClient CreateHttpClient(string userAgent, ProxySettings? proxySettings)
    {
        HttpClientHandler handler = new()
        {
            AllowAutoRedirect = false, // We want to capture redirects
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            UseCookies = false // Stateless crawling
        };

        // Configure proxy if provided
        if (proxySettings?.Enabled == true && !string.IsNullOrWhiteSpace(proxySettings.Server))
        {
            // Note: SOCKS5 is not natively supported by HttpClient, only HTTP/HTTPS proxies
            if (proxySettings.Type == ProxyType.Socks5)
            {
                _logger.LogWarning("SOCKS5 proxy requested but not supported by HttpClient. Static resources will use direct connection. Use Playwright for SOCKS5 support.");
            }
            else
            {
                var proxyUri = new Uri(proxySettings.GetProxyUrl());
                handler.Proxy = new System.Net.WebProxy(proxyUri)
                {
                    BypassProxyOnLocal = true,
                    BypassList = proxySettings.BypassList.ToArray()
                };

                // Add proxy authentication if required
                if (proxySettings.RequiresAuthentication && !string.IsNullOrWhiteSpace(proxySettings.Username))
                {
                    var password = proxySettings.GetPassword();
                    handler.Proxy.Credentials = new System.Net.NetworkCredential(
                        proxySettings.Username,
                        password
                    );
                }

                handler.UseProxy = true;
                _logger.LogDebug("Using proxy {ProxyUrl} for static resource requests", proxySettings.GetProxyUrl());
            }
        }

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        // Add browser-like headers to avoid bot detection
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("User-Agent", userAgent);
        client.DefaultRequestHeaders.Add("Accept", "*/*");
        client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
        client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
        client.DefaultRequestHeaders.Add("Pragma", "no-cache");
        // DNT (Do Not Track) - some crawlers include this
        client.DefaultRequestHeaders.Add("DNT", "1");

        return client;
    }

    /// <summary>
    /// Fetches a static resource (CSS, JS, image) using lightweight HTTP HEAD/GET request.
    /// Much faster than Playwright for static assets that don't need rendering.
    /// </summary>
    private async Task<UrlFetchResult> FetchStaticResourceAsync(string url, string userAgent, ProxySettings? proxySettings, CancellationToken cancellationToken)
    {
        // Create HttpClient with proxy and browser-like headers
        using var httpClient = CreateHttpClient(userAgent, proxySettings);
        
        try
        {
            // IMPORTANT: Use GET instead of HEAD for static resources
            // Some servers (especially .NET/ASP.NET) return different status codes for HEAD vs GET
            // HEAD might return 200 even when file doesn't exist, while GET returns 404
            // This matches Screaming Frog behavior which uses GET for all resources
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            HttpResponseMessage response;
            
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            using (response)
            {
                var headers = response.Headers
                    .Concat(response.Content.Headers)
                    .Select(h => new KeyValuePair<string, string>(h.Key, string.Join(", ", h.Value)))
                    .ToList();

                var contentType = response.Content.Headers.ContentType?.ToString();
                var contentLength = response.Content.Headers.ContentLength;

                return new UrlFetchResult
                {
                    StatusCode = (int)response.StatusCode,
                    IsSuccess = response.IsSuccessStatusCode,
                    ContentType = contentType,
                    ContentLength = contentLength,
                    Headers = headers,
                    IsHtml = false, // Static resources are not HTML
                    Content = null
                };
            }
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Timeout fetching static resource: {Url}", url);
            return new UrlFetchResult
            {
                StatusCode = 0,
                IsSuccess = false,
                ErrorMessage = "Request timeout",
                IsHtml = false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching static resource: {Url}", url);
            return new UrlFetchResult
            {
                StatusCode = 0,
                IsSuccess = false,
                ErrorMessage = ex.Message,
                IsHtml = false
            };
        }
    }

    /// <summary>
    /// Determines if a URL is a non-HTML resource that should use lightweight HTTP checking.
    /// This includes CSS, JS, images, videos, audio, documents, fonts, XML, etc.
    /// Only HTML pages should use Playwright for full rendering.
    /// </summary>
    private static bool IsStaticResource(string url)
    {
        try
        {
            var uri = new Uri(url);
            var path = uri.AbsolutePath.ToLowerInvariant();
            
            // Get the filename (last segment after /)
            var lastSlash = path.LastIndexOf('/');
            var filename = lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;
            
            // If filename has no extension (no dot after last slash), it's likely an HTML page
            if (!filename.Contains('.'))
            {
                return false; // Use Playwright for HTML pages like /page, /about
            }
            
            // Check if it's an HTML-generating extension
            if (path.EndsWith(".html") || 
                path.EndsWith(".htm") ||
                path.EndsWith(".php") ||
                path.EndsWith(".asp") ||
                path.EndsWith(".aspx") ||
                path.EndsWith(".jsp") ||
                path.EndsWith(".cfm"))
            {
                return false; // Use Playwright for HTML pages
            }
            
            // Everything else is a static resource - use lightweight HTTP checking
            // This includes:
            // - Stylesheets: .css
            // - Scripts: .js, .mjs
            // - Images: .jpg, .jpeg, .png, .gif, .webp, .ico, .bmp, .svg
            // - Videos: .mp4, .avi, .mov, .wmv, .flv, .mkv, .webm, .m4v, .mpg, .mpeg
            // - Audio: .mp3, .wav, .ogg, .m4a, .aac, .flac, .wma
            // - Documents: .pdf, .doc, .docx, .xls, .xlsx, .ppt, .pptx
            // - Data: .xml, .json, .csv, .txt
            // - Fonts: .ttf, .otf, .woff, .woff2, .eot
            // - Archives: .zip, .rar, .7z, .tar, .gz
            // And any other file type with an extension
            return true;
        }
        catch
        {
            return false; // If we can't parse the URL, treat as HTML and use Playwright
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
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // User-initiated cancellation - don't log as error
            _logger.LogDebug("Fetch cancelled for {Url}", url);
            throw new OperationCanceledException("Fetch was cancelled", cancellationToken);
        }
        catch (TaskCanceledException)
        {
            // Timeout (not user-initiated)
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
            existing.IsRedirectLoop = fetchResult.IsRedirectLoop;
            
            // Store rendered HTML for Phase 2 analysis (only for successful HTML pages)
            if (fetchResult.IsSuccess && fetchResult.IsHtml && !string.IsNullOrEmpty(renderedHtml))
            {
                existing.RenderedHtml = renderedHtml;
            }
            
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
            IsRedirectLoop = fetchResult.IsRedirectLoop,
            
            // Store rendered HTML for Phase 2 analysis (only for successful HTML pages)
            RenderedHtml = (fetchResult.IsSuccess && fetchResult.IsHtml && !string.IsNullOrEmpty(renderedHtml)) ? renderedHtml : null,
            
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
                        var root = jsonDoc.RootElement;
                        
                        // JSON-LD can be either a single object or an array of objects
                        if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            // Handle array of objects
                            foreach (var item in root.EnumerateArray())
                            {
                                var type = item.TryGetProperty("@type", out var typeEl) ? typeEl.GetString() : "Unknown";
                                result.Add(new StructuredDataInfo
                                {
                                    Type = "json-ld",
                                    SchemaType = type ?? "Unknown",
                                    RawData = item.GetRawText(),
                                    IsValid = true
                                });
                            }
                        }
                        else if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            // Handle single object
                            var type = root.TryGetProperty("@type", out var typeEl) ? typeEl.GetString() : "Unknown";
                            result.Add(new StructuredDataInfo
                            {
                                Type = "json-ld",
                                SchemaType = type ?? "Unknown",
                                RawData = json,
                                IsValid = true
                            });
                        }
                    }
                    catch (System.Text.Json.JsonException ex)
                    {
                        result.Add(new StructuredDataInfo
                        {
                            Type = "json-ld",
                            SchemaType = "Unknown",
                            RawData = json,
                            IsValid = false,
                            ValidationErrors = $"Invalid JSON: {ex.Message}"
                        });
                    }
                    catch (System.InvalidOperationException ex)
                    {
                        // Handle cases where the JSON structure is unexpected
                        result.Add(new StructuredDataInfo
                        {
                            Type = "json-ld",
                            SchemaType = "Unknown",
                            RawData = json,
                            IsValid = false,
                            ValidationErrors = $"Unexpected JSON structure: {ex.Message}"
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

    private async Task ProcessLinksAsync(int projectId, Url fromUrl, string htmlContent, string currentPageUrl, int currentDepth, string projectBaseUrl, int maxUrlsToCrawl, Microsoft.Playwright.IPage? page)
    {
        var extractedLinksEnum = await _linkExtractor.ExtractLinksAsync(htmlContent, currentPageUrl).ConfigureAwait(false);
        var extractedLinks = extractedLinksEnum.ToList(); // Materialize to avoid multiple enumeration
        var projectBaseUri = new Uri(projectBaseUrl);
        
        // Gather diagnostic metadata if we have a live browser page (Phase 1)
        Dictionary<string, ElementDiagnosticInfo>? diagnosticsMap = null;
        if (page != null)
        {
            diagnosticsMap = await GatherLinkDiagnosticsAsync(page, currentPageUrl, extractedLinks).ConfigureAwait(false);
        }

        foreach (var link in extractedLinks)
        {
            // Enqueue hyperlinks, stylesheets, scripts, and images for crawling
            // This matches Screaming Frog's behavior of crawling all resources
            if (link.LinkType == LinkType.Hyperlink || 
                link.LinkType == LinkType.Stylesheet || 
                link.LinkType == LinkType.Script || 
                link.LinkType == LinkType.Image)
            {
                // Enqueue links from both same and external domains
                if (Uri.TryCreate(link.Url, UriKind.Absolute, out var linkUri))
                {
                    if (IsSameDomain(projectBaseUri, linkUri))
                    {
                        // Internal link - use depth+1 for hyperlinks; static resources stay at same depth to avoid hitting depth limit
                        int resourceDepth = link.LinkType == LinkType.Hyperlink ? currentDepth + 1 : currentDepth;
                        await EnqueueUrlAsync(projectId, link.Url, resourceDepth, 100, projectBaseUrl, maxUrlsToCrawl).ConfigureAwait(false);
                    }
                    else
                    {
                        // External link - enqueue for status checking with a special depth marker (-1)
                        // Negative depth clearly marks external URLs and can't conflict with any valid internal depth
                        _logger.LogDebug("Enqueueing external link for status check: {Url} (external domain: {FromDomain})", link.Url, linkUri.Host);
                        await EnqueueUrlAsync(projectId, link.Url, -1, 50, projectBaseUrl, maxUrlsToCrawl).ConfigureAwait(false);
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
                
                // Add diagnostic metadata if captured (Phase 1 only)
                if (diagnosticsMap != null && diagnosticsMap.TryGetValue(link.Url, out var diagnosticInfo))
                {
                    linkEntity.DomPath = diagnosticInfo.DomPath;
                    linkEntity.ElementTag = diagnosticInfo.TagName;
                    linkEntity.IsVisible = diagnosticInfo.IsVisible;
                    linkEntity.PositionX = (int?)diagnosticInfo.BoundingBox?.X;
                    linkEntity.PositionY = (int?)diagnosticInfo.BoundingBox?.Y;
                    linkEntity.ElementWidth = (int?)diagnosticInfo.BoundingBox?.Width;
                    linkEntity.ElementHeight = (int?)diagnosticInfo.BoundingBox?.Height;
                    
                    // Trim HTML snippet to max 1000 chars
                    if (!string.IsNullOrEmpty(diagnosticInfo.HtmlContext))
                    {
                        linkEntity.HtmlSnippet = diagnosticInfo.HtmlContext.Length > 1000 
                            ? diagnosticInfo.HtmlContext.Substring(0, 1000) 
                            : diagnosticInfo.HtmlContext;
                    }
                    
                    linkEntity.ParentTag = diagnosticInfo.ParentElement;
                }
                
                await linkRepository.CreateAsync(linkEntity).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save link from {FromUrl} to {ToUrl}", fromUrl.Address, link.Url);
            }
        }
    }
    
    /// <summary>
    /// Gathers diagnostic metadata for all links on a page (Phase 1 only).
    /// Returns a dictionary mapping resolved URL to diagnostic info.
    /// </summary>
    private async Task<Dictionary<string, ElementDiagnosticInfo>> GatherLinkDiagnosticsAsync(
        Microsoft.Playwright.IPage page,
        string currentPageUrl,
        IEnumerable<ExtractedLink> extractedLinks)
    {
        var diagnostics = new Dictionary<string, ElementDiagnosticInfo>(StringComparer.OrdinalIgnoreCase);
        
        try
        {
            var currentUri = new Uri(currentPageUrl);
            
            // Build a set of URLs we care about for faster lookup
            var targetUrls = new HashSet<string>(extractedLinks.Select(l => l.Url), StringComparer.OrdinalIgnoreCase);
            
            // Query all link-related elements: a, link, script, img
            var elements = await page.QuerySelectorAllAsync("a[href], link[href], script[src], img[src]").ConfigureAwait(false);
            
            _logger.LogDebug("Gathering diagnostics for {Count} elements on {Url}", elements.Count, currentPageUrl);
            
            foreach (var element in elements)
            {
                try
                {
                    // Get the URL attribute (href or src)
                    var href = await element.GetAttributeAsync("href").ConfigureAwait(false)
                             ?? await element.GetAttributeAsync("src").ConfigureAwait(false);
                    
                    if (string.IsNullOrEmpty(href))
                        continue;
                    
                    // Resolve to absolute URL to match extractedLinks
                    string resolvedUrl;
                    try
                    {
                        if (Uri.TryCreate(href, UriKind.Absolute, out var absoluteUri))
                        {
                            resolvedUrl = absoluteUri.ToString();
                        }
                        else if (Uri.TryCreate(currentUri, href, out var resolvedUri))
                        {
                            resolvedUrl = resolvedUri.ToString();
                        }
                        else
                        {
                            continue;
                        }
                        
                        // Remove fragment for matching
                        var builder = new UriBuilder(resolvedUrl) { Fragment = string.Empty };
                        resolvedUrl = builder.Uri.ToString();
                    }
                    catch
                    {
                        continue;
                    }
                    
                    // Only gather diagnostics for links we're actually tracking
                    if (!targetUrls.Contains(resolvedUrl))
                        continue;
                    
                    // Gather all diagnostic info in a SINGLE JavaScript evaluation (performance optimization)
                    var diagnosticJson = await element.EvaluateAsync<string>(
                        @"(el) => {
                            // Get bounding box
                            const rect = el.getBoundingClientRect();
                            
                            // Get visibility
                            const style = window.getComputedStyle(el);
                            const isVisible = style.display !== 'none' && 
                                            style.visibility !== 'hidden' && 
                                            style.opacity !== '0' &&
                                            rect.width > 0 && 
                                            rect.height > 0;
                            
                            // Get DOM path
                            const path = [];
                            let current = el;
                            while (current && current.nodeType === Node.ELEMENT_NODE) {
                                let selector = current.nodeName.toLowerCase();
                                if (current.id) {
                                    selector += '#' + current.id;
                                    path.unshift(selector);
                                    break;
                                }
                                if (current.className && typeof current.className === 'string') {
                                    const classes = current.className.trim().split(/\s+/).join('.');
                                    if (classes) selector += '.' + classes;
                                }
                                if (current.parentElement) {
                                    const siblings = Array.from(current.parentElement.children);
                                    const index = siblings.indexOf(current) + 1;
                                    if (siblings.length > 1) {
                                        selector += ':nth-child(' + index + ')';
                                    }
                                }
                                path.unshift(selector);
                                current = current.parentElement;
                                if (path.length >= 10) break;
                            }
                            
                            return JSON.stringify({
                                tagName: el.tagName.toLowerCase(),
                                domPath: path.join(' > '),
                                isVisible: isVisible,
                                boundingBox: {
                                    x: rect.x,
                                    y: rect.y,
                                    width: rect.width,
                                    height: rect.height
                                },
                                outerHtml: el.outerHTML,
                                parentTag: el.parentElement?.tagName.toLowerCase() || null
                            });
                        }",
                        element).ConfigureAwait(false);
                    
                    // Parse the JSON response (case-insensitive to handle camelCase from JS)
                    var diagnostic = System.Text.Json.JsonSerializer.Deserialize<DiagnosticData>(diagnosticJson, 
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    
                    if (diagnostic != null)
                    {
                        var diagnosticInfo = new ElementDiagnosticInfo
                        {
                            TagName = diagnostic.TagName ?? "unknown",
                            DomPath = diagnostic.DomPath ?? "unknown",
                            IsVisible = diagnostic.IsVisible,
                            BoundingBox = new BoundingBoxInfo
                            {
                                X = diagnostic.BoundingBox?.X ?? 0,
                                Y = diagnostic.BoundingBox?.Y ?? 0,
                                Width = diagnostic.BoundingBox?.Width ?? 0,
                                Height = diagnostic.BoundingBox?.Height ?? 0
                            },
                            HtmlContext = diagnostic.OuterHtml ?? "",
                            ParentElement = diagnostic.ParentTag
                        };
                        
                        diagnostics[resolvedUrl] = diagnosticInfo;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error gathering diagnostic for element");
                }
            }
            
            _logger.LogDebug("Captured diagnostics for {Count} links on {Url}", diagnostics.Count, currentPageUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error gathering link diagnostics for {Url}", currentPageUrl);
        }
        
        return diagnostics;
    }

    private async Task EnqueueUrlAsync(int projectId, string url, int depth, int priority, string projectBaseUrl, int maxUrlsToCrawl = int.MaxValue, bool allowRecrawl = false)
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
        bool isExternal = !IsSameDomain(projectBaseUri, urlUri);
        
        // Allow external URLs only when marked with depth=-1 (for status checking)
        // Internal URLs are allowed at any valid depth (0 and positive)
        if (isExternal && depth != -1)
        {
            _logger.LogDebug("URL from different domain, skipping: {Url} (expected: {BaseDomain})", url, projectBaseUri.Host);
            return;
        }
        
        // Check if we've already discovered enough URLs (respect MaxUrlsToCrawl for queue too)
        // Allow seed URLs and external links to always be queued
        if (!allowRecrawl && !isExternal && _totalDiscovered >= maxUrlsToCrawl)
        {
            _logger.LogDebug("Reached MaxUrlsToCrawl limit ({MaxUrls}), skipping URL: {Url}", maxUrlsToCrawl, url);
            return;
        }

            // NOTE: We now crawl ALL resource types (PDFs, videos, fonts, etc.) using lightweight HTTP checking
            // The binary file skip filter has been removed to match Screaming Frog's behavior

            using var scope = _serviceProvider.CreateScope();
            var urlRepository = scope.ServiceProvider.GetRequiredService<IUrlRepository>();
            var queueRepository = scope.ServiceProvider.GetRequiredService<ICrawlQueueRepository>();
            
            // Check if URL already exists in queue (prevents race conditions between workers)
            var existingInQueue = await queueRepository.GetByAddressAsync(projectId, url).ConfigureAwait(false);
            
            if (allowRecrawl)
            {
                // For seed URLs when starting a fresh crawl, check if URL exists in queue and re-queue if completed/failed
                if (existingInQueue != null)
                {
                    // If already queued or in progress, skip
                    if (existingInQueue.State == QueueState.Queued || existingInQueue.State == QueueState.InProgress)
                    {
                        _logger.LogDebug("URL already in queue with active state, skipping: {Url}", url);
                        return;
                    }
                    
                    // If completed or failed, re-queue it by updating state
                    existingInQueue.State = QueueState.Queued;
                    existingInQueue.Priority = priority;
                    existingInQueue.Depth = depth;
                    existingInQueue.EnqueuedUtc = DateTime.UtcNow;
                    await queueRepository.UpdateAsync(existingInQueue).ConfigureAwait(false);
                    Interlocked.Increment(ref _totalDiscovered);
                    Interlocked.Increment(ref _queueSize);
                    _logger.LogInformation("✓ Re-queued: {Url} (Depth: {Depth})", url, depth);
                    return;
                }
            }
            else
            {
                // For normal crawling, check BOTH queue and crawled URLs to prevent duplicates
                // Check queue first (faster, prevents race conditions)
                if (existingInQueue != null)
                {
                    _logger.LogDebug("URL already in queue, skipping: {Url}", url);
                    return;
                }
                
                // Also check if already crawled
                var existing = await urlRepository.GetByAddressAsync(projectId, url).ConfigureAwait(false);
                if (existing != null)
                {
                    _logger.LogDebug("URL already crawled, skipping: {Url}", url);
                    return;
                }
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
            // Check if this is a duplicate key exception from the database unique constraint
            var exceptionMessage = ex.InnerException?.Message ?? ex.Message;
            if (exceptionMessage.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) || 
                exceptionMessage.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
            {
                // Duplicate URL caught by database unique constraint (race condition between workers)
                _logger.LogDebug("URL already in queue (caught by unique constraint), skipping: {Url}", url);
            }
            else
            {
                _logger.LogError(ex, "Failed to enqueue URL: {Url}", url);
            }
        }
    }

    private async Task<Microsoft.Playwright.IResponse?> LoadWithDOMContentLoadedAsync(Microsoft.Playwright.IPage page, string url, CancellationToken cancellationToken)
    {
        var response = await page.GotoAsync(url, new Microsoft.Playwright.PageGotoOptions
        {
            WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded,
            Timeout = 15000
        }).ConfigureAwait(false);
        
        // Grace period for lazy-loaded content (JavaScript, images, etc.)
        await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        
        // Wait for critical images to finish loading (max 2 seconds)
        try
        {
            await page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.Load, new Microsoft.Playwright.PageWaitForLoadStateOptions
            {
                Timeout = 2000
            }).ConfigureAwait(false);
        }
        catch
        {
            // Non-critical, continue even if images haven't fully loaded
        }
        
        return response;
    }

    private async Task EnforcePolitenessDelayAsync(string hostKey, string url, double configuredDelaySeconds, string userAgent, CancellationToken cancellationToken)
    {
        // Maximum allowed crawl-delay to prevent absurd values from robots.txt (e.g., hours/days)
        const double MaxAllowedDelaySeconds = 10.0; // Cap at 10 seconds
        
        // Check robots.txt for crawl-delay directive
        double effectiveDelay = configuredDelaySeconds;
        try
        {
            var uri = new Uri(url);
            var host = $"{uri.Scheme}://{uri.Host}";
            var robotsCrawlDelay = await _robotsService.GetCrawlDelayAsync(host, userAgent).ConfigureAwait(false);
            
            if (robotsCrawlDelay.HasValue && robotsCrawlDelay.Value > effectiveDelay)
            {
                // Apply maximum cap to prevent absurd delays
                if (robotsCrawlDelay.Value > MaxAllowedDelaySeconds)
                {
                    _logger.LogWarning("robots.txt crawl-delay of {Delay}s for {Host} exceeds maximum of {Max}s, capping delay. Site may not want to be crawled.", 
                        robotsCrawlDelay.Value, host, MaxAllowedDelaySeconds);
                    effectiveDelay = MaxAllowedDelaySeconds;
                }
                else
                {
                    _logger.LogDebug("Using robots.txt crawl-delay of {Delay}s for {Host} (configured: {ConfiguredDelay}s)", 
                        robotsCrawlDelay.Value, host, configuredDelaySeconds);
                    effectiveDelay = robotsCrawlDelay.Value;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking robots.txt crawl-delay, using configured delay");
        }

        // Add random jitter (±10% of delay) to appear more human-like
        var jitterRange = effectiveDelay * 0.1;
        var random = Random.Shared;
        var jitter = (random.NextDouble() * jitterRange * 2) - jitterRange; // Range: -10% to +10%
        var delayWithJitter = Math.Max(0.1, effectiveDelay + jitter); // Ensure minimum 100ms delay

        if (_lastCrawlTime.TryGetValue(hostKey, out var lastTime))
        {
            var elapsed = DateTime.UtcNow - lastTime;
            var requiredDelay = TimeSpan.FromSeconds(delayWithJitter);
            if (elapsed < requiredDelay)
            {
                var waitTime = requiredDelay - elapsed;
                _logger.LogDebug("Waiting {WaitTime}ms before crawling next URL on {Host}", waitTime.TotalMilliseconds, hostKey);
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

    /// <summary>
    /// Enforces a simple fixed delay per host without checking robots.txt.
    /// Used for external URL status checking to avoid unnecessary robots.txt fetches.
    /// </summary>
    private async Task EnforceHostDelayAsync(string hostKey, double delaySeconds)
    {
        if (_lastCrawlTime.TryGetValue(hostKey, out var lastTime))
        {
            var elapsed = DateTime.UtcNow - lastTime;
            var requiredDelay = TimeSpan.FromSeconds(delaySeconds);
            if (elapsed < requiredDelay)
            {
                var waitTime = requiredDelay - elapsed;
                await Task.Delay(waitTime).ConfigureAwait(false);
            }
        }

        _lastCrawlTime[hostKey] = DateTime.UtcNow;
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
                
                _logger.LogWarning("Consider stopping the crawl and restarting the application if memory usage becomes problematic");
            }

            var args = new CrawlProgressEventArgs
            {
                CurrentPhase = _currentPhase,
                UrlsCrawled = _urlsCrawled,
                UrlsAnalyzed = _urlsAnalyzed,
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

    /// <summary>
    /// Saves a checkpoint for crash recovery.
    /// </summary>
    private async Task SaveCheckpointAsync(int projectId)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var checkpointRepo = scope.ServiceProvider.GetRequiredService<ICrawlCheckpointRepository>();
            
            var checkpoint = new ShoutingIguana.Core.Models.CrawlCheckpoint
            {
                ProjectId = projectId,
                CreatedAt = DateTime.UtcNow,
                Phase = _currentPhase == CrawlPhase.Discovery ? "Discovery" : "Analysis",
                UrlsCrawled = _urlsCrawled,
                UrlsAnalyzed = _urlsAnalyzed,
                ErrorCount = _errorCount,
                QueueSize = _queueSize,
                LastCrawledUrl = _lastCrawledUrl,
                Status = "InProgress",
                ElapsedSeconds = (_stopwatch?.Elapsed ?? TimeSpan.Zero).TotalSeconds,
                IsActive = true
            };
            
            await checkpointRepo.CreateAsync(checkpoint).ConfigureAwait(false);
            _logger.LogInformation("Checkpoint saved: {UrlsCrawled} URLs crawled", _urlsCrawled);
            
            // Cleanup old checkpoints to avoid bloat
            await checkpointRepo.CleanupOldCheckpointsAsync(projectId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving checkpoint");
        }
    }

    /// <summary>
    /// Deactivates all checkpoints for a project when crawl completes successfully.
    /// </summary>
    private async Task DeactivateCheckpointsAsync(int projectId)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var checkpointRepo = scope.ServiceProvider.GetRequiredService<ICrawlCheckpointRepository>();
            await checkpointRepo.DeactivateCheckpointsAsync(projectId).ConfigureAwait(false);
            _logger.LogInformation("Deactivated checkpoints for project {ProjectId}", projectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating checkpoints");
        }
    }

    /// <summary>
    /// Clears all crawl data for a project to start a fresh crawl.
    /// Deletes URLs, Links, Findings, Images, Redirects, Queue items, and Checkpoints.
    /// </summary>
    private async Task ClearProjectCrawlDataAsync(int projectId)
    {
        try
        {
            _logger.LogInformation("Clearing all crawl data for project {ProjectId} to start fresh crawl", projectId);
            
            using var scope = _serviceProvider.CreateScope();
            
            // Get all repositories
            var urlRepo = scope.ServiceProvider.GetRequiredService<IUrlRepository>();
            var linkRepo = scope.ServiceProvider.GetRequiredService<ILinkRepository>();
            var queueRepo = scope.ServiceProvider.GetRequiredService<ICrawlQueueRepository>();
            var checkpointRepo = scope.ServiceProvider.GetRequiredService<ICrawlCheckpointRepository>();
            
            // Clear all data (order matters for foreign keys)
            // 1. Delete Links first (they have RESTRICT FK to URLs, must be removed before URLs)
            await linkRepo.DeleteByProjectIdAsync(projectId).ConfigureAwait(false);
            
            // 2. Delete Queue (no FK dependencies)
            await queueRepo.DeleteAllByProjectIdAsync(projectId).ConfigureAwait(false);
            
            // 3. Delete URLs (CASCADE will automatically delete Findings, Images, Redirects, Headers, etc.)
            await urlRepo.DeleteByProjectIdAsync(projectId).ConfigureAwait(false);
            
            // 4. Deactivate checkpoints
            await checkpointRepo.DeactivateCheckpointsAsync(projectId).ConfigureAwait(false);
            
            _logger.LogInformation("Successfully cleared all crawl data for project {ProjectId}", projectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing project crawl data");
            throw; // Re-throw to prevent crawl from starting with partial clear
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
        public bool IsRedirectLoop { get; set; }
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
    
    /// <summary>
    /// Element diagnostic information captured during Phase 1.
    /// </summary>
    private class ElementDiagnosticInfo
    {
        public string TagName { get; set; } = string.Empty;
        public string DomPath { get; set; } = string.Empty;
        public bool IsVisible { get; set; }
        public BoundingBoxInfo? BoundingBox { get; set; }
        public string HtmlContext { get; set; } = string.Empty;
        public string? ParentElement { get; set; }
    }
    
    /// <summary>
    /// Bounding box coordinates for an element.
    /// </summary>
    private class BoundingBoxInfo
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }
    
    /// <summary>
    /// Helper class for deserializing diagnostic data from JavaScript.
    /// </summary>
    private class DiagnosticData
    {
        public string? TagName { get; set; }
        public string? DomPath { get; set; }
        public bool IsVisible { get; set; }
        public DiagnosticBoundingBox? BoundingBox { get; set; }
        public string? OuterHtml { get; set; }
        public string? ParentTag { get; set; }
    }
    
    private class DiagnosticBoundingBox
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    public void Dispose()
    {
        _pauseEvent.Dispose();
        _cts?.Dispose();
    }
}

