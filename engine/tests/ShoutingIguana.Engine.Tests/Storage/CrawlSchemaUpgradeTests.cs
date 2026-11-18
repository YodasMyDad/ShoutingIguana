using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Tests.Support;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// The upgrade every writer runs as it opens a file (<see cref="CrawlSchemaUpgrade"/>) knows every crawl-time table and
/// column added since the first file of the schema: from that file's real DDL, frozen in <see cref="OlderCrawlFiles"/>, it
/// gives exactly what a new crawl's file has. A table or column added to the schema without the upgrade fails here.
/// </summary>
public sealed class CrawlSchemaUpgradeTests : IDisposable
{
    private readonly CrawlFiles _files = new();

    public void Dispose() => _files.Dispose();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheFirstFileOfTheSchemaUpgradedHasEveryTableAndColumnANewCrawlHas(bool rechecked)
    {
        var path = _files.NewPath();
        OlderCrawlFiles.Create(path, OlderCrawlFiles.SCHEMA_2 + (rechecked ? OlderCrawlFiles.FIRST_RECHECKS : string.Empty));

        Upgrade(path);

        Assert.Equal(await OlderCrawlFiles.NewCrawlColumnsAsync(), OlderCrawlFiles.Columns(path));
    }

    /// <summary>Upgrading a file twice, or one already current, changes nothing.</summary>
    [Fact]
    public async Task UpgradingAFileAlreadyUpToDateChangesNothing()
    {
        var path = _files.NewPath();
        OlderCrawlFiles.Create(path, OlderCrawlFiles.SCHEMA_2);
        Upgrade(path);
        var once = OlderCrawlFiles.Columns(path);

        Upgrade(path);

        Assert.Equal(once, OlderCrawlFiles.Columns(path));
        Assert.Equal(await OlderCrawlFiles.NewCrawlColumnsAsync(), once);
    }

    private static void Upgrade(string path)
    {
        using var connection = CrawlDatabase.OpenWriter(path, allowNew: false);
        CrawlSchemaUpgrade.Apply(connection, path);
    }
}
