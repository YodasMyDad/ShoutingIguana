using Microsoft.EntityFrameworkCore;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;

namespace ShoutingIguana.Data.Repositories;

public class ProjectRepository(IShoutingIguanaDbContext context) : IProjectRepository
{
    public async Task<Project?> GetByIdAsync(int id)
    {
        return await context.Projects.FindAsync(id).ConfigureAwait(false);
    }

    public async Task<IEnumerable<Project>> GetRecentProjectsAsync(int count = 5)
    {
        return await context.Projects
            .OrderByDescending(p => p.LastOpenedUtc)
            .Take(count)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<Project> CreateAsync(Project project)
    {
        context.Projects.Add(project);
        await context.SaveChangesAsync().ConfigureAwait(false);
        return project;
    }

    public async Task<Project> UpdateAsync(Project project)
    {
        context.Entry(project).State = EntityState.Modified;
        await context.SaveChangesAsync().ConfigureAwait(false);
        return project;
    }

    public async Task DeleteAsync(int id)
    {
        var project = await context.Projects.FindAsync(id).ConfigureAwait(false);
        if (project != null)
        {
            context.Projects.Remove(project);
            await context.SaveChangesAsync().ConfigureAwait(false);
        }
    }

    public async Task<bool> ExistsAsync(int id)
    {
        return await context.Projects.AnyAsync(p => p.Id == id).ConfigureAwait(false);
    }
}

