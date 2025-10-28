using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CsvHelper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Core.Repositories;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Services;

public class CsvExportService(ILogger<CsvExportService> logger, IServiceProvider serviceProvider) : ICsvExportService
{
    private readonly ILogger<CsvExportService> _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public async Task ExportUrlInventoryAsync(int projectId, string filePath)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var urlRepository = scope.ServiceProvider.GetRequiredService<IUrlRepository>();
            var urls = (await urlRepository.GetByProjectIdAsync(projectId)).ToList();

            using var writer = new StreamWriter(filePath);
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

            // Write headers
            csv.WriteField("URL");
            csv.WriteField("Status Code");
            csv.WriteField("Content Type");
            csv.WriteField("Content Length");
            csv.WriteField("Depth");
            csv.WriteField("Crawled At");
            csv.NextRecord();

            // Write data
            foreach (var url in urls)
            {
                csv.WriteField(url.Address);
                csv.WriteField(url.HttpStatus);
                csv.WriteField(url.ContentType);
                csv.WriteField(url.ContentLength);
                csv.WriteField(url.Depth);
                csv.WriteField(url.LastCrawledUtc?.ToString("yyyy-MM-dd HH:mm:ss"));
                csv.NextRecord();
            }

            _logger.LogInformation("Exported {Count} URLs to {FilePath}", urls.Count, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export URLs to CSV");
            throw;
        }
    }
    
    public async Task ExportFindingsAsync(int projectId, string filePath, bool includeTechnicalMetadata = false)
    {
        try
        {
            _logger.LogInformation("Exporting findings to CSV: {FilePath} (Include Technical Metadata: {IncludeTechnical})", 
                filePath, includeTechnicalMetadata);
            
            using var scope = _serviceProvider.CreateScope();
            var findingRepository = scope.ServiceProvider.GetRequiredService<IFindingRepository>();
            var findings = (await findingRepository.GetByProjectIdAsync(projectId)).ToList();
            
            if (findings.Count == 0)
            {
                _logger.LogWarning("No findings to export for project {ProjectId}", projectId);
                // Still create an empty CSV file
            }
            
            if (findings.Count > 50000)
            {
                _logger.LogWarning("Large CSV export detected: {Count} findings. This may take some time.", findings.Count);
            }

            using var writer = new StreamWriter(filePath);
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

            // Write headers
            csv.WriteField("URL");
            csv.WriteField("Severity");
            csv.WriteField("Code");
            csv.WriteField("Message");
            csv.WriteField("Details");
            
            if (includeTechnicalMetadata)
            {
                csv.WriteField("Technical Data");
            }
            
            csv.WriteField("Date");
            csv.NextRecord();

            // Write data
            foreach (var finding in findings.OrderByDescending(f => f.Severity).ThenByDescending(f => f.CreatedUtc))
            {
                csv.WriteField(finding.Url?.Address ?? "");
                csv.WriteField(finding.Severity.ToString());
                csv.WriteField(finding.Code);
                csv.WriteField(finding.Message);
                
                // Format structured details
                FindingDetails? details = null;
                string detailsText = "";
                try
                {
                    details = finding.GetDetails();
                    detailsText = FormatFindingDetailsForCsv(details);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error parsing finding details for finding {FindingId}", finding.Id);
                    detailsText = "[Error parsing details]";
                }
                
                csv.WriteField(detailsText);
                
                // Include technical metadata if requested
                if (includeTechnicalMetadata)
                {
                    string technicalData = "";
                    try
                    {
                        technicalData = FormatTechnicalMetadataForCsv(details);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error formatting technical metadata for finding {FindingId}", finding.Id);
                        technicalData = "[Error formatting metadata]";
                    }
                    csv.WriteField(technicalData);
                }
                
                csv.WriteField(finding.CreatedUtc.ToString("yyyy-MM-dd HH:mm:ss"));
                csv.NextRecord();
            }

            _logger.LogInformation("Exported {Count} findings to {FilePath}", findings.Count, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export findings to CSV");
            throw;
        }
    }
    
    /// <summary>
    /// Formats FindingDetails as readable text for CSV with semicolon-separated nested items.
    /// </summary>
    private string FormatFindingDetailsForCsv(FindingDetails? details)
    {
        if (details == null || details.Items.Count == 0)
        {
            return "";
        }
        
        var lines = new List<string>();
        FormatDetailItemsForCsv(details.Items, 0, lines);
        return string.Join(" | ", lines); // Use pipe separator for readability in CSV
    }
    
    /// <summary>
    /// Recursively formats detail items for CSV.
    /// </summary>
    private void FormatDetailItemsForCsv(List<FindingDetail> items, int indentLevel, List<string> lines)
    {
        // Guard against excessive nesting to prevent stack overflow
        if (indentLevel > 10)
        {
            lines.Add($"{new string(' ', indentLevel * 2)}[Max nesting depth reached]");
            return;
        }
        
        foreach (var item in items)
        {
            var indent = new string(' ', indentLevel * 2);
            var bullet = indentLevel > 0 ? "• " : "";
            lines.Add($"{indent}{bullet}{item.Text}");
            
            if (item.Children != null && item.Children.Count > 0)
            {
                FormatDetailItemsForCsv(item.Children, indentLevel + 1, lines);
            }
        }
    }
    
    /// <summary>
    /// Formats technical metadata as compact JSON for CSV.
    /// </summary>
    private string FormatTechnicalMetadataForCsv(FindingDetails? details)
    {
        if (details?.TechnicalMetadata == null || details.TechnicalMetadata.Count == 0)
        {
            return "";
        }
        
        try
        {
            // Use compact JSON for CSV (no indentation)
            return System.Text.Json.JsonSerializer.Serialize(
                details.TechnicalMetadata,
                JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error formatting technical metadata for CSV export");
            return "[Error formatting technical metadata]";
        }
    }
}

