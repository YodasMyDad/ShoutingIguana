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

