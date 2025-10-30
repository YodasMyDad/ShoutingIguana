using Microsoft.EntityFrameworkCore;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;

namespace ShoutingIguana.Data.Repositories;

public class UrlRepository(IShoutingIguanaDbContext context) : IUrlRepository
{
    private readonly IShoutingIguanaDbContext _context = context;

    public async Task<Url?> GetByIdAsync(int id)
    {
        return await _context.Urls
            .AsNoTracking()
            .Include(u => u.Headers)
            .FirstOrDefaultAsync(u => u.Id == id).ConfigureAwait(false);
    }

    public async Task<Url?> GetByIdWithHeadersAsync(int id)
    {
        return await _context.Urls
            .Include(u => u.Headers)
            .FirstOrDefaultAsync(u => u.Id == id).ConfigureAwait(false);
    }

    public async Task<Url?> GetByAddressAsync(int projectId, string address)
    {
        var normalized = NormalizeUrl(address);
        return await _context.Urls
            .FirstOrDefaultAsync(u => u.ProjectId == projectId && u.NormalizedUrl == normalized).ConfigureAwait(false);
    }

    public async Task<IEnumerable<Url>> GetByProjectIdAsync(int projectId)
    {
        return await _context.Urls
            .Where(u => u.ProjectId == projectId)
            .OrderBy(u => u.Depth)
            .ThenBy(u => u.FirstSeenUtc)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<IEnumerable<Url>> GetByStatusAsync(int projectId, UrlStatus status)
    {
        return await _context.Urls
            .Where(u => u.ProjectId == projectId && u.Status == status)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<List<Url>> GetCompletedUrlsAsync(int projectId)
    {
        return await _context.Urls
            .Where(u => u.ProjectId == projectId && u.Status == UrlStatus.Completed)
            .OrderBy(u => u.Id)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<List<int>> GetCompletedUrlIdsAsync(int projectId)
    {
        return await _context.Urls
            .AsNoTracking()
            .Where(u => u.ProjectId == projectId && u.Status == UrlStatus.Completed)
            .Select(u => u.Id)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<UrlAnalysisDto?> GetForAnalysisAsync(int id)
    {
        return await _context.Urls
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new UrlAnalysisDto
            {
                Id = u.Id,
                ProjectId = u.ProjectId,
                Address = u.Address,
                NormalizedUrl = u.NormalizedUrl,
                Scheme = u.Scheme,
                Host = u.Host,
                Path = u.Path,
                Depth = u.Depth,
                DiscoveredFromUrlId = u.DiscoveredFromUrlId,
                FirstSeenUtc = u.FirstSeenUtc,
                LastCrawledUtc = u.LastCrawledUtc,
                Status = u.Status,
                HttpStatus = u.HttpStatus,
                ContentType = u.ContentType,
                ContentLength = u.ContentLength,
                RobotsAllowed = u.RobotsAllowed,
                Title = u.Title,
                MetaDescription = u.MetaDescription,
                CanonicalUrl = u.CanonicalUrl,
                MetaRobots = u.MetaRobots,
                RedirectTarget = u.RedirectTarget,
                CanonicalHtml = u.CanonicalHtml,
                CanonicalHttp = u.CanonicalHttp,
                HasMultipleCanonicals = u.HasMultipleCanonicals,
                HasCrossDomainCanonical = u.HasCrossDomainCanonical,
                CanonicalIssues = u.CanonicalIssues,
                RobotsNoindex = u.RobotsNoindex,
                RobotsNofollow = u.RobotsNofollow,
                RobotsNoarchive = u.RobotsNoarchive,
                RobotsNosnippet = u.RobotsNosnippet,
                RobotsNoimageindex = u.RobotsNoimageindex,
                RobotsSource = u.RobotsSource,
                XRobotsTag = u.XRobotsTag,
                HasRobotsConflict = u.HasRobotsConflict,
                HtmlLang = u.HtmlLang,
                ContentLanguageHeader = u.ContentLanguageHeader,
                HasMetaRefresh = u.HasMetaRefresh,
                MetaRefreshDelay = u.MetaRefreshDelay,
                MetaRefreshTarget = u.MetaRefreshTarget,
                HasJsChanges = u.HasJsChanges,
                JsChangedElements = u.JsChangedElements,
                IsRedirectLoop = u.IsRedirectLoop,
                RedirectChainLength = u.RedirectChainLength,
                IsSoft404 = u.IsSoft404,
                CacheControl = u.CacheControl,
                Vary = u.Vary,
                ContentEncoding = u.ContentEncoding,
                LinkHeader = u.LinkHeader,
                HasHsts = u.HasHsts,
                ContentHash = u.ContentHash,
                SimHash = u.SimHash,
                IsIndexable = u.IsIndexable,
                // Note: RenderedHtml is intentionally excluded
                Headers = u.Headers.ToList()
            })
            .FirstOrDefaultAsync().ConfigureAwait(false);
    }

    public async Task<string?> GetRenderedHtmlAsync(int id)
    {
        return await _context.Urls
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => u.RenderedHtml)
            .FirstOrDefaultAsync().ConfigureAwait(false);
    }

    public async Task<Url> CreateAsync(Url url)
    {
        _context.Urls.Add(url);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        return url;
    }

    public async Task<Url> UpdateAsync(Url url)
    {
        _context.Entry(url).State = EntityState.Modified;
        await _context.SaveChangesAsync().ConfigureAwait(false);
        return url;
    }

    public async Task<int> CountByProjectIdAsync(int projectId)
    {
        return await _context.Urls.CountAsync(u => u.ProjectId == projectId).ConfigureAwait(false);
    }

    public async Task<int> CountByStatusAsync(int projectId, UrlStatus status)
    {
        return await _context.Urls.CountAsync(u => u.ProjectId == projectId && u.Status == status).ConfigureAwait(false);
    }

    public async Task DeleteByProjectIdAsync(int projectId)
    {
        var urls = await _context.Urls
            .Where(u => u.ProjectId == projectId)
            .ToListAsync().ConfigureAwait(false);
        
        _context.Urls.RemoveRange(urls);
        await _context.SaveChangesAsync().ConfigureAwait(false);
    }

    private static string NormalizeUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.GetLeftPart(UriPartial.Path).ToLowerInvariant();
        }
        catch
        {
            return url.ToLowerInvariant();
        }
    }
}

