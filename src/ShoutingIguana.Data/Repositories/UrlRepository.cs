using System.Collections.Generic;
using System.Linq;
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
        var entity = await _context.Urls
            .AsNoTracking()
            .Include(u => u.Headers)
            .FirstOrDefaultAsync(u => u.Id == id)
            .ConfigureAwait(false);

        if (entity == null)
        {
            return null;
        }

        return new UrlAnalysisDto
        {
            Id = entity.Id,
            ProjectId = entity.ProjectId,
            Address = entity.Address,
            NormalizedUrl = entity.NormalizedUrl,
            Scheme = entity.Scheme,
            Host = entity.Host,
            Path = entity.Path,
            Depth = entity.Depth,
            DiscoveredFromUrlId = entity.DiscoveredFromUrlId,
            FirstSeenUtc = entity.FirstSeenUtc,
            LastCrawledUtc = entity.LastCrawledUtc,
            Status = entity.Status,
            HttpStatus = entity.HttpStatus,
            ContentType = entity.ContentType,
            ContentLength = entity.ContentLength,
            RobotsAllowed = entity.RobotsAllowed,
            Title = entity.Title,
            MetaDescription = entity.MetaDescription,
            CanonicalUrl = entity.CanonicalUrl,
            MetaRobots = entity.MetaRobots,
            RedirectTarget = entity.RedirectTarget,
            CanonicalHtml = entity.CanonicalHtml,
            CanonicalHttp = entity.CanonicalHttp,
            HasMultipleCanonicals = entity.HasMultipleCanonicals,
            HasCrossDomainCanonical = entity.HasCrossDomainCanonical,
            CanonicalIssues = entity.CanonicalIssues,
            RobotsNoindex = entity.RobotsNoindex,
            RobotsNofollow = entity.RobotsNofollow,
            RobotsNoarchive = entity.RobotsNoarchive,
            RobotsNosnippet = entity.RobotsNosnippet,
            RobotsNoimageindex = entity.RobotsNoimageindex,
            RobotsSource = entity.RobotsSource,
            XRobotsTag = entity.XRobotsTag,
            HasRobotsConflict = entity.HasRobotsConflict,
            HtmlLang = entity.HtmlLang,
            ContentLanguageHeader = entity.ContentLanguageHeader,
            HasMetaRefresh = entity.HasMetaRefresh,
            MetaRefreshDelay = entity.MetaRefreshDelay,
            MetaRefreshTarget = entity.MetaRefreshTarget,
            HasJsChanges = entity.HasJsChanges,
            JsChangedElements = entity.JsChangedElements,
            IsRedirectLoop = entity.IsRedirectLoop,
            RedirectChainLength = entity.RedirectChainLength,
            IsSoft404 = entity.IsSoft404,
            CacheControl = entity.CacheControl,
            Vary = entity.Vary,
            ContentEncoding = entity.ContentEncoding,
            LinkHeader = entity.LinkHeader,
            HasHsts = entity.HasHsts,
            ContentHash = entity.ContentHash,
            SimHash = entity.SimHash,
            IsIndexable = entity.IsIndexable,
            Headers = entity.Headers
                .Select(h => new HeaderSnapshot(h.Name, h.Value))
                .ToList()
        };
    }

    public async Task<string?> GetRenderedHtmlAsync(int id)
    {
        return await _context.Urls
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => u.RenderedHtml)
            .FirstOrDefaultAsync().ConfigureAwait(false);
    }

    public async Task<List<HeaderSnapshot>> GetHeadersAsync(int urlId)
    {
        return await _context.Headers
            .AsNoTracking()
            .Where(h => h.UrlId == urlId)
            .Select(h => new HeaderSnapshot(h.Name, h.Value))
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<Url> CreateAsync(Url url)
    {
        _context.Urls.Add(url);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        return url;
    }

    public async Task<Url> UpdateAsync(Url url, IEnumerable<KeyValuePair<string, string>>? headers = null)
    {
        if (headers != null)
        {
            var existingHeaders = _context.Headers.Where(h => h.UrlId == url.Id);
            _context.Headers.RemoveRange(existingHeaders);

            var newHeaders = headers.Select(h => new Header
            {
                UrlId = url.Id,
                Name = h.Key,
                Value = h.Value
            });

            await _context.Headers.AddRangeAsync(newHeaders).ConfigureAwait(false);
        }

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

