using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ShoutingIguana.Data;

/// <summary>
/// Provides access to project-specific database contexts.
/// 
/// THREAD-SAFETY REQUIREMENTS:
/// - EF Core DbContext is NOT thread-safe and should never be shared across threads
/// - This provider creates a NEW DbContext instance per call to GetDbContext()
/// - Consumers MUST create a new DI scope per thread/worker to ensure isolation
/// - Example: using var scope = serviceProvider.CreateScope();
///           var dbContext = scope.ServiceProvider.GetRequiredService&lt;IShoutingIguanaDbContext&gt;();
/// 
/// LIFETIME:
/// - Register as Singleton in DI container
/// - DbContext is registered as Scoped and resolved via this factory
/// - Each DI scope gets its own short-lived DbContext instance
/// 
/// CRAWL ENGINE PATTERN:
/// The CrawlEngine creates a new scope for each worker and database operation:
/// - Worker threads create scopes independently
/// - Each scope gets an isolated DbContext
/// - No shared state between concurrent operations
/// </summary>
public interface IProjectDbContextProvider
{
    /// <summary>
    /// Gets a new DbContext instance for the current project.
    /// WARNING: Creates a new instance on each call - caller must dispose via DI scope.
    /// </summary>
    IShoutingIguanaDbContext GetDbContext();
    
    /// <summary>
    /// Gets a new DbContext instance for the current project asynchronously.
    /// WARNING: Creates a new instance on each call - caller must dispose via DI scope.
    /// </summary>
    Task<IShoutingIguanaDbContext> GetDbContextAsync();
    
    /// <summary>
    /// Sets the active project database path and ensures migrations are applied.
    /// This operation is thread-safe and can be called from any thread.
    /// </summary>
    Task SetProjectPathAsync(string projectPath);
    
    /// <summary>
    /// Closes the current project. Thread-safe.
    /// </summary>
    void CloseProject();
}

/// <summary>
/// Implementation of IProjectDbContextProvider that manages project-specific database contexts.
/// 
/// INTERNAL THREAD-SAFETY:
/// - Uses SemaphoreSlim to protect _currentProjectPath reads/writes
/// - Lock is held only during path reads - DbContext creation happens outside lock
/// - Factory method (CreateDbContext) is thread-safe and creates independent instances
/// 
/// CRITICAL: This class creates NEW DbContext instances on every GetDbContext() call.
/// The DbContext is NOT cached or reused - each caller gets a fresh instance.
/// Disposal is handled by the DI scope that requested the instance.
/// </summary>
public class ProjectDbContextProvider(
    ILogger<ProjectDbContextProvider> logger,
    ISqliteDbContextFactory dbContextFactory) : IProjectDbContextProvider
{
    private string? _currentProjectPath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Gets a new DbContext instance for the current project.
    /// Thread-safe: Creates a NEW instance per call - safe to call from multiple threads simultaneously.
    /// Each thread/scope receives an isolated DbContext instance.
    /// </summary>
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
            // This ensures thread safety: each caller gets an independent instance
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

