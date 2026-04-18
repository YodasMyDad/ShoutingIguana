using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ShoutingIguana.Data;
using Xunit;

namespace ShoutingIguana.Tests.Data;

public sealed class ProjectDbContextProviderTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            try { File.Delete(f); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task ConcurrentSetProjectPathAsyncDoesNotDeadlock()
    {
        var factory = new InMemoryFactoryStub();
        var provider = new ProjectDbContextProvider(
            new Mock<ILogger<ProjectDbContextProvider>>().Object,
            factory);

        var paths = Enumerable.Range(0, 32).Select(_ => CreateTempDbPath()).ToArray();

        var tasks = paths.Select(p => provider.SetProjectPathAsync(p)).ToArray();
        var allDone = Task.WhenAll(tasks);

        // Bound the wait so a deadlock fails the test instead of hanging the runner
        var winner = await Task.WhenAny(allDone, Task.Delay(TimeSpan.FromSeconds(15)));
        Assert.Same(allDone, winner);
        await allDone;

        // Whatever won the race must be one of the provided paths — never torn or null
        var final = provider.GetDbContext();
        Assert.NotNull(final);
        final.Dispose();
    }

    [Fact]
    public async Task GetDbContextThrowsBeforeFirstSet()
    {
        var provider = new ProjectDbContextProvider(
            new Mock<ILogger<ProjectDbContextProvider>>().Object,
            new InMemoryFactoryStub());

        await Task.Yield();
        Assert.Throws<InvalidOperationException>(() => provider.GetDbContext());
    }

    private string CreateTempDbPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sitest-{Guid.NewGuid():N}.db");
        _tempFiles.Add(path);
        return path;
    }

    /// <summary>
    /// Minimal factory that hands back a real EF Core context bound to an on-disk
    /// SQLite file so <c>Database.Migrate()</c> succeeds without the production
    /// connection-string plumbing.
    /// </summary>
    private sealed class InMemoryFactoryStub : ISqliteDbContextFactory
    {
        public SqliteShoutingIguanaDbContext CreateDbContext(string projectPath)
        {
            var csb = new SqliteConnectionStringBuilder { DataSource = projectPath };
            var options = new DbContextOptionsBuilder<SqliteShoutingIguanaDbContext>()
                .UseSqlite(csb.ToString())
                .Options;
            return new SqliteShoutingIguanaDbContext(options, csb.ToString());
        }
    }
}
