using ShoutingIguana.Sidecar.Library;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>Which paths are a crawl's own computer, and how a path folds to one spelling for its id.</summary>
public sealed class CrawlPathsTests
{
    [Theory]
    [InlineData(@"\\server\share\crawl.sicrawl", true)]
    [InlineData(@"\\?\UNC\server\share\crawl.sicrawl", true)]
    [InlineData(@"\\?\C:\crawls\crawl.sicrawl", true)]
    [InlineData(@"\\.\pipe\thing", true)]
    [InlineData(@"C:\crawls\crawl.sicrawl", false)]
    [InlineData(@"Z:\mapped\crawl.sicrawl", false)]
    public void UncAndDevicePathsAreRecognisedOnWindows(string path, bool isUncOrDevice)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal(isUncOrDevice, CrawlPaths.IsUncOrDevicePath(path));
    }

    [Theory]
    [InlineData(@"\\server\share\crawl.sicrawl", true)]
    [InlineData(@"\\?\UNC\server\share\crawl.sicrawl", true)]
    [InlineData(@"\\?\C:\crawls\crawl.sicrawl", false)]
    [InlineData(@"\\.\pipe\thing", false)]
    [InlineData(@"C:\crawls\crawl.sicrawl", false)]
    public void NetworkPathsAreTheOnesOnAnotherComputer(string path, bool isNetwork)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal(isNetwork, CrawlPaths.IsNetworkPath(path));
    }

    /// <summary>Resolve gives a full path, with <c>.</c> and <c>..</c> and links folded away, so two spellings share an id.</summary>
    [Fact]
    public void ResolveFoldsAFileReachedTwoWaysToOneId()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"si-paths-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "sub"));
        try
        {
            var plain = Path.Combine(directory, "sub", "crawl.sicrawl");
            var roundabout = Path.Combine(directory, "sub", "..", "sub", ".", "crawl.sicrawl");
            File.WriteAllText(plain, string.Empty);

            Assert.Equal(CrawlPaths.Resolve(plain), CrawlPaths.Resolve(roundabout));
            Assert.Equal(CrawlIds.For(plain), CrawlIds.For(roundabout));

            // A symbolic link to the same file resolves to it, so it gets the same id. Creating one needs privilege on
            // Windows (developer mode), so this half is skipped where it can't be made.
            var link = Path.Combine(directory, "link.sicrawl");
            try
            {
                File.CreateSymbolicLink(link, plain);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return;
            }

            Assert.Equal(CrawlIds.For(plain), CrawlIds.For(link));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
