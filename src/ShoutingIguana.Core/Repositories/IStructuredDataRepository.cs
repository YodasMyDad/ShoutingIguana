using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Core.Repositories;

public interface IStructuredDataRepository
{
    Task<StructuredData> CreateAsync(StructuredData structuredData);
    Task<List<StructuredData>> CreateBatchAsync(List<StructuredData> structuredDataList);
    Task<List<StructuredData>> GetByUrlIdAsync(int urlId);
    Task<List<StructuredData>> GetBySchemaTypeAsync(int projectId, string schemaType);
    Task DeleteByUrlIdAsync(int urlId);
}

