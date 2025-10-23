using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Core.Repositories;

public interface IProjectRepository
{
    Task<Project?> GetByIdAsync(int id);
    Task<IEnumerable<Project>> GetRecentProjectsAsync(int count = 5);
    Task<Project> CreateAsync(Project project);
    Task<Project> UpdateAsync(Project project);
    Task DeleteAsync(int id);
    Task<bool> ExistsAsync(int id);
}

