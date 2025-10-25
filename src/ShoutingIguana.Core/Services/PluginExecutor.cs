using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Microsoft.Extensions.DependencyInjection;
using ShoutingIguana.Core.Browser;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;
using ShoutingIguana.PluginSdk;
using System.Text.Json;

namespace ShoutingIguana.Core.Services;

/// <summary>
/// Executes plugin tasks for crawled URLs.
/// </summary>
public class PluginExecutor(
    ILogger<PluginExecutor> logger,
    IPluginRegistry pluginRegistry,
    IServiceProvider serviceProvider)
{
    private readonly ILogger<PluginExecutor> _logger = logger;
    private readonly IPluginRegistry _pluginRegistry = pluginRegistry;
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    public async Task ExecuteTasksAsync(
        Url urlEntity,
        IPage? page,
        string? renderedHtml,
        Dictionary<string, string> headers,
        Configuration.ProjectSettings projectSettings,
        string userAgent,
        int projectId,
        CancellationToken cancellationToken)
    {
        var tasks = _pluginRegistry.GetTasksByPriority();
        if (tasks.Count == 0)
        {
            _logger.LogWarning("No plugin tasks registered");
            return;
        }

        _logger.LogDebug("Executing {Count} plugin tasks for {Url}", tasks.Count, urlEntity.Address);

        // Create context
        IBrowserPage? browserPage = page != null ? new BrowserPage(page) : null;
        
        var findingSink = new FindingSink(urlEntity.Id, projectId, _serviceProvider, _logger);
        
        var urlContext = new UrlContext(
            Url: new Uri(urlEntity.Address),
            Page: browserPage,
            HttpResponse: null, // We don't pass HttpResponse in Stage 2
            RenderedHtml: renderedHtml,
            Headers: headers,
            Project: new PluginSdk.ProjectSettings(
                ProjectId: projectId,
                BaseUrl: projectSettings.BaseUrl,
                MaxDepth: projectSettings.MaxCrawlDepth,
                UserAgent: userAgent,
                RespectRobotsTxt: projectSettings.RespectRobotsTxt),
            Metadata: new UrlMetadata(
                UrlId: urlEntity.Id,
                StatusCode: urlEntity.HttpStatus ?? 0,
                ContentType: urlEntity.ContentType,
                ContentLength: urlEntity.ContentLength,
                Depth: urlEntity.Depth,
                CrawledUtc: urlEntity.LastCrawledUtc ?? DateTime.UtcNow),
            Findings: findingSink,
            Enqueue: new UrlEnqueueStub(), // Not implemented in Stage 2
            Logger: _logger);

        // Execute each task with proper cleanup
        try
        {
            foreach (var task in tasks)
            {
                try
                {
                    // Check for cancellation before executing each task
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    _logger.LogDebug("Executing task: {TaskName}", task.DisplayName);
                    await task.ExecuteAsync(urlContext, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Task execution cancelled for {Url}", urlEntity.Address);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing task {TaskName} for {Url}", task.DisplayName, urlEntity.Address);
                }
            }
        }
        finally
        {
            // Always flush findings, even if an error occurred
            await findingSink.FlushAsync();
        }
    }
}

/// <summary>
/// Implementation of IFindingSink that batches findings for better performance.
/// </summary>
internal class FindingSink(int urlId, int projectId, IServiceProvider serviceProvider, ILogger logger) : IFindingSink
{
    private readonly int _urlId = urlId;
    private readonly int _projectId = projectId;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger _logger = logger;
    private readonly List<Finding> _pendingFindings = [];

    public async Task ReportAsync(string taskKey, Severity severity, string code, string message, object? data = null)
    {
        try
        {
            var finding = new Finding
            {
                ProjectId = _projectId,
                UrlId = _urlId,
                TaskKey = taskKey,
                Severity = severity,
                Code = code,
                Message = message,
                DataJson = data != null ? JsonSerializer.Serialize(data) : null,
                CreatedUtc = DateTime.UtcNow
            };

            // Batch findings - don't save immediately
            _pendingFindings.Add(finding);
            _logger.LogDebug("Finding queued: {Code} - {Message}", code, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error queueing finding: {Code}", code);
        }
        
        await Task.CompletedTask;
    }
    
    /// <summary>
    /// Flush all pending findings to database in a single transaction.
    /// </summary>
    public async Task FlushAsync()
    {
        if (_pendingFindings.Count == 0)
            return;
            
        try
        {
            // Create a new scope to get a fresh DbContext
            using var scope = _serviceProvider.CreateScope();
            var findingRepository = scope.ServiceProvider.GetRequiredService<IFindingRepository>();
            
            // Use the batch insert method which handles transactions internally
            await findingRepository.CreateBatchAsync(_pendingFindings);
            
            _logger.LogDebug("Flushed {Count} findings to database", _pendingFindings.Count);
            _pendingFindings.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing findings to database");
            // Findings remain in _pendingFindings for potential retry or investigation
        }
    }
}

/// <summary>
/// Stub implementation of IUrlEnqueue (not implemented in Stage 2).
/// </summary>
internal class UrlEnqueueStub : IUrlEnqueue
{
    public Task EnqueueAsync(string url, int depth, int priority = 100)
    {
        // Not implemented in Stage 2
        return Task.CompletedTask;
    }
}

