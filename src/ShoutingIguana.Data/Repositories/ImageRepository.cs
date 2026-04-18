using Microsoft.EntityFrameworkCore;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;

namespace ShoutingIguana.Data.Repositories;

public class ImageRepository(IShoutingIguanaDbContext context) : IImageRepository
{
    public async Task<Image> CreateAsync(Image image)
    {
        context.Images.Add(image);
        await context.SaveChangesAsync().ConfigureAwait(false);
        return image;
    }

    public async Task<List<Image>> CreateRangeAsync(List<Image> images)
    {
        context.Images.AddRange(images);
        await context.SaveChangesAsync().ConfigureAwait(false);
        return images;
    }

    public async Task<List<Image>> GetByUrlIdAsync(int urlId)
    {
        return await context.Images
            .Where(i => i.UrlId == urlId)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<List<Image>> GetByProjectIdAsync(int projectId)
    {
        return await context.Images
            .Include(i => i.Url)
            .Where(i => i.Url.ProjectId == projectId)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task DeleteByProjectIdAsync(int projectId)
    {
        var images = await context.Images
            .Include(i => i.Url)
            .Where(i => i.Url.ProjectId == projectId)
            .ToListAsync().ConfigureAwait(false);
        
        context.Images.RemoveRange(images);
        await context.SaveChangesAsync().ConfigureAwait(false);
    }
}

