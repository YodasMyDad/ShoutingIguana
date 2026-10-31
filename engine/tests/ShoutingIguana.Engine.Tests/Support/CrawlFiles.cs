namespace ShoutingIguana.Engine.Tests.Support;

/// <summary>Temporary crawl files for one test class, deleted together with what SQLite and the writer leave beside them.</summary>
internal sealed class CrawlFiles : IDisposable
{
    private readonly List<string> _paths = [];

    public string NewPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"si-test-{Guid.NewGuid():N}.sicrawl");
        _paths.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var path in _paths.SelectMany(path => new[] { path, path + "-wal", path + "-shm", path + "-lock" }))
        {
            File.Delete(path);
        }
    }
}
