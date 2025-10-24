using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ShoutingIguana.Data;

public interface IProjectDbContextProvider
{
    IShoutingIguanaDbContext GetDbContext();
    Task<IShoutingIguanaDbContext> GetDbContextAsync();
    Task SetProjectPathAsync(string projectPath);
    void CloseProject();
}

public class ProjectDbContextProvider(
    ILogger<ProjectDbContextProvider> logger,
    ISqliteDbContextFactory dbContextFactory) : IProjectDbContextProvider
{
    private string? _currentProjectPath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public IShoutingIguanaDbContext GetDbContext()
    {
        _lock.Wait();
        try
        {
            if (_currentProjectPath == null)
            {
                throw new InvalidOperationException("No project is currently open. Call SetProjectPathAsync first.");
            }

            // Create a NEW DbContext for each scope - DbContext should be short-lived
            return dbContextFactory.CreateDbContext(_currentProjectPath);
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<IShoutingIguanaDbContext> GetDbContextAsync()
    {
        return Task.FromResult(GetDbContext());
    }

    public async Task SetProjectPathAsync(string projectPath)
    {
        await _lock.WaitAsync();
        try
        {
            if (_currentProjectPath == projectPath)
            {
                return; // Already using this database path
            }

            // Update the current project path
            _currentProjectPath = projectPath;

            // Create a temporary context to ensure database is created and migrations applied
            // Run on background thread to avoid UI freeze
            await Task.Run(() =>
            {
                using var tempContext = dbContextFactory.CreateDbContext(projectPath);
                tempContext.Database.Migrate();
            });

            logger.LogInformation("Switched to project database: {ProjectPath}", projectPath);
        }
        finally
        {
            _lock.Release();
        }
    }
    
    [Obsolete("Use SetProjectPathAsync instead to avoid UI freezes")]
    public void SetProjectPath(string projectPath)
    {
        // Synchronous wrapper for backward compatibility - but prefer async version
        SetProjectPathAsync(projectPath).GetAwaiter().GetResult();
    }

    public void CloseProject()
    {
        _lock.Wait();
        try
        {
            _currentProjectPath = null;
            logger.LogInformation("Closed project database");
        }
        finally
        {
            _lock.Release();
        }
    }
}

