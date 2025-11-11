using Microsoft.EntityFrameworkCore;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;

namespace ShoutingIguana.Data.Repositories;

public class ReportDataRepository(IShoutingIguanaDbContext context) : IReportDataRepository
{
    private readonly IShoutingIguanaDbContext _context = context;

    public async Task<List<ReportRow>> GetByTaskKeyAsync(
        int projectId,
        string taskKey,
        int page = 0,
        int pageSize = 100,
        string? searchText = null,
        string? sortColumn = null,
        bool sortDescending = false)
    {
        var query = _context.ReportRows
            .Where(rr => rr.ProjectId == projectId && rr.TaskKey == taskKey)
            .Include(rr => rr.Url)
            .AsNoTracking();

        // Apply search filter if provided
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            // Search in JSON data using SQLite JSON functions
            // For simplicity, we'll also search in URL address if available
            query = query.Where(rr =>
                rr.RowDataJson.Contains(searchText) ||
                (rr.Url != null && rr.Url.Address.Contains(searchText)));
        }

        // Apply sorting
        // Default sort by CreatedUtc descending
        if (string.IsNullOrWhiteSpace(sortColumn))
        {
            query = sortDescending
                ? query.OrderBy(rr => rr.CreatedUtc)
                : query.OrderByDescending(rr => rr.CreatedUtc);
        }
        else
        {
            // For custom column sorting, we would need to extract from JSON
            // For now, fall back to CreatedUtc
            query = sortDescending
                ? query.OrderBy(rr => rr.CreatedUtc)
                : query.OrderByDescending(rr => rr.CreatedUtc);
        }

        // Apply paging
        query = query
            .Skip(page * pageSize)
            .Take(pageSize);

        return await query.ToListAsync().ConfigureAwait(false);
    }

    public async Task<int> GetCountByTaskKeyAsync(int projectId, string taskKey, string? searchText = null)
    {
        var query = _context.ReportRows
            .Where(rr => rr.ProjectId == projectId && rr.TaskKey == taskKey);

        // Apply search filter if provided
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            query = query.Where(rr =>
                rr.RowDataJson.Contains(searchText) ||
                (rr.Url != null && rr.Url.Address.Contains(searchText)));
        }

        return await query.CountAsync().ConfigureAwait(false);
    }

    public async Task<List<ReportRow>> GetByUrlIdAsync(int urlId)
    {
        return await _context.ReportRows
            .Where(rr => rr.UrlId == urlId)
            .OrderBy(rr => rr.TaskKey)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<ReportRow> CreateAsync(ReportRow row)
    {
        _context.ReportRows.Add(row);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        return row;
    }

    public async Task CreateBatchAsync(IEnumerable<ReportRow> rows)
    {
        // Use a transaction to ensure all rows are saved together
        using var transaction = await _context.Database.BeginTransactionAsync().ConfigureAwait(false);

        try
        {
            _context.ReportRows.AddRange(rows);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            await transaction.CommitAsync().ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task DeleteByProjectIdAsync(int projectId)
    {
        var rows = await _context.ReportRows
            .Where(rr => rr.ProjectId == projectId)
            .ToListAsync()
            .ConfigureAwait(false);

        _context.ReportRows.RemoveRange(rows);
        await _context.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task DeleteByTaskKeyAsync(int projectId, string taskKey)
    {
        var rows = await _context.ReportRows
            .Where(rr => rr.ProjectId == projectId && rr.TaskKey == taskKey)
            .ToListAsync()
            .ConfigureAwait(false);

        _context.ReportRows.RemoveRange(rows);
        await _context.SaveChangesAsync().ConfigureAwait(false);
    }
}

