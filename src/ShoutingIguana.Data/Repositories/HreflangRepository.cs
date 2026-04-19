using Microsoft.EntityFrameworkCore;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;

namespace ShoutingIguana.Data.Repositories;

public class HreflangRepository(IShoutingIguanaDbContext context) : IHreflangRepository
{
    public async Task<Hreflang> CreateAsync(Hreflang hreflang)
    {
        context.Hreflangs.Add(hreflang);
        await context.SaveChangesAsync().ConfigureAwait(false);
        return hreflang;
    }

    public async Task<List<Hreflang>> CreateBatchAsync(List<Hreflang> hreflangs)
    {
        context.Hreflangs.AddRange(hreflangs);
        await context.SaveChangesAsync().ConfigureAwait(false);
        return hreflangs;
    }

    public async Task<List<Hreflang>> GetByUrlIdAsync(int urlId)
    {
        return await context.Hreflangs
            .AsNoTracking()
            .Where(h => h.UrlId == urlId)
            .OrderBy(h => h.LanguageCode)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<List<Hreflang>> GetByLanguageCodeAsync(int projectId, string languageCode)
    {
        return await context.Hreflangs
            .AsNoTracking()
            .Include(h => h.Url)
            .Where(h => h.Url.ProjectId == projectId && h.LanguageCode == languageCode)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task DeleteByUrlIdAsync(int urlId)
    {
        var hreflangs = await context.Hreflangs
            .Where(h => h.UrlId == urlId)
            .ToListAsync().ConfigureAwait(false);
        
        context.Hreflangs.RemoveRange(hreflangs);
        await context.SaveChangesAsync().ConfigureAwait(false);
    }
}

