using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ShoutingIguana.Data;

public interface IProjectDbContextProvider
{
    IShoutingIguanaDbContext GetDbContext();
    Task<IShoutingIguanaDbContext> GetDbContextAsync();
}

public class ProjectDbContextProvider(
    ILogger<ProjectDbContextProvider> logger,
    ISqliteDbContextFactory dbContextFactory) : IProjectDbContextProvider
{
    private string? _currentProjectPath;
    private readonly object _lock = new();

    public IShoutingIguanaDbContext GetDbContext()
    {
        lock (_lock)
        {
            if (_currentProjectPath == null)
            {
                throw new InvalidOperationException("No project is currently open. Call SetProjectPath first.");
            }

            // Create a NEW DbContext for each scope - DbContext should be short-lived
            return dbContextFactory.CreateDbContext(_currentProjectPath);
        }
    }

    public Task<IShoutingIguanaDbContext> GetDbContextAsync()
    {
        return Task.FromResult(GetDbContext());
    }

    public void SetProjectPath(string projectPath)
    {
        lock (_lock)
        {
            if (_currentProjectPath == projectPath)
            {
                return; // Already using this database path
            }

            // Update the current project path
            _currentProjectPath = projectPath;

            // Create a temporary context to ensure database is created and migrations applied
            using var tempContext = dbContextFactory.CreateDbContext(projectPath);
            tempContext.Database.Migrate();

            logger.LogInformation("Switched to project database: {ProjectPath}", projectPath);
        }
    }

    public void CloseProject()
    {
        lock (_lock)
        {
            _currentProjectPath = null;
            logger.LogInformation("Closed project database");
        }
    }
}

