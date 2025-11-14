using ShoutingIguana.PluginSdk;
using ReportRowModel = ShoutingIguana.Core.Models.ReportRow;

namespace ShoutingIguana.Core.Repositories;

/// <summary>
/// Repository for managing report data rows with efficient paging support.
/// </summary>
public interface IReportDataRepository
{
    /// <summary>
    /// Gets report rows for a specific task with paging, filtering, and sorting.
    /// </summary>
    /// <param name="projectId">Project ID</param>
    /// <param name="taskKey">Task key</param>
    /// <param name="page">Page number (0-based)</param>
    /// <param name="pageSize">Number of rows per page</param>
    /// <param name="searchText">Optional search text to filter rows</param>
    /// <param name="sortColumn">Optional column name to sort by</param>
    /// <param name="sortDescending">Sort direction</param>
    /// <returns>List of report rows for the requested page</returns>
    Task<List<ReportRowModel>> GetByTaskKeyAsync(
        int projectId, 
        string taskKey, 
        int page = 0, 
        int pageSize = 100,
        string? searchText = null,
        string? sortColumn = null,
        bool sortDescending = false,
        Severity? severityFilter = null);
    
    /// <summary>
    /// Gets the total count of report rows for a task (used for paging).
    /// </summary>
    Task<int> GetCountByTaskKeyAsync(int projectId, string taskKey, string? searchText = null, Severity? severityFilter = null);
    
    /// <summary>
    /// Gets report rows by URL ID.
    /// </summary>
    Task<List<ReportRowModel>> GetByUrlIdAsync(int urlId);
    
    /// <summary>
    /// Creates a single report row.
    /// </summary>
    Task<ReportRowModel> CreateAsync(ReportRowModel row);
    
    /// <summary>
    /// Creates multiple report rows in a batch (more efficient).
    /// </summary>
    Task CreateBatchAsync(IEnumerable<ReportRowModel> rows);
    
    /// <summary>
    /// Deletes all report rows for a project.
    /// </summary>
    Task DeleteByProjectIdAsync(int projectId);
    
    /// <summary>
    /// Deletes all report rows for a specific task in a project.
    /// </summary>
    Task DeleteByTaskKeyAsync(int projectId, string taskKey);
}

