using ShoutingIguana.Engine.Storage;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

// The commands write to the console, which the tests capture, so they can't run side by side.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ShoutingIguana.Cli.Tests;

/// <summary>The si commands end to end, in-process, against a small synthetic site.</summary>
public sealed class CliTests : IAsyncLifetime
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"si-cli-{Guid.NewGuid():N}");
    private SyntheticSiteServer _server = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_folder);
        _server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 60 });
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task CrawlsAnalysesAndReportsTheIssues()
    {
        var crawl = await CrawlAsync();

        var summary = await RunAsync("issues", crawl);
        Assert.Equal(0, summary.ExitCode);
        Assert.Contains("title-too-long", summary.Output);

        var oneIssue = await RunAsync("issues", crawl, "title-too-long");
        Assert.Equal(0, oneIssue.ExitCode);
        Assert.Contains($"{_server.BaseUri}plants/title-long", oneIssue.Output);

        var stats = await RunAsync("stats", crawl);
        Assert.Equal(0, stats.ExitCode);
        Assert.Contains("Status        Completed", stats.Output);
        Assert.Contains("Sitemaps      ", stats.Output);
    }

    [Fact]
    public async Task CsvExportNeverDamagesTheCrawl()
    {
        var crawl = await CrawlAsync();
        var size = new FileInfo(crawl).Length;

        var ontoItself = await RunAsync("issues", crawl, "--csv", crawl);
        Assert.Equal(2, ontoItself.ExitCode);
        Assert.Equal(size, new FileInfo(crawl).Length);
        Assert.Equal(0, (await RunAsync("stats", crawl)).ExitCode);

        // An existing export is replaced only once the new one is complete, and no temporary file is left behind.
        var csv = Path.Combine(_folder, "issues.csv");
        await File.WriteAllTextAsync(csv, "an older export");
        var export = await RunAsync("issues", crawl, "--csv", csv);
        Assert.Equal(0, export.ExitCode);
        var lines = await File.ReadAllLinesAsync(csv);
        Assert.Equal("issue,severity,category,title,url,detail,related_url", lines[0].TrimStart((char)0xFEFF));
        Assert.Equal(IssueReader.ReadAll(crawl).Count() + 1, lines.Length);
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    [Fact]
    public async Task UserMistakesGetOneLineMessages()
    {
        var crawl = await CrawlAsync();
        var notes = Path.Combine(_folder, "notes.txt");
        await File.WriteAllTextAsync(notes, "not a crawl");

        AssertFails(await RunAsync("stats", notes), "isn't a Shouting Iguana crawl");
        AssertFails(await RunAsync("analyze", notes), "isn't a Shouting Iguana crawl");
        AssertFails(await RunAsync("crawl", _server.BaseUri.AbsoluteUri, "--db", crawl, "-q"), "already holds a crawl");
        AssertFails(await RunAsync("issues", crawl, "--limit", "-1"), "--limit");
        AssertFails(await RunAsync("issues", crawl, "no-such-issue"), "Unknown issue");
        AssertFails(await RunAsync("issues", crawl, "--csv", Path.Combine(_folder, "missing", "issues.csv")), "doesn't exist");
        AssertFails(await RunAsync("crawl", "--resume", "--db", crawl, "--max-urls", "5"), "--max-urls can't change");
        AssertFails(await RunAsync("crawl", "--resume", "--db", crawl, _server.BaseUri.AbsoluteUri), "the start URL can't change");
    }

    [Fact]
    public async Task SecretsAreNeverStoredAndResumingAsksForThemAgain()
    {
        var crawl = Path.Combine(_folder, "secret.sicrawl");
        var started = await RunAsync("crawl", _server.BaseUri.AbsoluteUri, "--db", crawl, "--max-urls", "20",
            "--header", "X-Crawler-Key: s3cret-value", "-q", "--no-analyze");
        Assert.Equal(0, started.ExitCode);

        Assert.Equal(string.Empty, CrawlDatabase.ReadOptions(crawl).ExtraHeaders["X-Crawler-Key"]);
        Assert.True((await File.ReadAllBytesAsync(crawl)).AsSpan().IndexOf("s3cret-value"u8) < 0);

        var resumed = await RunAsync("crawl", "--resume", "--db", crawl, "-q", "--no-analyze");
        Assert.Equal(0, resumed.ExitCode);
        Assert.Contains("X-Crawler-Key", resumed.Error);
    }

    private async Task<string> CrawlAsync()
    {
        var crawl = Path.Combine(_folder, $"{Guid.NewGuid():N}.sicrawl");
        var result = await RunAsync("crawl", _server.BaseUri.AbsoluteUri, "--db", crawl, "-q");
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Contains("Done:", result.Output);
        Assert.Contains("checks found something", result.Output);
        return crawl;
    }

    private static void AssertFails((int ExitCode, string Output, string Error) result, string message)
    {
        Assert.Equal(2, result.ExitCode);
        Assert.Contains(message, result.Error);
        Assert.DoesNotContain("   at ", result.Error);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args)
    {
        using StringWriter output = new(), error = new();
        var (originalOutput, originalError) = (Console.Out, Console.Error);
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            var exitCode = await CliApp.Create().Parse(args).InvokeAsync();
            return (exitCode, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }
    }
}
