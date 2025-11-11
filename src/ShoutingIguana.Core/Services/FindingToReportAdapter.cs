using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Core.Services;

/// <summary>
/// Adapter service that automatically creates default report schemas for plugins using the legacy IFindingSink.
/// Ensures backward compatibility by converting Finding-based reports to the new ReportRow system.
/// </summary>
public class FindingToReportAdapter
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FindingToReportAdapter> _logger;
    private readonly HashSet<string> _ensuredSchemas = new();
    private readonly object _lock = new();

    public FindingToReportAdapter(IServiceProvider serviceProvider, ILogger<FindingToReportAdapter> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Ensures a default report schema exists for the given task key.
    /// Called automatically when a finding is reported for a task.
    /// </summary>
    public async Task EnsureDefaultSchemaAsync(string taskKey)
    {
        // Check if already ensured (fast path)
        lock (_lock)
        {
            if (_ensuredSchemas.Contains(taskKey))
                return;
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var schemaRepository = scope.ServiceProvider.GetRequiredService<IReportSchemaRepository>();

            // Check if schema already exists in database
            var existingSchema = await schemaRepository.GetByTaskKeyAsync(taskKey);
            if (existingSchema != null)
            {
                lock (_lock)
                {
                    _ensuredSchemas.Add(taskKey);
                }
                return;
            }

            // Create default schema that mirrors the Finding structure
            var schema = new ShoutingIguana.Core.Models.ReportSchema
            {
                TaskKey = taskKey,
                SchemaVersion = 1,
                IsUrlBased = true,
                CreatedUtc = DateTime.UtcNow
            };

            var columns = new List<ReportColumnDefinition>
            {
                new() { Name = "Severity", DisplayName = "Severity", ColumnType = (int)ReportColumnType.String, Width = 120, IsSortable = true, IsFilterable = true, IsPrimaryKey = false },
                new() { Name = "Message", DisplayName = "Message", ColumnType = (int)ReportColumnType.String, Width = 400, IsSortable = true, IsFilterable = true, IsPrimaryKey = true },
                new() { Name = "URL", DisplayName = "URL", ColumnType = (int)ReportColumnType.Url, Width = 400, IsSortable = true, IsFilterable = true, IsPrimaryKey = false },
                new() { Name = "Code", DisplayName = "Code", ColumnType = (int)ReportColumnType.String, Width = 150, IsSortable = true, IsFilterable = true, IsPrimaryKey = false },
                new() { Name = "Date", DisplayName = "Date", ColumnType = (int)ReportColumnType.DateTime, Width = 150, IsSortable = true, IsFilterable = false, IsPrimaryKey = false }
            };

            schema.SetColumns(columns);

            await schemaRepository.CreateAsync(schema);
            _logger.LogInformation("Created default report schema for task: {TaskKey}", taskKey);

            lock (_lock)
            {
                _ensuredSchemas.Add(taskKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring default schema for task: {TaskKey}", taskKey);
        }
    }

    /// <summary>
    /// Converts a Finding to a ReportRow using the default schema.
    /// </summary>
    public ShoutingIguana.Core.Models.ReportRow ConvertFindingToReportRow(Finding finding)
    {
        var reportRow = new ShoutingIguana.Core.Models.ReportRow
        {
            ProjectId = finding.ProjectId,
            TaskKey = finding.TaskKey,
            UrlId = finding.UrlId,
            CreatedUtc = finding.CreatedUtc
        };

        var data = new Dictionary<string, object?>
        {
            ["Severity"] = finding.Severity.ToString(),
            ["Message"] = finding.Message,
            ["URL"] = finding.Url?.Address ?? string.Empty,
            ["Code"] = finding.Code,
            ["Date"] = finding.CreatedUtc
        };

        // Include finding details if available
        var details = finding.GetDetails();
        if (details != null)
        {
            // Flatten details into a string representation
            if (details.Items.Count > 0)
            {
                var detailsText = string.Join(" | ", details.Items);
                data["Details"] = detailsText;
            }
        }

        reportRow.SetData(data);
        return reportRow;
    }
}

