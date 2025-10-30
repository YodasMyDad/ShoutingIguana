using System.Threading.Tasks;

namespace ShoutingIguana.Services;

public interface IExcelExportService
{
    /// <summary>
    /// Export all findings for a project to an Excel workbook with multiple sheets.
    /// </summary>
    Task<bool> ExportFindingsAsync(int projectId, string filePath, bool includeTechnicalMetadata = false, bool includeErrors = true, bool includeWarnings = true, bool includeInfo = true);
    
    /// <summary>
    /// Export all URLs for a project to an Excel workbook with comprehensive SEO data.
    /// </summary>
    Task<bool> ExportUrlsAsync(int projectId, string filePath);
}

