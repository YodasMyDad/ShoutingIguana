using Microsoft.EntityFrameworkCore;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;

namespace ShoutingIguana.Data.Repositories;

public class ReportSchemaRepository(IShoutingIguanaDbContext context) : IReportSchemaRepository
{
    private readonly IShoutingIguanaDbContext _context = context;

    public async Task<ReportSchema?> GetByTaskKeyAsync(string taskKey)
    {
        return await _context.ReportSchemas
            .AsNoTracking()
            .FirstOrDefaultAsync(rs => rs.TaskKey == taskKey)
            .ConfigureAwait(false);
    }

    public async Task<List<ReportSchema>> GetAllAsync()
    {
        return await _context.ReportSchemas
            .AsNoTracking()
            .OrderBy(rs => rs.TaskKey)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<ReportSchema> CreateAsync(ReportSchema schema)
    {
        _context.ReportSchemas.Add(schema);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        return schema;
    }

    public async Task<ReportSchema> UpdateAsync(ReportSchema schema)
    {
        _context.Entry(schema).State = EntityState.Modified;
        await _context.SaveChangesAsync().ConfigureAwait(false);
        return schema;
    }

    public async Task DeleteByTaskKeyAsync(string taskKey)
    {
        var schema = await _context.ReportSchemas
            .FirstOrDefaultAsync(rs => rs.TaskKey == taskKey)
            .ConfigureAwait(false);
        
        if (schema != null)
        {
            _context.ReportSchemas.Remove(schema);
            await _context.SaveChangesAsync().ConfigureAwait(false);
        }
    }

    public async Task<bool> ExistsAsync(string taskKey)
    {
        return await _context.ReportSchemas
            .AnyAsync(rs => rs.TaskKey == taskKey)
            .ConfigureAwait(false);
    }
}

