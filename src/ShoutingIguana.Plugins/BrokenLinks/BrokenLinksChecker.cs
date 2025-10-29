using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.BrokenLinks;

/// <summary>
/// Implementation of IBrokenLinksChecker that queries crawled URLs via repository accessor.
/// </summary>
public class BrokenLinksChecker : IBrokenLinksChecker
{
    private readonly IRepositoryAccessor _repositoryAccessor;
    private readonly ILogger<BrokenLinksChecker> _logger;

    public BrokenLinksChecker(IRepositoryAccessor repositoryAccessor, ILogger<BrokenLinksChecker> logger)
    {
        _repositoryAccessor = repositoryAccessor;
        _logger = logger;
    }

    public async Task<int?> CheckLinkStatusAsync(int projectId, string url, CancellationToken cancellationToken)
    {
        try
        {
            var urlInfo = await _repositoryAccessor.GetUrlByAddressAsync(projectId, url);
            
            if (urlInfo == null)
            {
                _logger.LogDebug("URL not found in database: {Url}", url);
                return null; // URL not crawled yet
            }
            
            _logger.LogDebug("Found URL {Url} with HTTP status {Status}", url, urlInfo.Status);
            return urlInfo.Status;
        }
        catch (Exception ex)
        {
            // Reduce log noise - only log at debug level since this is expected for external links
            _logger.LogDebug(ex, "Error checking link status for {Url}", url);
            return null;
        }
    }
}

