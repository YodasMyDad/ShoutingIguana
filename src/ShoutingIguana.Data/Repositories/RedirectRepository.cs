using Microsoft.EntityFrameworkCore;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;

namespace ShoutingIguana.Data.Repositories;

public class RedirectRepository(IShoutingIguanaDbContext context) : IRedirectRepository
{
    public async Task<Redirect> CreateAsync(Redirect redirect)
    {
        context.Redirects.Add(redirect);
        await context.SaveChangesAsync().ConfigureAwait(false);
        return redirect;
    }

    public async Task<List<Redirect>> CreateRangeAsync(List<Redirect> redirects)
    {
        context.Redirects.AddRange(redirects);
        await context.SaveChangesAsync().ConfigureAwait(false);
        return redirects;
    }

    public async Task<List<Redirect>> GetByUrlIdAsync(int urlId)
    {
        return await context.Redirects
            .AsNoTracking()
            .Where(r => r.UrlId == urlId)
            .OrderBy(r => r.Position)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<List<Redirect>> GetByProjectIdAsync(int projectId)
    {
        return await context.Redirects
            .AsNoTracking()
            .Include(r => r.Url)
            .Where(r => r.Url.ProjectId == projectId)
            .OrderBy(r => r.UrlId)
            .ThenBy(r => r.Position)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task DeleteByProjectIdAsync(int projectId)
    {
        var redirects = await context.Redirects
            .Include(r => r.Url)
            .Where(r => r.Url.ProjectId == projectId)
            .ToListAsync().ConfigureAwait(false);
        
        context.Redirects.RemoveRange(redirects);
        await context.SaveChangesAsync().ConfigureAwait(false);
    }
}

