namespace ShoutingIguana.Plugins.BrokenLinks;

/// <summary>
/// Service for checking if a link is broken by querying the crawl database.
/// </summary>
public interface IBrokenLinksChecker
{
    /// <summary>
    /// Checks the HTTP status of a URL in the database.
    /// Returns null if URL hasn't been crawled yet, otherwise returns the HTTP status code.
    /// </summary>
    Task<int?> CheckLinkStatusAsync(int projectId, string url, CancellationToken cancellationToken);
}

