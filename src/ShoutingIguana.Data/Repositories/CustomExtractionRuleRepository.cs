using Microsoft.EntityFrameworkCore;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;

namespace ShoutingIguana.Data.Repositories;

/// <summary>
/// Repository implementation for custom extraction rules.
/// </summary>
public class CustomExtractionRuleRepository : ICustomExtractionRuleRepository
{
    private readonly IShoutingIguanaDbContext _context;

    public CustomExtractionRuleRepository(IShoutingIguanaDbContext context)
    {
        _context = context;
    }

    public async Task<List<CustomExtractionRule>> GetByProjectIdAsync(int projectId)
    {
        return await _context.CustomExtractionRules
            .Where(r => r.ProjectId == projectId)
            .OrderBy(r => r.Name)
            .ToListAsync();
    }

    public async Task<CustomExtractionRule?> GetByIdAsync(int id)
    {
        return await _context.CustomExtractionRules
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<CustomExtractionRule> CreateAsync(CustomExtractionRule rule)
    {
        rule.CreatedUtc = DateTime.UtcNow;
        _context.CustomExtractionRules.Add(rule);
        await _context.SaveChangesAsync();
        return rule;
    }

    public async Task UpdateAsync(CustomExtractionRule rule)
    {
        _context.CustomExtractionRules.Update(rule);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var rule = await GetByIdAsync(id);
        if (rule != null)
        {
            _context.CustomExtractionRules.Remove(rule);
            await _context.SaveChangesAsync();
        }
    }
}

