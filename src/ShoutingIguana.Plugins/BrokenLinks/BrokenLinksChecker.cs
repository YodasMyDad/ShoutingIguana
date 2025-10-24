using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ShoutingIguana.Plugins.BrokenLinks;

/// <summary>
/// Implementation of IBrokenLinksChecker that queries the database via a service provider.
/// </summary>
public class BrokenLinksChecker : IBrokenLinksChecker
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BrokenLinksChecker> _logger;

    public BrokenLinksChecker(IServiceProvider serviceProvider, ILogger<BrokenLinksChecker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<int?> CheckLinkStatusAsync(int projectId, string url, CancellationToken cancellationToken)
    {
        try
        {
            // We need to create a scope to get scoped services (repositories)
            using var scope = _serviceProvider.CreateScope();
            
            // Get the URL repository through reflection to avoid tight coupling to Core assemblies
            // This is necessary because plugins are separate assemblies and shouldn't reference Core directly
            var urlRepositoryType = Type.GetType("ShoutingIguana.Core.Repositories.IUrlRepository, ShoutingIguana.Core");
            if (urlRepositoryType == null)
            {
                // Only log once at warning level to avoid log spam
                _logger.LogDebug("Could not find IUrlRepository type - this is expected if checking external links");
                return null;
            }

            var urlRepository = scope.ServiceProvider.GetService(urlRepositoryType);
            if (urlRepository == null)
            {
                _logger.LogDebug("Could not resolve IUrlRepository from service provider");
                return null;
            }

            // Call GetByAddressAsync via reflection with improved error handling
            var getByAddressMethod = urlRepositoryType.GetMethod("GetByAddressAsync");
            if (getByAddressMethod == null)
            {
                _logger.LogDebug("Could not find GetByAddressAsync method");
                return null;
            }

            // Invoke the method and properly handle the Task<T> result
            var task = getByAddressMethod.Invoke(urlRepository, new object[] { projectId, url }) as Task;
            if (task == null)
            {
                return null;
            }

            // ConfigureAwait(false) is important in library code to avoid deadlocks
            await task.ConfigureAwait(false);

            // Get the result (Url entity) - handle both null and non-null cases
            var resultProperty = task.GetType().GetProperty("Result");
            if (resultProperty == null)
            {
                return null;
            }
            
            var urlEntity = resultProperty.GetValue(task);
            if (urlEntity == null)
            {
                return null; // URL not crawled yet
            }

            // Get HttpStatus property with null checking
            var httpStatusProperty = urlEntity.GetType().GetProperty("HttpStatus");
            if (httpStatusProperty == null)
            {
                _logger.LogWarning("HttpStatus property not found on Url entity");
                return null;
            }
            
            var httpStatus = httpStatusProperty.GetValue(urlEntity) as int?;
            return httpStatus;
        }
        catch (Exception ex)
        {
            // Reduce log noise - only log at debug level since this is expected for external links
            _logger.LogDebug(ex, "Error checking link status for {Url}", url);
            return null;
        }
    }
}

