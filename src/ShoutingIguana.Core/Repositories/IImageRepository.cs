using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Core.Repositories;

public interface IImageRepository
{
    Task<Image> CreateAsync(Image image);
    Task<List<Image>> CreateRangeAsync(List<Image> images);
    Task<List<Image>> GetByUrlIdAsync(int urlId);
    Task<List<Image>> GetByProjectIdAsync(int projectId);
    Task DeleteByProjectIdAsync(int projectId);
}

