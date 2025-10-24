using Microsoft.EntityFrameworkCore;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;

namespace ShoutingIguana.Data.Repositories;

public class RedirectRepository(IShoutingIguanaDbContext context) : IRedirectRepository
{
    private readonly IShoutingIguanaDbContext _context = context;

    public async Task<Redirect> CreateAsync(Redirect redirect)
    {
        _context.Redirects.Add(redirect);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        return redirect;
    }

    public async Task<List<Redirect>> CreateRangeAsync(List<Redirect> redirects)
    {
        _context.Redirects.AddRange(redirects);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        return redirects;
    }

    public async Task<List<Redirect>> GetByUrlIdAsync(int urlId)
    {
        return await _context.Redirects
            .Where(r => r.UrlId == urlId)
            .OrderBy(r => r.Position)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<List<Redirect>> GetByProjectIdAsync(int projectId)
    {
        return await _context.Redirects
            .Include(r => r.Url)
            .Where(r => r.Url.ProjectId == projectId)
            .OrderBy(r => r.UrlId)
            .ThenBy(r => r.Position)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task DeleteByProjectIdAsync(int projectId)
    {
        var redirects = await _context.Redirects
            .Include(r => r.Url)
            .Where(r => r.Url.ProjectId == projectId)
            .ToListAsync().ConfigureAwait(false);
        
        _context.Redirects.RemoveRange(redirects);
        await _context.SaveChangesAsync().ConfigureAwait(false);
    }
}

