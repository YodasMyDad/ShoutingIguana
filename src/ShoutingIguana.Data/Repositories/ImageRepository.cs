using Microsoft.EntityFrameworkCore;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;

namespace ShoutingIguana.Data.Repositories;

public class ImageRepository(IShoutingIguanaDbContext context) : IImageRepository
{
    private readonly IShoutingIguanaDbContext _context = context;

    public async Task<Image> CreateAsync(Image image)
    {
        _context.Images.Add(image);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        return image;
    }

    public async Task<List<Image>> CreateRangeAsync(List<Image> images)
    {
        _context.Images.AddRange(images);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        return images;
    }

    public async Task<List<Image>> GetByUrlIdAsync(int urlId)
    {
        return await _context.Images
            .Where(i => i.UrlId == urlId)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<List<Image>> GetByProjectIdAsync(int projectId)
    {
        return await _context.Images
            .Include(i => i.Url)
            .Where(i => i.Url.ProjectId == projectId)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task DeleteByProjectIdAsync(int projectId)
    {
        var images = await _context.Images
            .Include(i => i.Url)
            .Where(i => i.Url.ProjectId == projectId)
            .ToListAsync().ConfigureAwait(false);
        
        _context.Images.RemoveRange(images);
        await _context.SaveChangesAsync().ConfigureAwait(false);
    }
}

