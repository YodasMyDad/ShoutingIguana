using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Core.Repositories;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Core.Services;

/// <summary>
/// Implementation of IRepositoryAccessor that adapts Core repositories for plugin use.
/// Uses internal repositories but exposes simplified DTOs to avoid coupling plugins to Core models.
/// </summary>
public class RepositoryAccessor(
    IServiceProvider serviceProvider,
    ILogger<RepositoryAccessor> logger) : IRepositoryAccessor
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger<RepositoryAccessor> _logger = logger;

    public async Task<UrlInfo?> GetUrlByAddressAsync(int projectId, string address)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var urlRepository = scope.ServiceProvider.GetRequiredService<IUrlRepository>();
            
            // Repository handles normalization internally (lowercases and uses GetLeftPart)
            // So we can pass the URL directly without pre-normalizing
            var url = await urlRepository.GetByAddressAsync(projectId, address);
            
            if (url == null)
            {
                return null; // URL not in database at all
            }
            
            // IMPORTANT: Only return URLs that have been crawled (have an HttpStatus)
            // URLs can be in database with Status=Pending (discovered but not crawled yet)
            // These should be treated as "not found" for link checking purposes
            if (!url.HttpStatus.HasValue)
            {
                _logger.LogDebug("URL {Address} exists but hasn't been crawled yet (Status: {Status})", 
                    address, url.Status);
                return null; // Not crawled yet, treat as not found
            }
            
            return new UrlInfo(
                url.Address,
                url.HttpStatus.Value,
                url.ContentType,
                url.Depth,
                url.IsIndexable ?? false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting URL by address: {Address} for project {ProjectId}", 
                address, projectId);
            return null;
        }
    }

    public async IAsyncEnumerable<UrlInfo> GetUrlsAsync(
        int projectId, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // IMPORTANT: Materialize the data before yielding to avoid scope disposal issues
        // We create a scope, fetch all data, then dispose the scope before yielding
        List<UrlInfo> urlInfos;
        
        using (var scope = _serviceProvider.CreateScope())
        {
            var urlRepository = scope.ServiceProvider.GetRequiredService<IUrlRepository>();
            var urls = await urlRepository.GetByProjectIdAsync(projectId);
            
            // Materialize to DTOs while scope is active
            // Only include URLs that have been actually crawled (have HttpStatus)
            urlInfos = urls
                .Where(url => url.HttpStatus.HasValue) // Filter out pending/uncrawled URLs
                .Select(url => new UrlInfo(
                    url.Address,
                    url.HttpStatus!.Value,
                    url.ContentType,
                    url.Depth,
                    url.IsIndexable ?? false
                )).ToList();
        } // Scope disposed here
        
        // Now yield items from materialized list (safe, no scope issues)
        foreach (var urlInfo in urlInfos)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return urlInfo;
        }
    }

    public async IAsyncEnumerable<RedirectInfo> GetRedirectsAsync(
        int projectId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // IMPORTANT: Materialize the data before yielding to avoid scope disposal issues
        List<RedirectInfo> redirectInfos;
        
        using (var scope = _serviceProvider.CreateScope())
        {
            var redirectRepository = scope.ServiceProvider.GetRequiredService<IRedirectRepository>();
            var urlRepository = scope.ServiceProvider.GetRequiredService<IUrlRepository>();
            
            // Get all redirects for the project
            var redirects = await redirectRepository.GetByProjectIdAsync(projectId);
            
            // Build a lookup of UrlId to Address for efficient mapping
            var urls = await urlRepository.GetByProjectIdAsync(projectId);
            var urlLookup = urls.ToDictionary(u => u.Id, u => u.Address);
            
            // Materialize to DTOs while scope is active
            redirectInfos = redirects.Select(redirect =>
            {
                var sourceUrl = urlLookup.GetValueOrDefault(redirect.UrlId) ?? $"[Unknown URL ID: {redirect.UrlId}]";
                
                return new RedirectInfo(
                    sourceUrl,
                    redirect.ToUrl,
                    redirect.StatusCode,
                    redirect.Position);
            }).ToList();
        } // Scope disposed here
        
        // Now yield items from materialized list (safe, no scope issues)
        foreach (var redirectInfo in redirectInfos)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return redirectInfo;
        }
    }

    public async Task<RedirectInfo?> GetRedirectAsync(int projectId, string sourceUrl)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var urlRepository = scope.ServiceProvider.GetRequiredService<IUrlRepository>();
            var redirectRepository = scope.ServiceProvider.GetRequiredService<IRedirectRepository>();
            
            // First, get the URL entity to get its ID
            var url = await urlRepository.GetByAddressAsync(projectId, sourceUrl);
            
            if (url == null)
            {
                return null;
            }
            
            // Get redirects for this specific URL (much more efficient than GetByProjectIdAsync)
            var urlRedirects = await redirectRepository.GetByUrlIdAsync(url.Id);
            
            if (urlRedirects.Count == 0)
            {
                return null;
            }
            
            // Return the first redirect in the chain (ordered by position)
            var firstRedirect = urlRedirects.OrderBy(r => r.Position).First();
            
            return new RedirectInfo(
                sourceUrl,
                firstRedirect.ToUrl,
                firstRedirect.StatusCode,
                firstRedirect.Position);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting redirect for URL: {SourceUrl} in project {ProjectId}", 
                sourceUrl, projectId);
            return null;
        }
    }
    
    public async Task<List<PluginSdk.LinkInfo>> GetLinksByFromUrlAsync(int projectId, int fromUrlId)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var linkRepository = scope.ServiceProvider.GetRequiredService<ILinkRepository>();
            var urlRepository = scope.ServiceProvider.GetRequiredService<IUrlRepository>();
            
            // OPTIMIZED: Use specific query instead of loading all links for the project
            var outgoingLinks = (await linkRepository.GetByFromUrlIdAsync(fromUrlId)).ToList();
            
            if (outgoingLinks.Count == 0)
            {
                return new List<PluginSdk.LinkInfo>();
            }
            
            // OPTIMIZED: Only fetch the specific target URLs we need, not all URLs in the project
            var targetUrlIds = outgoingLinks.Select(l => l.ToUrlId).Distinct().ToList();
            var allUrls = await urlRepository.GetByProjectIdAsync(projectId);
            var urlLookup = allUrls
                .Where(u => targetUrlIds.Contains(u.Id))
                .ToDictionary(u => u.Id, u => u.Address);
            
            // Map to LinkInfo DTOs with diagnostic metadata
            return outgoingLinks.Select(link => new PluginSdk.LinkInfo(
                link.FromUrlId,
                link.ToUrlId,
                urlLookup.GetValueOrDefault(link.ToUrlId) ?? "Unknown",
                link.AnchorText,
                link.LinkType.ToString(),
                link.DomPath,
                link.ElementTag,
                link.IsVisible,
                link.PositionX,
                link.PositionY,
                link.ElementWidth,
                link.ElementHeight,
                link.HtmlSnippet,
                link.ParentTag
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting links from URL ID: {FromUrlId} for project {ProjectId}", 
                fromUrlId, projectId);
            return new List<PluginSdk.LinkInfo>();
        }
    }
    
    public async Task<List<CustomExtractionRuleInfo>> GetCustomExtractionRulesAsync(int projectId)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var customExtractionService = scope.ServiceProvider.GetRequiredService<ICustomExtractionService>();
            
            var rules = await customExtractionService.GetRulesByProjectIdAsync(projectId);
            
            return rules.Select(r => new CustomExtractionRuleInfo(
                r.Id,
                r.ProjectId,
                r.Name,
                r.FieldName,
                (PluginSdk.SelectorType)r.SelectorType, // Map enum
                r.Selector,
                r.IsEnabled
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting custom extraction rules for project {ProjectId}", projectId);
            return new List<CustomExtractionRuleInfo>();
        }
    }
}

