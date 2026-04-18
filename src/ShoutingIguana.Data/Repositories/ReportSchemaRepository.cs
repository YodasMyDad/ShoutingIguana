using Microsoft.EntityFrameworkCore;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;

namespace ShoutingIguana.Data.Repositories;

public class ReportSchemaRepository(IShoutingIguanaDbContext context) : IReportSchemaRepository
{
    public async Task<ReportSchema?> GetByTaskKeyAsync(string taskKey)
    {
        return await context.ReportSchemas
            .AsNoTracking()
            .FirstOrDefaultAsync(rs => rs.TaskKey == taskKey)
            .ConfigureAwait(false);
    }

    public async Task<List<ReportSchema>> GetAllAsync()
    {
        return await context.ReportSchemas
            .AsNoTracking()
            .OrderBy(rs => rs.TaskKey)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<ReportSchema> CreateAsync(ReportSchema schema)
    {
        context.ReportSchemas.Add(schema);
        await context.SaveChangesAsync().ConfigureAwait(false);
        return schema;
    }

    public async Task<ReportSchema> UpdateAsync(ReportSchema schema)
    {
        context.Entry(schema).State = EntityState.Modified;
        await context.SaveChangesAsync().ConfigureAwait(false);
        return schema;
    }

    public async Task DeleteByTaskKeyAsync(string taskKey)
    {
        var schema = await context.ReportSchemas
            .FirstOrDefaultAsync(rs => rs.TaskKey == taskKey)
            .ConfigureAwait(false);
        
        if (schema != null)
        {
            context.ReportSchemas.Remove(schema);
            await context.SaveChangesAsync().ConfigureAwait(false);
        }
    }

    public async Task<bool> ExistsAsync(string taskKey)
    {
        return await context.ReportSchemas
            .AnyAsync(rs => rs.TaskKey == taskKey)
            .ConfigureAwait(false);
    }
}

