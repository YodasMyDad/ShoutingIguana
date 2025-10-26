using ShoutingIguana.Core.Services.Models;

namespace ShoutingIguana.Core.Services;

/// <summary>
/// Service for handling list-mode URL import and crawling.
/// </summary>
public interface IListModeService
{
    /// <summary>
    /// Imports URLs from a CSV file and adds them to the crawl queue.
    /// </summary>
    /// <param name="projectId">Project ID.</param>
    /// <param name="csvFilePath">Path to CSV file.</param>
    /// <param name="followDiscoveredLinks">If true, follow links discovered on imported URLs. If false, only crawl the list.</param>
    /// <param name="priority">Priority for imported URLs (higher = crawled first).</param>
    /// <param name="progress">Progress reporting.</param>
    /// <returns>Result with success status and imported count.</returns>
    Task<ListModeImportResult> ImportUrlListAsync(
        int projectId,
        string csvFilePath,
        bool followDiscoveredLinks = false,
        int priority = 1000,
        IProgress<string>? progress = null);
}

