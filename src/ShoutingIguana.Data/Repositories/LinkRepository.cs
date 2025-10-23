using Microsoft.EntityFrameworkCore;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;

namespace ShoutingIguana.Data.Repositories;

public class LinkRepository(IShoutingIguanaDbContext context) : ILinkRepository
{
    public async Task<Link> CreateAsync(Link link)
    {
        context.Links.Add(link);
        await context.SaveChangesAsync();
        return link;
    }

    public async Task<IEnumerable<Link>> GetByProjectIdAsync(int projectId)
    {
        return await context.Links
            .Where(l => l.ProjectId == projectId)
            .ToListAsync();
    }

    public async Task<IEnumerable<Link>> GetByFromUrlIdAsync(int fromUrlId)
    {
        return await context.Links
            .Where(l => l.FromUrlId == fromUrlId)
            .ToListAsync();
    }

    public async Task<IEnumerable<Link>> GetByToUrlIdAsync(int toUrlId)
    {
        return await context.Links
            .Where(l => l.ToUrlId == toUrlId)
            .ToListAsync();
    }

    public async Task<int> CountByProjectIdAsync(int projectId)
    {
        return await context.Links.CountAsync(l => l.ProjectId == projectId);
    }
}

