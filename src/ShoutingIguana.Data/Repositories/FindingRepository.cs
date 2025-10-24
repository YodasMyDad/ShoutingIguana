using Microsoft.EntityFrameworkCore;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Data.Repositories;

public class FindingRepository(IShoutingIguanaDbContext context) : IFindingRepository
{
    private readonly IShoutingIguanaDbContext _context = context;

    public async Task<Finding> CreateAsync(Finding finding)
    {
        _context.Findings.Add(finding);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        return finding;
    }

    public async Task CreateBatchAsync(IEnumerable<Finding> findings)
    {
        // Use a transaction to ensure all findings are saved together
        using var transaction = await _context.Database.BeginTransactionAsync().ConfigureAwait(false);
        
        try
        {
            _context.Findings.AddRange(findings);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            await transaction.CommitAsync().ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<List<Finding>> GetByProjectIdAsync(int projectId)
    {
        return await _context.Findings
            .Where(f => f.ProjectId == projectId)
            .Include(f => f.Url)
            .OrderByDescending(f => f.CreatedUtc)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<List<Finding>> GetByUrlIdAsync(int urlId)
    {
        return await _context.Findings
            .Where(f => f.UrlId == urlId)
            .OrderBy(f => f.TaskKey)
            .ThenBy(f => f.Severity)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<List<Finding>> GetByTaskKeyAsync(int projectId, string taskKey)
    {
        return await _context.Findings
            .Where(f => f.ProjectId == projectId && f.TaskKey == taskKey)
            .Include(f => f.Url)
            .OrderByDescending(f => f.Severity)
            .ThenByDescending(f => f.CreatedUtc)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<List<Finding>> GetBySeverityAsync(int projectId, Severity severity)
    {
        return await _context.Findings
            .Where(f => f.ProjectId == projectId && f.Severity == severity)
            .Include(f => f.Url)
            .OrderByDescending(f => f.CreatedUtc)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<int> GetCountByTaskKeyAsync(int projectId, string taskKey)
    {
        return await _context.Findings
            .Where(f => f.ProjectId == projectId && f.TaskKey == taskKey)
            .CountAsync().ConfigureAwait(false);
    }

    public async Task DeleteByProjectIdAsync(int projectId)
    {
        var findings = await _context.Findings
            .Where(f => f.ProjectId == projectId)
            .ToListAsync().ConfigureAwait(false);
        
        _context.Findings.RemoveRange(findings);
        await _context.SaveChangesAsync().ConfigureAwait(false);
    }
}

