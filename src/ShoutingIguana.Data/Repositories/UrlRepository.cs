using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;

namespace ShoutingIguana.Data.Repositories;

public class UrlRepository(IShoutingIguanaDbContext context) : IUrlRepository
{
    public async Task<Url?> GetByIdAsync(int id)
    {
        return await context.Urls
            .AsNoTracking()
            .Include(u => u.Headers)
            .FirstOrDefaultAsync(u => u.Id == id).ConfigureAwait(false);
    }

    public async Task<Url?> GetByIdWithHeadersAsync(int id)
    {
        return await context.Urls
            .Include(u => u.Headers)
            .FirstOrDefaultAsync(u => u.Id == id).ConfigureAwait(false);
    }

    public async Task<Url?> GetByAddressAsync(int projectId, string address)
    {
        var normalized = NormalizeUrl(address);
        return await context.Urls
            .FirstOrDefaultAsync(u => u.ProjectId == projectId && u.NormalizedUrl == normalized).ConfigureAwait(false);
    }

    public async Task<Dictionary<string, Url>> GetByAddressesAsync(int projectId, IEnumerable<string> addresses)
    {
        var normalizedKeys = addresses
            .Select(NormalizeUrl)
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .ToList();

        if (normalizedKeys.Count == 0)
        {
            return new Dictionary<string, Url>(StringComparer.Ordinal);
        }

        var found = await context.Urls
            .Where(u => u.ProjectId == projectId && normalizedKeys.Contains(u.NormalizedUrl))
            .ToListAsync()
            .ConfigureAwait(false);

        var result = new Dictionary<string, Url>(StringComparer.Ordinal);
        foreach (var url in found)
        {
            result[url.NormalizedUrl] = url;
        }
        return result;
    }

    public async Task<IEnumerable<Url>> GetByProjectIdAsync(int projectId)
    {
        return await context.Urls
            .AsNoTracking()
            .Where(u => u.ProjectId == projectId)
            .OrderBy(u => u.Depth)
            .ThenBy(u => u.FirstSeenUtc)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<IEnumerable<Url>> GetByStatusAsync(int projectId, UrlStatus status)
    {
        return await context.Urls
            .AsNoTracking()
            .Where(u => u.ProjectId == projectId && u.Status == status)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<List<Url>> GetCompletedUrlsAsync(int projectId)
    {
        return await context.Urls
            .AsNoTracking()
            .Where(u => u.ProjectId == projectId && u.Status == UrlStatus.Completed)
            .OrderBy(u => u.Id)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<List<int>> GetCompletedUrlIdsAsync(int projectId)
    {
        return await context.Urls
            .AsNoTracking()
            .Where(u => u.ProjectId == projectId && u.Status == UrlStatus.Completed)
            .Select(u => u.Id)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<UrlAnalysisDto?> GetForAnalysisAsync(int id)
    {
        var entity = await context.Urls
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
        var compressed = await context.Urls
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => u.RenderedHtmlGzip)
            .FirstOrDefaultAsync().ConfigureAwait(false);

        return Url.DecompressHtml(compressed);
    }

    public async Task<List<HeaderSnapshot>> GetHeadersAsync(int urlId)
    {
        return await context.Headers
            .AsNoTracking()
            .Where(h => h.UrlId == urlId)
            .Select(h => new HeaderSnapshot(h.Name, h.Value))
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<Url> CreateAsync(Url url)
    {
        context.Urls.Add(url);
        await context.SaveChangesAsync().ConfigureAwait(false);
        return url;
    }

    public async Task CreateBatchAsync(IEnumerable<Url> urls)
    {
        var materialized = urls as IList<Url> ?? urls.ToList();
        if (materialized.Count == 0)
        {
            return;
        }

        await context.Urls.AddRangeAsync(materialized).ConfigureAwait(false);
        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task<Url> UpdateAsync(Url url, IEnumerable<KeyValuePair<string, string>>? headers = null)
    {
        if (headers != null)
        {
            var existingHeaders = context.Headers.Where(h => h.UrlId == url.Id);
            context.Headers.RemoveRange(existingHeaders);

            var newHeaders = headers.Select(h => new Header
            {
                UrlId = url.Id,
                Name = h.Key,
                Value = h.Value
            });

            await context.Headers.AddRangeAsync(newHeaders).ConfigureAwait(false);
        }

        context.Entry(url).State = EntityState.Modified;
        await context.SaveChangesAsync().ConfigureAwait(false);
        return url;
    }

    public async Task<int> CountByProjectIdAsync(int projectId)
    {
        return await context.Urls.CountAsync(u => u.ProjectId == projectId).ConfigureAwait(false);
    }

    public async Task<int> CountByStatusAsync(int projectId, UrlStatus status)
    {
        return await context.Urls.CountAsync(u => u.ProjectId == projectId && u.Status == status).ConfigureAwait(false);
    }

    public async Task<List<Url>> GetPagedByProjectIdAsync(int projectId, int skip, int take)
    {
        return await context.Urls
            .AsNoTracking()
            .Where(u => u.ProjectId == projectId)
            .OrderBy(u => u.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<List<Url>> GetPagedByStatusesAsync(int projectId, IReadOnlyCollection<UrlStatus> statuses, int skip, int take)
    {
        return await context.Urls
            .AsNoTracking()
            .Where(u => u.ProjectId == projectId && statuses.Contains(u.Status))
            .OrderBy(u => u.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<int> CountByStatusesAsync(int projectId, IReadOnlyCollection<UrlStatus> statuses)
    {
        return await context.Urls
            .CountAsync(u => u.ProjectId == projectId && statuses.Contains(u.Status)).ConfigureAwait(false);
    }

    public async Task<List<Url>> GetPagedErrorsAsync(int projectId, int skip, int take)
    {
        return await context.Urls
            .AsNoTracking()
            .Where(u => u.ProjectId == projectId && (u.Status == UrlStatus.Failed || (u.HttpStatus != null && u.HttpStatus >= 400)))
            .OrderBy(u => u.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<int> CountErrorsAsync(int projectId)
    {
        return await context.Urls
            .CountAsync(u => u.ProjectId == projectId && (u.Status == UrlStatus.Failed || (u.HttpStatus != null && u.HttpStatus >= 400)))
            .ConfigureAwait(false);
    }

    public async Task DeleteByProjectIdAsync(int projectId)
    {
        var urls = await context.Urls
            .Where(u => u.ProjectId == projectId)
            .ToListAsync().ConfigureAwait(false);
        
        context.Urls.RemoveRange(urls);
        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    private static string NormalizeUrl(string url)
    {
        // Single source of truth for normalization — same as the SDK helper so
        // GetByAddress and CrawlEngine produce matching lookup keys.
        return ShoutingIguana.PluginSdk.Helpers.UrlHelper.Normalize(url);
    }
}

