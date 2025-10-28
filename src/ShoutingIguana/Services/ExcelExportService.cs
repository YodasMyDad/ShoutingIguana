using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Core.Repositories;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Services;

public class ExcelExportService(
    ILogger<ExcelExportService> logger,
    IServiceProvider serviceProvider) : IExcelExportService
{
    private readonly ILogger<ExcelExportService> _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<bool> ExportFindingsAsync(int projectId, string filePath, bool includeTechnicalMetadata = false)
    {
        try
        {
            _logger.LogInformation("Exporting findings to Excel: {FilePath} (Include Technical Metadata: {IncludeTechnical})", 
                filePath, includeTechnicalMetadata);

            // Create scope for repositories
            using var scope = _serviceProvider.CreateScope();
            var findingRepo = scope.ServiceProvider.GetRequiredService<IFindingRepository>();
            var projectRepo = scope.ServiceProvider.GetRequiredService<IProjectRepository>();

            // Get project info
            var project = await projectRepo.GetByIdAsync(projectId);
            if (project == null)
            {
                _logger.LogError("Project not found: {ProjectId}", projectId);
                return false;
            }

            // Get all findings
            var allFindings = (await findingRepo.GetByProjectIdAsync(projectId)).ToList();
            
            if (allFindings.Count == 0)
            {
                _logger.LogWarning("No findings to export for project {ProjectId}", projectId);
                return false;
            }
            
            if (allFindings.Count > 50000)
            {
                _logger.LogWarning("Large export detected: {Count} findings. This may take some time.", allFindings.Count);
            }
            
            using var workbook = new XLWorkbook();

            // Group findings by task key
            var findingsByTask = allFindings.GroupBy(f => f.TaskKey).ToList();

            // Create summary sheet
            CreateSummarySheet(workbook, project.Name, allFindings, findingsByTask);

            // Create a sheet for each plugin's findings
            foreach (var taskGroup in findingsByTask.OrderBy(g => g.Key))
            {
                CreateTaskSheet(workbook, taskGroup.Key, taskGroup.ToList(), includeTechnicalMetadata);
            }

            // Ensure directory exists
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Save workbook
            workbook.SaveAs(filePath);

            _logger.LogInformation("Excel export completed: {Count} findings exported to {FilePath}", 
                allFindings.Count, filePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting to Excel");
            return false;
        }
    }

    private void CreateSummarySheet(XLWorkbook workbook, string projectName, List<Core.Models.Finding> allFindings, IEnumerable<IGrouping<string, Core.Models.Finding>> findingsByTask)
    {
        var ws = workbook.Worksheets.Add("Summary");

        // Project info
        ws.Cell(1, 1).Value = "Project:";
        ws.Cell(1, 2).Value = projectName;
        ws.Cell(1, 1).Style.Font.Bold = true;

        ws.Cell(2, 1).Value = "Export Date:";
        ws.Cell(2, 2).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        ws.Cell(2, 1).Style.Font.Bold = true;

        ws.Cell(3, 1).Value = "Total Findings:";
        ws.Cell(3, 2).Value = allFindings.Count;
        ws.Cell(3, 1).Style.Font.Bold = true;

        // Summary by task
        ws.Cell(5, 1).Value = "Findings by Plugin";
        ws.Cell(5, 1).Style.Font.Bold = true;
        ws.Cell(5, 1).Style.Font.FontSize = 14;

        var row = 7;
        ws.Cell(row, 1).Value = "Plugin";
        ws.Cell(row, 2).Value = "Total";
        ws.Cell(row, 3).Value = "Errors";
        ws.Cell(row, 4).Value = "Warnings";
        ws.Cell(row, 5).Value = "Info";
        ws.Range(row, 1, row, 5).Style.Font.Bold = true;
        ws.Range(row, 1, row, 5).Style.Fill.BackgroundColor = XLColor.LightGray;

        row++;
        foreach (var taskGroup in findingsByTask.OrderBy(g => g.Key))
        {
            ws.Cell(row, 1).Value = taskGroup.Key;
            ws.Cell(row, 2).Value = taskGroup.Count();
            ws.Cell(row, 3).Value = taskGroup.Count(f => f.Severity == Severity.Error);
            ws.Cell(row, 4).Value = taskGroup.Count(f => f.Severity == Severity.Warning);
            ws.Cell(row, 5).Value = taskGroup.Count(f => f.Severity == Severity.Info);
            row++;
        }

        // Auto-fit columns
        ws.Columns().AdjustToContents();
    }

    private void CreateTaskSheet(XLWorkbook workbook, string taskKey, List<Core.Models.Finding> findings, bool includeTechnicalMetadata)
    {
        // Sanitize sheet name (Excel has restrictions)
        var sheetName = SanitizeSheetName(taskKey);
        var ws = workbook.Worksheets.Add(sheetName);

        // Headers - add Details column
        var colIndex = 1;
        ws.Cell(1, colIndex++).Value = "URL";
        ws.Cell(1, colIndex++).Value = "Severity";
        ws.Cell(1, colIndex++).Value = "Code";
        ws.Cell(1, colIndex++).Value = "Message";
        ws.Cell(1, colIndex++).Value = "Details";
        
        var technicalColIndex = 0;
        if (includeTechnicalMetadata)
        {
            technicalColIndex = colIndex;
            ws.Cell(1, colIndex++).Value = "Technical Data";
        }
        
        ws.Cell(1, colIndex++).Value = "Date";
        
        var headerEndCol = colIndex - 1;
        ws.Range(1, 1, 1, headerEndCol).Style.Font.Bold = true;
        ws.Range(1, 1, 1, headerEndCol).Style.Fill.BackgroundColor = XLColor.LightBlue;
        ws.Row(1).Height = 20;

        // Data
        var row = 2;
        foreach (var finding in findings.OrderByDescending(f => f.Severity).ThenByDescending(f => f.CreatedUtc))
        {
            colIndex = 1;
            ws.Cell(row, colIndex++).Value = finding.Url?.Address ?? "";
            ws.Cell(row, colIndex++).Value = finding.Severity.ToString();
            ws.Cell(row, colIndex++).Value = finding.Code;
            ws.Cell(row, colIndex++).Value = finding.Message;
            
            // Add formatted structured details
            FindingDetails? details = null;
            string detailsText = "";
            try
            {
                details = finding.GetDetails();
                detailsText = FormatFindingDetailsForExcel(details);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing finding details for finding {FindingId}", finding.Id);
                detailsText = "[Error parsing details]";
            }
            
            var detailsCell = ws.Cell(row, colIndex++);
            detailsCell.Value = detailsText;
            detailsCell.Style.Alignment.WrapText = true;
            detailsCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            
            // Set row height to auto for wrapped content
            if (!string.IsNullOrEmpty(detailsText))
            {
                ws.Row(row).AdjustToContents();
            }
            
            // Add technical metadata if requested
            if (includeTechnicalMetadata && technicalColIndex > 0)
            {
                var technicalData = FormatTechnicalMetadataForExcel(details);
                ws.Cell(row, technicalColIndex).Value = technicalData;
                ws.Cell(row, technicalColIndex).Style.Alignment.WrapText = true;
                ws.Cell(row, technicalColIndex).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                ws.Cell(row, technicalColIndex).Style.Font.FontName = "Consolas";
                ws.Cell(row, technicalColIndex).Style.Font.FontSize = 9;
            }
            
            ws.Cell(row, colIndex++).Value = finding.CreatedUtc.ToString("yyyy-MM-dd HH:mm:ss");

            // Color code by severity
            var severityCell = ws.Cell(row, 2);
            switch (finding.Severity)
            {
                case Severity.Error:
                    severityCell.Style.Fill.BackgroundColor = XLColor.LightPink;
                    severityCell.Style.Font.FontColor = XLColor.DarkRed;
                    break;
                case Severity.Warning:
                    severityCell.Style.Fill.BackgroundColor = XLColor.LightYellow;
                    severityCell.Style.Font.FontColor = XLColor.DarkOrange;
                    break;
                case Severity.Info:
                    severityCell.Style.Fill.BackgroundColor = XLColor.LightGreen;
                    severityCell.Style.Font.FontColor = XLColor.DarkGreen;
                    break;
            }

            row++;
        }

        // Auto-filter
        if (row > 2)
        {
            ws.Range(1, 1, row - 1, headerEndCol).SetAutoFilter();
        }

        // Auto-fit columns
        ws.Columns().AdjustToContents();
        
        // Set max column width to avoid extremely wide columns
        foreach (var column in ws.ColumnsUsed())
        {
            if (column.Width > 80)
            {
                column.Width = 80;
            }
        }

        // Freeze header row
        ws.SheetView.FreezeRows(1);
    }
    
    /// <summary>
    /// Formats FindingDetails as readable text for Excel with indentation for nested items.
    /// </summary>
    private string FormatFindingDetailsForExcel(FindingDetails? details)
    {
        if (details == null || details.Items.Count == 0)
        {
            return "";
        }
        
        var lines = new List<string>();
        FormatDetailItems(details.Items, 0, lines);
        return string.Join("\n", lines);
    }
    
    /// <summary>
    /// Recursively formats detail items with indentation.
    /// </summary>
    private void FormatDetailItems(List<FindingDetail> items, int indentLevel, List<string> lines)
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
                FormatDetailItems(item.Children, indentLevel + 1, lines);
            }
        }
    }
    
    /// <summary>
    /// Formats technical metadata as JSON for Excel.
    /// </summary>
    private string FormatTechnicalMetadataForExcel(FindingDetails? details)
    {
        if (details?.TechnicalMetadata == null || details.TechnicalMetadata.Count == 0)
        {
            return "";
        }
        
        try
        {
            return System.Text.Json.JsonSerializer.Serialize(
                details.TechnicalMetadata,
                JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error formatting technical metadata for export");
            return "[Error formatting technical metadata]";
        }
    }

    private string SanitizeSheetName(string name)
    {
        // Excel sheet names can't be longer than 31 chars and can't contain: \ / ? * [ ]
        var invalidChars = new[] { '\\', '/', '?', '*', '[', ']', ':' };
        var sanitized = name;
        
        foreach (var c in invalidChars)
        {
            sanitized = sanitized.Replace(c, '_');
        }

        if (sanitized.Length > 31)
        {
            sanitized = sanitized.Substring(0, 31);
        }

        return sanitized;
    }
}

