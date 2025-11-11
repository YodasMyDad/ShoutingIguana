using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Core.Repositories;

/// <summary>
/// Repository for managing report schemas.
/// </summary>
public interface IReportSchemaRepository
{
    /// <summary>
    /// Gets a schema by task key.
    /// </summary>
    Task<ReportSchema?> GetByTaskKeyAsync(string taskKey);
    
    /// <summary>
    /// Gets all schemas for a project.
    /// </summary>
    Task<List<ReportSchema>> GetAllAsync();
    
    /// <summary>
    /// Creates a new schema.
    /// </summary>
    Task<ReportSchema> CreateAsync(ReportSchema schema);
    
    /// <summary>
    /// Updates an existing schema.
    /// </summary>
    Task<ReportSchema> UpdateAsync(ReportSchema schema);
    
    /// <summary>
    /// Deletes a schema by task key.
    /// </summary>
    Task DeleteByTaskKeyAsync(string taskKey);
    
    /// <summary>
    /// Checks if a schema exists for the given task key.
    /// </summary>
    Task<bool> ExistsAsync(string taskKey);
}

