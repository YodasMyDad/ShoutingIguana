using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;
using ShoutingIguana.Core.Services.Models;

namespace ShoutingIguana.Core.Services;

/// <summary>
/// Implementation of IListModeService for importing URL lists from CSV files.
/// </summary>
public class ListModeService(ILogger<ListModeService> logger, IServiceProvider serviceProvider) : IListModeService
{
    private readonly ILogger<ListModeService> _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    public async Task<ListModeImportResult> ImportUrlListAsync(
        int projectId,
        string csvFilePath,
        bool followDiscoveredLinks = false,
        int priority = 1000,
        IProgress<string>? progress = null)
    {
        var result = new ListModeImportResult();
        
        try
        {
            if (!File.Exists(csvFilePath))
            {
                result.ErrorMessage = "CSV file not found";
                return result;
            }

            _logger.LogInformation("Importing URL list from: {CsvFilePath}", csvFilePath);
            progress?.Report("Reading CSV file...");

            var urls = new List<UrlImportRecord>();
            
            using (var reader = new StreamReader(csvFilePath))
            using (var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                MissingFieldFound = null,
                BadDataFound = null
            }))
            {
                await foreach (var record in csv.GetRecordsAsync<UrlImportRecord>())
                {
                    if (!string.IsNullOrWhiteSpace(record.Url))
                    {
                        urls.Add(record);
                    }
                }
            }

            _logger.LogInformation("Read {Count} URLs from CSV", urls.Count);
            progress?.Report($"Validating {urls.Count} URLs...");

            // Validate and import URLs
            using var scope = _serviceProvider.CreateScope();
            var queueRepository = scope.ServiceProvider.GetRequiredService<ICrawlQueueRepository>();

            foreach (var record in urls)
            {
                try
                {
                    // Validate URL
                    if (!Uri.TryCreate(record.Url, UriKind.Absolute, out var uri))
                    {
                        _logger.LogWarning("Invalid URL skipped: {Url}", record.Url);
                        result.InvalidCount++;
                        result.Errors.Add($"Invalid URL: {record.Url}");
                        continue;
                    }

                    // Normalize URL
                    var normalizedUrl = uri.ToString();

                    // Check if already in queue
                    var existing = await queueRepository.GetByAddressAsync(projectId, normalizedUrl);
                    if (existing != null)
                    {
                        _logger.LogDebug("URL already in queue, skipping: {Url}", normalizedUrl);
                        result.SkippedCount++;
                        continue;
                    }

                    // Add to queue with high priority
                    var queueItem = new CrawlQueueItem
                    {
                        ProjectId = projectId,
                        Address = normalizedUrl,
                        Depth = 0, // List-mode URLs treated as depth 0 (like seeds)
                        Priority = record.Priority ?? priority,
                        State = QueueState.Queued,
                        HostKey = uri.Host.ToLowerInvariant(),
                        EnqueuedUtc = DateTime.UtcNow
                    };

                    await queueRepository.CreateAsync(queueItem);
                    result.ImportedCount++;

                    if (result.ImportedCount % 10 == 0)
                    {
                        progress?.Report($"Imported {result.ImportedCount}/{urls.Count} URLs...");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error importing URL: {Url}", record.Url);
                    result.InvalidCount++;
                    result.Errors.Add($"Error importing {record.Url}: {ex.Message}");
                }
            }

            result.Success = true;
            _logger.LogInformation("Import complete: {Imported} imported, {Skipped} skipped, {Invalid} invalid",
                result.ImportedCount, result.SkippedCount, result.InvalidCount);
            
            progress?.Report($"✓ Imported {result.ImportedCount} URLs");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import URL list");
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }
}

