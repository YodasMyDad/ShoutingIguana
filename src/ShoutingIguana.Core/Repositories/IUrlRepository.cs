using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Core.Repositories;

public interface IUrlRepository
{
    Task<Url?> GetByIdAsync(int id);
    Task<Url?> GetByAddressAsync(int projectId, string address);
    Task<IEnumerable<Url>> GetByProjectIdAsync(int projectId);
    Task<IEnumerable<Url>> GetByStatusAsync(int projectId, UrlStatus status);
    Task<Url> CreateAsync(Url url);
    Task<Url> UpdateAsync(Url url);
    Task<int> CountByProjectIdAsync(int projectId);
    Task<int> CountByStatusAsync(int projectId, UrlStatus status);
    Task DeleteByProjectIdAsync(int projectId);
}

