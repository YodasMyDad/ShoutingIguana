using Microsoft.EntityFrameworkCore;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;

namespace ShoutingIguana.Data.Repositories;

public class StructuredDataRepository(IShoutingIguanaDbContext context) : IStructuredDataRepository
{
    public async Task<StructuredData> CreateAsync(StructuredData structuredData)
    {
        context.StructuredData.Add(structuredData);
        await context.SaveChangesAsync().ConfigureAwait(false);
        return structuredData;
    }

    public async Task<List<StructuredData>> CreateBatchAsync(List<StructuredData> structuredDataList)
    {
        using var transaction = await context.Database.BeginTransactionAsync().ConfigureAwait(false);
        
        try
        {
            context.StructuredData.AddRange(structuredDataList);
            await context.SaveChangesAsync().ConfigureAwait(false);
            await transaction.CommitAsync().ConfigureAwait(false);
            return structuredDataList;
        }
        catch
        {
            await transaction.RollbackAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<List<StructuredData>> GetByUrlIdAsync(int urlId)
    {
        return await context.StructuredData
            .AsNoTracking()
            .Where(sd => sd.UrlId == urlId)
            .OrderBy(sd => sd.Type)
            .ThenBy(sd => sd.SchemaType)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<List<StructuredData>> GetBySchemaTypeAsync(int projectId, string schemaType)
    {
        return await context.StructuredData
            .AsNoTracking()
            .Include(sd => sd.Url)
            .Where(sd => sd.Url.ProjectId == projectId && sd.SchemaType == schemaType)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task DeleteByUrlIdAsync(int urlId)
    {
        var structuredDataList = await context.StructuredData
            .Where(sd => sd.UrlId == urlId)
            .ToListAsync().ConfigureAwait(false);
        
        context.StructuredData.RemoveRange(structuredDataList);
        await context.SaveChangesAsync().ConfigureAwait(false);
    }
}

