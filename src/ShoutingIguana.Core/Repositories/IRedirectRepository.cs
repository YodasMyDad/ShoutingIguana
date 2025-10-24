using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Core.Repositories;

public interface IRedirectRepository
{
    Task<Redirect> CreateAsync(Redirect redirect);
    Task<List<Redirect>> CreateRangeAsync(List<Redirect> redirects);
    Task<List<Redirect>> GetByUrlIdAsync(int urlId);
    Task<List<Redirect>> GetByProjectIdAsync(int projectId);
    Task DeleteByProjectIdAsync(int projectId);
}

