using System.Threading.Tasks;

namespace ShoutingIguana.Services;

public interface ICsvExportService
{
    Task ExportUrlInventoryAsync(int projectId, string filePath);
    Task ExportFindingsAsync(int projectId, string filePath, bool includeTechnicalMetadata = false, bool includeErrors = true, bool includeWarnings = true, bool includeInfo = true);
}

