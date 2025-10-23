using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CsvHelper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Core.Repositories;

namespace ShoutingIguana.Services;

public class CsvExportService(ILogger<CsvExportService> logger, IServiceProvider serviceProvider) : ICsvExportService
{
    private readonly ILogger<CsvExportService> _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    public async Task ExportUrlInventoryAsync(int projectId, string filePath)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var urlRepository = scope.ServiceProvider.GetRequiredService<IUrlRepository>();
            var urls = await urlRepository.GetByProjectIdAsync(projectId);

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

            _logger.LogInformation("Exported {Count} URLs to {FilePath}", urls.Count(), filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export URLs to CSV");
            throw;
        }
    }
}

