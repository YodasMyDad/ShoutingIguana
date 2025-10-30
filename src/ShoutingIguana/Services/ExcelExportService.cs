using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using HtmlAgilityPack;
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

    public async Task<bool> ExportFindingsAsync(int projectId, string filePath, bool includeTechnicalMetadata = false, bool includeErrors = true, bool includeWarnings = true, bool includeInfo = true)
    {
        try
        {
            _logger.LogInformation("Exporting findings to Excel: {FilePath} (Include Technical Metadata: {IncludeTechnical}, Errors: {Errors}, Warnings: {Warnings}, Info: {Info})", 
                filePath, includeTechnicalMetadata, includeErrors, includeWarnings, includeInfo);

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
            
            // Apply severity filtering
            var filteredFindings = allFindings.Where(f => 
                (includeErrors && f.Severity == Severity.Error) ||
                (includeWarnings && f.Severity == Severity.Warning) ||
                (includeInfo && f.Severity == Severity.Info)
            ).ToList();
            
            if (filteredFindings.Count == 0)
            {
                _logger.LogWarning("No findings to export for project {ProjectId} after applying severity filters", projectId);
                return false;
            }
            
            if (filteredFindings.Count > 50000)
            {
                _logger.LogWarning("Large export detected: {Count} findings. This may take some time.", filteredFindings.Count);
            }
            
            using var workbook = new XLWorkbook();

            // Group findings by task key
            var findingsByTask = filteredFindings.GroupBy(f => f.TaskKey).ToList();

            // Create summary sheet
            CreateSummarySheet(workbook, project.Name, filteredFindings, findingsByTask);

            // Create a sheet for each plugin's findings (skip empty groups)
            foreach (var taskGroup in findingsByTask.OrderBy(g => g.Key))
            {
                var taskFindings = taskGroup.ToList();
                if (taskFindings.Count > 0)
                {
                    CreateTaskSheet(workbook, taskGroup.Key, taskFindings, includeTechnicalMetadata);
                }
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
                filteredFindings.Count, filePath);
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

    public async Task<bool> ExportUrlsAsync(int projectId, string filePath)
    {
        try
        {
            _logger.LogInformation("Exporting URLs to Excel: {FilePath}", filePath);

            // Create scope for repositories
            using var scope = _serviceProvider.CreateScope();
            var urlRepo = scope.ServiceProvider.GetRequiredService<IUrlRepository>();
            var projectRepo = scope.ServiceProvider.GetRequiredService<IProjectRepository>();

            // Get project info
            var project = await projectRepo.GetByIdAsync(projectId);
            if (project == null)
            {
                _logger.LogError("Project not found: {ProjectId}", projectId);
                return false;
            }

            // Get all URLs
            var allUrls = (await urlRepo.GetByProjectIdAsync(projectId)).ToList();
            
            if (allUrls.Count == 0)
            {
                _logger.LogWarning("No URLs to export for project {ProjectId}", projectId);
                return false;
            }
            
            if (allUrls.Count > 50000)
            {
                _logger.LogWarning("Large export detected: {Count} URLs. This may take some time.", allUrls.Count);
            }
            
            using var workbook = new XLWorkbook();

            // Create main URLs sheet with all data
            CreateUrlsSheet(workbook, allUrls);

            // Ensure directory exists
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Save workbook
            workbook.SaveAs(filePath);

            _logger.LogInformation("Excel export completed: {Count} URLs exported to {FilePath}", 
                allUrls.Count, filePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting URLs to Excel");
            return false;
        }
    }

    private void CreateUrlsSheet(XLWorkbook workbook, List<Core.Models.Url> urls)
    {
        var ws = workbook.Worksheets.Add("URLs");

        // Headers - Essential SEO data first, then technical details
        var colIndex = 1;
        
        // Essential SEO columns
        ws.Cell(1, colIndex++).Value = "Address";
        ws.Cell(1, colIndex++).Value = "Status Code";
        ws.Cell(1, colIndex++).Value = "Title";
        ws.Cell(1, colIndex++).Value = "Meta Description";
        ws.Cell(1, colIndex++).Value = "H1";
        ws.Cell(1, colIndex++).Value = "H2";
        ws.Cell(1, colIndex++).Value = "H3";
        ws.Cell(1, colIndex++).Value = "H4";
        ws.Cell(1, colIndex++).Value = "H5";
        ws.Cell(1, colIndex++).Value = "H6";
        
        // Content & Structure
        ws.Cell(1, colIndex++).Value = "Content Type";
        ws.Cell(1, colIndex++).Value = "Content Length";
        ws.Cell(1, colIndex++).Value = "Depth";
        ws.Cell(1, colIndex++).Value = "Status";
        
        // URLs & Structure
        ws.Cell(1, colIndex++).Value = "Scheme";
        ws.Cell(1, colIndex++).Value = "Host";
        ws.Cell(1, colIndex++).Value = "Path";
        
        // Canonical & Indexability
        ws.Cell(1, colIndex++).Value = "Canonical (HTML)";
        ws.Cell(1, colIndex++).Value = "Canonical (HTTP)";
        ws.Cell(1, colIndex++).Value = "Has Multiple Canonicals";
        ws.Cell(1, colIndex++).Value = "Has Cross-Domain Canonical";
        ws.Cell(1, colIndex++).Value = "Canonical Issues";
        
        // Robots & Indexability
        ws.Cell(1, colIndex++).Value = "Robots Allowed";
        ws.Cell(1, colIndex++).Value = "Noindex";
        ws.Cell(1, colIndex++).Value = "Nofollow";
        ws.Cell(1, colIndex++).Value = "Noarchive";
        ws.Cell(1, colIndex++).Value = "Nosnippet";
        ws.Cell(1, colIndex++).Value = "Noimageindex";
        ws.Cell(1, colIndex++).Value = "Robots Source";
        ws.Cell(1, colIndex++).Value = "X-Robots-Tag";
        ws.Cell(1, colIndex++).Value = "Has Robots Conflict";
        ws.Cell(1, colIndex++).Value = "Is Indexable";
        
        // Redirects
        ws.Cell(1, colIndex++).Value = "Redirect Target";
        ws.Cell(1, colIndex++).Value = "Is Redirect Loop";
        ws.Cell(1, colIndex++).Value = "Redirect Chain Length";
        ws.Cell(1, colIndex++).Value = "Is Soft 404";
        
        // Meta Refresh
        ws.Cell(1, colIndex++).Value = "Has Meta Refresh";
        ws.Cell(1, colIndex++).Value = "Meta Refresh Delay";
        ws.Cell(1, colIndex++).Value = "Meta Refresh Target";
        
        // JavaScript
        ws.Cell(1, colIndex++).Value = "Has JS Changes";
        ws.Cell(1, colIndex++).Value = "JS Changed Elements";
        
        // HTTP Headers
        ws.Cell(1, colIndex++).Value = "Cache-Control";
        ws.Cell(1, colIndex++).Value = "Vary";
        ws.Cell(1, colIndex++).Value = "Content-Encoding";
        ws.Cell(1, colIndex++).Value = "Link Header";
        ws.Cell(1, colIndex++).Value = "Has HSTS";
        
        // Language
        ws.Cell(1, colIndex++).Value = "HTML Lang";
        ws.Cell(1, colIndex++).Value = "Content-Language Header";
        
        // Content Analysis
        ws.Cell(1, colIndex++).Value = "Content Hash";
        ws.Cell(1, colIndex++).Value = "SimHash";
        
        // Timestamps
        ws.Cell(1, colIndex++).Value = "First Seen";
        ws.Cell(1, colIndex++).Value = "Last Crawled";
        
        var headerEndCol = colIndex - 1;
        ws.Range(1, 1, 1, headerEndCol).Style.Font.Bold = true;
        ws.Range(1, 1, 1, headerEndCol).Style.Fill.BackgroundColor = XLColor.LightBlue;
        ws.Row(1).Height = 20;

        // Data
        var row = 2;
        foreach (var url in urls.OrderBy(u => u.Address))
        {
            colIndex = 1;
            
            // Essential SEO
            ws.Cell(row, colIndex++).Value = url.Address;
            ws.Cell(row, colIndex++).Value = url.HttpStatus ?? 0;
            ws.Cell(row, colIndex++).Value = System.Net.WebUtility.HtmlDecode(url.Title ?? "");
            ws.Cell(row, colIndex++).Value = System.Net.WebUtility.HtmlDecode(url.MetaDescription ?? "");
            
            // Extract H1-H6 from RenderedHtml
            var (h1, h2, h3, h4, h5, h6) = ExtractHeadings(url.RenderedHtml);
            ws.Cell(row, colIndex++).Value = h1;
            ws.Cell(row, colIndex++).Value = h2;
            ws.Cell(row, colIndex++).Value = h3;
            ws.Cell(row, colIndex++).Value = h4;
            ws.Cell(row, colIndex++).Value = h5;
            ws.Cell(row, colIndex++).Value = h6;
            
            // Content & Structure
            ws.Cell(row, colIndex++).Value = url.ContentType ?? "";
            ws.Cell(row, colIndex++).Value = url.ContentLength ?? 0;
            ws.Cell(row, colIndex++).Value = url.Depth == -1 ? "External" : url.Depth.ToString();
            ws.Cell(row, colIndex++).Value = url.Status.ToString();
            
            // URLs & Structure
            ws.Cell(row, colIndex++).Value = url.Scheme;
            ws.Cell(row, colIndex++).Value = url.Host;
            ws.Cell(row, colIndex++).Value = url.Path;
            
            // Canonical & Indexability
            ws.Cell(row, colIndex++).Value = url.CanonicalHtml ?? "";
            ws.Cell(row, colIndex++).Value = url.CanonicalHttp ?? "";
            ws.Cell(row, colIndex++).Value = url.HasMultipleCanonicals ? "Yes" : "";
            ws.Cell(row, colIndex++).Value = url.HasCrossDomainCanonical ? "Yes" : "";
            ws.Cell(row, colIndex++).Value = url.CanonicalIssues ?? "";
            
            // Robots & Indexability
            ws.Cell(row, colIndex++).Value = url.RobotsAllowed.HasValue ? (url.RobotsAllowed.Value ? "Yes" : "No") : "";
            ws.Cell(row, colIndex++).Value = url.RobotsNoindex.HasValue ? (url.RobotsNoindex.Value ? "Yes" : "No") : "";
            ws.Cell(row, colIndex++).Value = url.RobotsNofollow.HasValue ? (url.RobotsNofollow.Value ? "Yes" : "No") : "";
            ws.Cell(row, colIndex++).Value = url.RobotsNoarchive.HasValue ? (url.RobotsNoarchive.Value ? "Yes" : "No") : "";
            ws.Cell(row, colIndex++).Value = url.RobotsNosnippet.HasValue ? (url.RobotsNosnippet.Value ? "Yes" : "No") : "";
            ws.Cell(row, colIndex++).Value = url.RobotsNoimageindex.HasValue ? (url.RobotsNoimageindex.Value ? "Yes" : "No") : "";
            ws.Cell(row, colIndex++).Value = url.RobotsSource ?? "";
            ws.Cell(row, colIndex++).Value = url.XRobotsTag ?? "";
            ws.Cell(row, colIndex++).Value = url.HasRobotsConflict ? "Yes" : "";
            ws.Cell(row, colIndex++).Value = url.IsIndexable.HasValue ? (url.IsIndexable.Value ? "Yes" : "No") : "";
            
            // Redirects
            ws.Cell(row, colIndex++).Value = url.RedirectTarget ?? "";
            ws.Cell(row, colIndex++).Value = url.IsRedirectLoop ? "Yes" : "";
            ws.Cell(row, colIndex++).Value = url.RedirectChainLength?.ToString() ?? "";
            ws.Cell(row, colIndex++).Value = url.IsSoft404 ? "Yes" : "";
            
            // Meta Refresh
            ws.Cell(row, colIndex++).Value = url.HasMetaRefresh ? "Yes" : "";
            ws.Cell(row, colIndex++).Value = url.MetaRefreshDelay?.ToString() ?? "";
            ws.Cell(row, colIndex++).Value = url.MetaRefreshTarget ?? "";
            
            // JavaScript
            ws.Cell(row, colIndex++).Value = url.HasJsChanges ? "Yes" : "";
            ws.Cell(row, colIndex++).Value = url.JsChangedElements ?? "";
            
            // HTTP Headers
            ws.Cell(row, colIndex++).Value = url.CacheControl ?? "";
            ws.Cell(row, colIndex++).Value = url.Vary ?? "";
            ws.Cell(row, colIndex++).Value = url.ContentEncoding ?? "";
            ws.Cell(row, colIndex++).Value = url.LinkHeader ?? "";
            ws.Cell(row, colIndex++).Value = url.HasHsts ? "Yes" : "";
            
            // Language
            ws.Cell(row, colIndex++).Value = url.HtmlLang ?? "";
            ws.Cell(row, colIndex++).Value = url.ContentLanguageHeader ?? "";
            
            // Content Analysis
            ws.Cell(row, colIndex++).Value = url.ContentHash ?? "";
            ws.Cell(row, colIndex++).Value = url.SimHash?.ToString() ?? "";
            
            // Timestamps
            ws.Cell(row, colIndex++).Value = url.FirstSeenUtc.ToString("yyyy-MM-dd HH:mm:ss");
            ws.Cell(row, colIndex++).Value = url.LastCrawledUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";

            row++;
        }

        // Auto-filter
        if (row > 2)
        {
            ws.Range(1, 1, row - 1, headerEndCol).SetAutoFilter();
        }

        // Set column widths
        ws.Column(1).Width = 60; // Address
        ws.Column(2).Width = 12; // Status Code
        ws.Column(3).Width = 50; // Title
        ws.Column(4).Width = 50; // Meta Description
        ws.Column(5).Width = 50; // H1
        ws.Column(6).Width = 40; // H2
        ws.Column(7).Width = 40; // H3
        ws.Column(8).Width = 30; // H4
        ws.Column(9).Width = 30; // H5
        ws.Column(10).Width = 30; // H6
        
        // Auto-fit remaining columns, but limit max width
        for (int i = 11; i <= headerEndCol; i++)
        {
            ws.Column(i).AdjustToContents();
            if (ws.Column(i).Width > 60)
            {
                ws.Column(i).Width = 60;
            }
        }

        // Freeze header row and first column
        ws.SheetView.FreezeRows(1);
        ws.SheetView.FreezeColumns(1);
    }

    private (string h1, string h2, string h3, string h4, string h5, string h6) ExtractHeadings(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return ("", "", "", "", "", "");
        }

        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var h1 = ExtractHeadingText(doc, "//h1");
            var h2 = ExtractHeadingText(doc, "//h2");
            var h3 = ExtractHeadingText(doc, "//h3");
            var h4 = ExtractHeadingText(doc, "//h4");
            var h5 = ExtractHeadingText(doc, "//h5");
            var h6 = ExtractHeadingText(doc, "//h6");

            return (h1, h2, h3, h4, h5, h6);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extracting headings from HTML");
            return ("", "", "", "", "", "");
        }
    }

    private string ExtractHeadingText(HtmlDocument doc, string xpath)
    {
        var nodes = doc.DocumentNode.SelectNodes(xpath);
        if (nodes == null || nodes.Count == 0)
        {
            return "";
        }

        var texts = nodes
            .Select(n => System.Net.WebUtility.HtmlDecode(n.InnerText?.Trim() ?? ""))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        return texts.Count > 0 ? string.Join(" | ", texts) : "";
    }
}

