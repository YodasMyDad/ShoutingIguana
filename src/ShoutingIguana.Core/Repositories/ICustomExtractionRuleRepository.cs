using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Core.Repositories;

/// <summary>
/// Repository for managing custom extraction rules.
/// </summary>
public interface ICustomExtractionRuleRepository
{
    Task<List<CustomExtractionRule>> GetByProjectIdAsync(int projectId);
    Task<CustomExtractionRule?> GetByIdAsync(int id);
    Task<CustomExtractionRule> CreateAsync(CustomExtractionRule rule);
    Task UpdateAsync(CustomExtractionRule rule);
    Task DeleteAsync(int id);
}

