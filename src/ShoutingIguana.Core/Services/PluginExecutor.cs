using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Microsoft.Extensions.DependencyInjection;
using ShoutingIguana.Core.Browser;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Core.Services;

/// <summary>
/// Executes plugin tasks for crawled URLs.
/// </summary>
public class PluginExecutor(
    ILogger<PluginExecutor> logger,
    IPluginRegistry pluginRegistry,
    IServiceProvider serviceProvider)
{
    public async Task ExecuteTasksAsync(
        UrlAnalysisDto urlData,
        IPage? page,
        string? renderedHtml,
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        Configuration.ProjectSettings projectSettings,
        string userAgent,
        int projectId,
        CancellationToken cancellationToken)
    {
        var tasks = pluginRegistry.GetTasksByPriority();
        if (tasks.Count == 0)
        {
            logger.LogWarning("No plugin tasks registered");
            return;
        }

        logger.LogDebug("Executing {Count} plugin tasks for {Url}", tasks.Count, urlData.Address);

        // Create context
        IBrowserPage? browserPage = page != null ? new BrowserPage(page) : null;
        
        var reportSink = new ReportSink(urlData.Id, projectId, serviceProvider, logger);
        
        var urlContext = new UrlContext(
            Url: new Uri(urlData.Address),
            Page: browserPage,
            HttpResponse: BuildResponseInfo(urlData, headers),
            RenderedHtml: renderedHtml,
            Headers: headers,
            Project: new ProjectSettings(
                ProjectId: projectId,
                BaseUrl: projectSettings.BaseUrl,
                MaxDepth: projectSettings.MaxCrawlDepth,
                UserAgent: userAgent,
                RespectRobotsTxt: projectSettings.RespectRobotsTxt,
                UseSitemapXml: projectSettings.UseSitemapXml),
            Metadata: new UrlMetadata(
                UrlId: urlData.Id,
                StatusCode: urlData.HttpStatus ?? 0,
                ContentType: urlData.ContentType,
                ContentLength: urlData.ContentLength,
                Depth: urlData.Depth,
                CrawledUtc: urlData.LastCrawledUtc ?? DateTime.UtcNow,
                CanonicalHtml: urlData.CanonicalHtml,
                CanonicalHttp: urlData.CanonicalHttp,
                HasMultipleCanonicals: urlData.HasMultipleCanonicals,
                HasCrossDomainCanonical: urlData.HasCrossDomainCanonical,
                RobotsNoindex: urlData.RobotsNoindex,
                RobotsNofollow: urlData.RobotsNofollow,
                XRobotsTag: urlData.XRobotsTag,
                HasRobotsConflict: urlData.HasRobotsConflict,
                HasMetaRefresh: urlData.HasMetaRefresh,
                MetaRefreshDelay: urlData.MetaRefreshDelay,
                MetaRefreshTarget: urlData.MetaRefreshTarget,
                HtmlLang: urlData.HtmlLang,
                IsRedirectLoop: urlData.IsRedirectLoop),
            Reports: reportSink,
            Enqueue: new UrlEnqueueStub(), // Not implemented in Stage 2
            Logger: logger);

        // Execute each task with proper cleanup
        try
        {
            foreach (var task in tasks)
            {
                try
                {
                    // Check for cancellation before executing each task
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    logger.LogDebug("Executing task: {TaskName}", task.DisplayName);
                    await task.ExecuteAsync(urlContext, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    logger.LogInformation("Task execution cancelled for {Url}", urlData.Address);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error executing task {TaskName} for {Url}", task.DisplayName, urlData.Address);
                }
            }
        }
        finally
        {
            // Always flush findings and reports, even if an error occurred
            await reportSink.FlushAsync().ConfigureAwait(false);
        }
    }

    private static UrlResponseInfo? BuildResponseInfo(UrlAnalysisDto urlData, IReadOnlyDictionary<string, IReadOnlyList<string>> headers)
    {
        if (urlData.HttpStatus is null || !Uri.TryCreate(urlData.Address, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return new UrlResponseInfo(
            Scheme: uri.Scheme,
            Host: uri.Host,
            StatusCode: urlData.HttpStatus.Value,
            Headers: headers,
            ContentType: urlData.ContentType,
            BytesRead: urlData.ContentLength ?? 0);
    }
}

/// <summary>
/// Implementation of IReportSink that batches report rows for better performance.
/// </summary>
internal class ReportSink(int? urlId, int projectId, IServiceProvider serviceProvider, ILogger logger) : IReportSink
{
    private readonly List<ShoutingIguana.Core.Models.ReportRow> _pendingRows = [];
    private const int IssueTextMaxLength = 512;

    public async Task ReportAsync(string taskKey, PluginSdk.ReportRow row, int? overrideUrlId = null, CancellationToken ct = default)
    {
        try
        {
            var reportRow = new ShoutingIguana.Core.Models.ReportRow
            {
                ProjectId = projectId,
                TaskKey = taskKey,
                UrlId = overrideUrlId ?? urlId,
                CreatedUtc = DateTime.UtcNow
            };
            
            // Set row data using the helper method
            reportRow.SetData(row);
            reportRow.Severity = ExtractSeverity(row);
            reportRow.IssueText = ExtractIssueText(row);

            // Batch rows - don't save immediately
            _pendingRows.Add(reportRow);
            logger.LogDebug("Report row queued for task: {TaskKey}", taskKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error queueing report row for task: {TaskKey}", taskKey);
        }
        
        await Task.CompletedTask.ConfigureAwait(false);
    }
    
    /// <summary>
    /// Flush all pending report rows to database in a single transaction.
    /// </summary>
    public async Task FlushAsync()
    {
        if (_pendingRows.Count == 0)
            return;
            
        try
        {
            // Create a new scope to get a fresh DbContext
            using var scope = serviceProvider.CreateScope();
            var reportRepository = scope.ServiceProvider.GetRequiredService<IReportDataRepository>();
            
            // Use the batch insert method which handles transactions internally
            await reportRepository.CreateBatchAsync(_pendingRows).ConfigureAwait(false);
            
            logger.LogDebug("Flushed {Count} report rows to database", _pendingRows.Count);
            _pendingRows.Clear();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error flushing report rows to database");
            // Rows remain in _pendingRows for potential retry or investigation
        }
    }

    private static PluginSdk.Severity? ExtractSeverity(PluginSdk.ReportRow row)
    {
        var severityValue = row.Get("Severity");
        if (severityValue == null)
        {
            return null;
        }

        if (severityValue is PluginSdk.Severity severityEnum)
        {
            return severityEnum;
        }

        if (severityValue is string severityText &&
            Enum.TryParse<PluginSdk.Severity>(severityText, true, out var parsedSeverity))
        {
            return parsedSeverity;
        }

        if (severityValue is int severityInt &&
            Enum.IsDefined(typeof(PluginSdk.Severity), severityInt))
        {
            return (PluginSdk.Severity)severityInt;
        }

        if (severityValue is IConvertible convertible)
        {
            var converted = convertible.ToInt32(null);
            if (Enum.IsDefined(typeof(PluginSdk.Severity), converted))
            {
                return (PluginSdk.Severity)converted;
            }
        }

        return null;
    }

    private static string? ExtractIssueText(PluginSdk.ReportRow row)
    {
        var candidates = new[] { "Issue", "Message", "Description", "Explanation" };

        foreach (var candidate in candidates)
        {
            var value = row.Get(candidate);
            var normalized = NormalizeIssueText(value);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized.Length <= IssueTextMaxLength
                    ? normalized
                    : normalized[..IssueTextMaxLength];
            }
        }

        return null;
    }

    private static string? NormalizeIssueText(object? value)
    {
        if (value == null)
        {
            return null;
        }

        return value switch
        {
            string text => text.Trim(),
            Uri uri => uri.ToString(),
            IFormattable formattable => formattable.ToString(null, null)?.Trim(),
            _ => value.ToString()?.Trim()
        };
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

