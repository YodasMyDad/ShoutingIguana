using Microsoft.EntityFrameworkCore;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;

namespace ShoutingIguana.Data.Repositories;

public class HreflangRepository(IShoutingIguanaDbContext context) : IHreflangRepository
{
    private readonly IShoutingIguanaDbContext _context = context;

    public async Task<Hreflang> CreateAsync(Hreflang hreflang)
    {
        _context.Hreflangs.Add(hreflang);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        return hreflang;
    }

    public async Task<List<Hreflang>> CreateBatchAsync(List<Hreflang> hreflangs)
    {
        using var transaction = await _context.Database.BeginTransactionAsync().ConfigureAwait(false);
        
        try
        {
            _context.Hreflangs.AddRange(hreflangs);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            await transaction.CommitAsync().ConfigureAwait(false);
            return hreflangs;
        }
        catch
        {
            await transaction.RollbackAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<List<Hreflang>> GetByUrlIdAsync(int urlId)
    {
        return await _context.Hreflangs
            .Where(h => h.UrlId == urlId)
            .OrderBy(h => h.LanguageCode)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<List<Hreflang>> GetByLanguageCodeAsync(int projectId, string languageCode)
    {
        return await _context.Hreflangs
            .Include(h => h.Url)
            .Where(h => h.Url.ProjectId == projectId && h.LanguageCode == languageCode)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task DeleteByUrlIdAsync(int urlId)
    {
        var hreflangs = await _context.Hreflangs
            .Where(h => h.UrlId == urlId)
            .ToListAsync().ConfigureAwait(false);
        
        _context.Hreflangs.RemoveRange(hreflangs);
        await _context.SaveChangesAsync().ConfigureAwait(false);
    }
}

