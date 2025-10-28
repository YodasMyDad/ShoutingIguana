using System.Threading.Tasks;

namespace ShoutingIguana.Services;

public interface IExcelExportService
{
    /// <summary>
    /// Export all findings for a project to an Excel workbook with multiple sheets.
    /// </summary>
    Task<bool> ExportFindingsAsync(int projectId, string filePath, bool includeTechnicalMetadata = false);
}

