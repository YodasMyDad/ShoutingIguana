using Microsoft.EntityFrameworkCore;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;
using ShoutingIguana.PluginSdk;
using CoreReportRow = ShoutingIguana.Core.Models.ReportRow;

namespace ShoutingIguana.Data.Repositories;

public class ReportDataRepository(IShoutingIguanaDbContext context) : IReportDataRepository
{
    private readonly IShoutingIguanaDbContext _context = context;

    public async Task<List<CoreReportRow>> GetByTaskKeyAsync(
        int projectId,
        string taskKey,
        int page = 0,
        int pageSize = 100,
        string? searchText = null,
        string? sortColumn = null,
        bool sortDescending = false,
        PluginSdk.Severity? severityFilter = null)
    {
        if (page < 0) page = 0;
        if (pageSize <= 0) pageSize = 100;

        var query = _context.ReportRows
            .AsNoTracking()
            .Where(rr => rr.ProjectId == projectId && rr.TaskKey == taskKey);

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var normalizedSearch = searchText.Trim();
            query = query.Where(rr =>
                rr.RowDataJson.Contains(normalizedSearch) ||
                (rr.IssueText != null && rr.IssueText.Contains(normalizedSearch)) ||
                (rr.Url != null && rr.Url.Address.Contains(normalizedSearch)));
        }

        if (severityFilter.HasValue)
        {
            query = query.Where(rr => rr.Severity == severityFilter);
        }

        query = ApplySorting(query, sortColumn, sortDescending);

        var skip = page * pageSize;
        return await query
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<int> GetCountByTaskKeyAsync(
        int projectId,
        string taskKey,
        string? searchText = null,
        PluginSdk.Severity? severityFilter = null)
    {
        var query = _context.ReportRows
            .Where(rr => rr.ProjectId == projectId && rr.TaskKey == taskKey);

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var normalizedSearch = searchText.Trim();
            query = query.Where(rr =>
                rr.RowDataJson.Contains(normalizedSearch) ||
                (rr.IssueText != null && rr.IssueText.Contains(normalizedSearch)) ||
                (rr.Url != null && rr.Url.Address.Contains(normalizedSearch)));
        }

        if (severityFilter.HasValue)
        {
            query = query.Where(rr => rr.Severity == severityFilter);
        }

        return await query.CountAsync().ConfigureAwait(false);
    }

    public async Task<List<CoreReportRow>> GetByUrlIdAsync(int urlId)
    {
        return await _context.ReportRows
            .Where(rr => rr.UrlId == urlId)
            .OrderBy(rr => rr.TaskKey)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<CoreReportRow> CreateAsync(CoreReportRow row)
    {
        _context.ReportRows.Add(row);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        return row;
    }

    public async Task CreateBatchAsync(IEnumerable<CoreReportRow> rows)
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

    private static IQueryable<CoreReportRow> ApplySorting(
        IQueryable<CoreReportRow> query,
        string? sortColumn,
        bool sortDescending)
    {
        if (string.IsNullOrWhiteSpace(sortColumn))
        {
            return OrderByDefault(query, sortDescending);
        }

        return sortColumn.Trim().ToLowerInvariant() switch
        {
            "createdutc" => sortDescending
                ? query.OrderByDescending(rr => rr.CreatedUtc).ThenByDescending(rr => rr.Id)
                : query.OrderBy(rr => rr.CreatedUtc).ThenBy(rr => rr.Id),
            "severity" => OrderByDefault(query, sortDescending),
            "issue" or "issuetext" => sortDescending
                ? query.OrderByDescending(rr => rr.IssueText ?? string.Empty).ThenByDescending(rr => rr.Id)
                : query.OrderBy(rr => rr.IssueText ?? string.Empty).ThenBy(rr => rr.Id),
            _ => OrderByDefault(query, sortDescending)
        };
    }

    private static IQueryable<CoreReportRow> OrderByDefault(IQueryable<CoreReportRow> query, bool sortDescending)
    {
        return sortDescending
            ? query
                .OrderByDescending(rr => rr.Severity.HasValue ? (int)rr.Severity.Value : int.MinValue)
                .ThenByDescending(rr => rr.IssueText ?? string.Empty)
                .ThenByDescending(rr => rr.Id)
            : query
                .OrderBy(rr => rr.Severity.HasValue ? (int)rr.Severity.Value : int.MaxValue)
                .ThenBy(rr => rr.IssueText ?? string.Empty)
                .ThenBy(rr => rr.Id);
    }
}
