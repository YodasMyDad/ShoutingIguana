using ShoutingIguana.Core.Models;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Core.Repositories;

public interface IFindingRepository
{
    Task<Finding> CreateAsync(Finding finding);
    Task CreateBatchAsync(IEnumerable<Finding> findings);
    Task<List<Finding>> GetByProjectIdAsync(int projectId);
    Task<List<Finding>> GetByUrlIdAsync(int urlId);
    Task<List<Finding>> GetByTaskKeyAsync(int projectId, string taskKey);
    Task<List<Finding>> GetBySeverityAsync(int projectId, Severity severity);
    Task<int> GetCountByTaskKeyAsync(int projectId, string taskKey);
    Task DeleteByProjectIdAsync(int projectId);
}

