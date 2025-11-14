using Microsoft.EntityFrameworkCore;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;

namespace ShoutingIguana.Data.Repositories;

public class LinkRepository(IShoutingIguanaDbContext context) : ILinkRepository
{
    public async Task<Link> CreateAsync(Link link)
    {
        context.Links.Add(link);
        await context.SaveChangesAsync().ConfigureAwait(false);
        return link;
    }

    public async Task<IEnumerable<Link>> GetByProjectIdAsync(int projectId)
    {
        return await context.Links
            .Where(l => l.ProjectId == projectId)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<IEnumerable<Link>> GetByFromUrlIdAsync(int fromUrlId)
    {
        return await context.Links
            .AsNoTracking()
            .Where(l => l.FromUrlId == fromUrlId)
            .Include(l => l.ToUrl)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<IEnumerable<Link>> GetByToUrlIdAsync(int toUrlId)
    {
        return await context.Links
            .Where(l => l.ToUrlId == toUrlId)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<int> CountByProjectIdAsync(int projectId)
    {
        return await context.Links.CountAsync(l => l.ProjectId == projectId).ConfigureAwait(false);
    }

    public async Task DeleteByProjectIdAsync(int projectId)
    {
        var links = await context.Links
            .Where(l => l.ProjectId == projectId)
            .ToListAsync().ConfigureAwait(false);
        
        context.Links.RemoveRange(links);
        await context.SaveChangesAsync().ConfigureAwait(false);
    }
}

