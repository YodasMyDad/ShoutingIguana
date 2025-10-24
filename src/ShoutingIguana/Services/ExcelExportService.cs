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

    public async Task<bool> ExportFindingsAsync(int projectId, string filePath)
    {
        try
        {
            _logger.LogInformation("Exporting findings to Excel: {FilePath}", filePath);

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
            var allFindings = await findingRepo.GetByProjectIdAsync(projectId);
            
            using var workbook = new XLWorkbook();

            // Group findings by task key
            var findingsByTask = allFindings.GroupBy(f => f.TaskKey);

            // Create summary sheet
            CreateSummarySheet(workbook, project.Name, allFindings, findingsByTask);

            // Create a sheet for each plugin's findings
            foreach (var taskGroup in findingsByTask.OrderBy(g => g.Key))
            {
                CreateTaskSheet(workbook, taskGroup.Key, taskGroup.ToList());
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

    private void CreateTaskSheet(XLWorkbook workbook, string taskKey, List<Core.Models.Finding> findings)
    {
        // Sanitize sheet name (Excel has restrictions)
        var sheetName = SanitizeSheetName(taskKey);
        var ws = workbook.Worksheets.Add(sheetName);

        // Headers
        ws.Cell(1, 1).Value = "URL";
        ws.Cell(1, 2).Value = "Severity";
        ws.Cell(1, 3).Value = "Code";
        ws.Cell(1, 4).Value = "Message";
        ws.Cell(1, 5).Value = "Date";
        
        ws.Range(1, 1, 1, 5).Style.Font.Bold = true;
        ws.Range(1, 1, 1, 5).Style.Fill.BackgroundColor = XLColor.LightBlue;
        ws.Row(1).Height = 20;

        // Data
        var row = 2;
        foreach (var finding in findings.OrderByDescending(f => f.Severity).ThenByDescending(f => f.CreatedUtc))
        {
            ws.Cell(row, 1).Value = finding.Url?.Address ?? "";
            ws.Cell(row, 2).Value = finding.Severity.ToString();
            ws.Cell(row, 3).Value = finding.Code;
            ws.Cell(row, 4).Value = finding.Message;
            ws.Cell(row, 5).Value = finding.CreatedUtc.ToString("yyyy-MM-dd HH:mm:ss");

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
            ws.Range(1, 1, row - 1, 5).SetAutoFilter();
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

