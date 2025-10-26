using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Core.Services;

/// <summary>
/// Service for managing custom extraction rules.
/// </summary>
public interface ICustomExtractionService
{
    /// <summary>
    /// Gets all extraction rules for a project.
    /// </summary>
    Task<List<CustomExtractionRule>> GetRulesByProjectIdAsync(int projectId);
    
    /// <summary>
    /// Gets a specific rule by ID.
    /// </summary>
    Task<CustomExtractionRule?> GetRuleAsync(int ruleId);
    
    /// <summary>
    /// Creates or updates a rule.
    /// </summary>
    Task<CustomExtractionRule> SaveRuleAsync(CustomExtractionRule rule);
    
    /// <summary>
    /// Deletes a rule.
    /// </summary>
    Task DeleteRuleAsync(int ruleId);
}

