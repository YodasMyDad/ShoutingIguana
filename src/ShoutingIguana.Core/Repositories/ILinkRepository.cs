using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Core.Repositories;

public interface ILinkRepository
{
    Task<Link> CreateAsync(Link link);
    Task<IEnumerable<Link>> GetByProjectIdAsync(int projectId);
    Task<IEnumerable<Link>> GetByFromUrlIdAsync(int fromUrlId);
    Task<IEnumerable<Link>> GetByToUrlIdAsync(int toUrlId);
    Task<int> CountByProjectIdAsync(int projectId);
    Task DeleteByProjectIdAsync(int projectId);
}

