using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ShoutingIguana.Services;

public interface IExcelExportService
{
    /// <summary>
    /// Export all findings for a project to an Excel workbook with multiple sheets.
    /// </summary>
    /// <param name="progressCallback">Optional callback to report progress (current plugin, current index, total count)</param>
    Task<bool> ExportFindingsAsync(int projectId, string filePath, List<string>? selectedTaskKeys = null, bool includeTechnicalMetadata = false, bool includeErrors = true, bool includeWarnings = true, bool includeInfo = true, Action<string, int, int>? progressCallback = null);
    
    /// <summary>
    /// Export all URLs for a project to an Excel workbook with comprehensive SEO data.
    /// </summary>
    Task<bool> ExportUrlsAsync(int projectId, string filePath);
}

