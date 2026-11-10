using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using ShoutingIguana.Cli.Benchmarking;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Configuration.Models;
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
        Assert.Contains("Error: Site not on HTTPS (1 URL)", (await RunAsync("issues", crawl, "no-https")).Output);

        // The synthetic site's sitemap index lists one sitemap that doesn't exist.
        var stats = await RunAsync("stats", crawl);
        var stored = CrawlStatsReader.Read(crawl);
        Assert.Equal(0, stats.ExitCode);
        Assert.Contains("Status        Completed", stats.Output);
        Assert.True(stored is { SitemapCount: > 1, SitemapErrorCount: 1 });
        Assert.Contains($"Sitemaps      {stored.SitemapCount:N0} read, listing {stored.SitemapUrlCount:N0} URLs, 1 couldn't be read", stats.Output);

        // Ctrl+C as an analysis starts: 130, and the results stay. CrawlEngineTests cancels one part-way.
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Assert.Equal(130, (await RunAsync(cancelled.Token, "analyze", crawl)).ExitCode);
        Assert.Contains($"{_server.BaseUri}plants/title-long", (await RunAsync("issues", crawl, "title-too-long")).Output);
    }

    [Fact]
    public async Task TextFromTheCrawlCantDriveTheTerminal()
    {
        var crawl = await CrawlAsync();

        // A detail carrying an escape sequence (here, "turn red") and a right-to-left isolate, as page text could.
        using (var connection = new SqliteConnection($"Data Source={crawl};Pooling=False"))
        {
            connection.Open();
            using var update = connection.CreateCommand();
            update.CommandText = "UPDATE issues SET detail = 'before' || char(27) || '[31m' || char(8295) || 'after' WHERE issue = 'title-too-long'";
            Assert.True(update.ExecuteNonQuery() > 0);
        }

        var shown = await RunAsync("issues", crawl, "title-too-long");

        Assert.Contains("before\\u001B[31m\\u2067after", shown.Output);
        Assert.DoesNotContain((char)27, shown.Output);
        Assert.DoesNotContain((char)0x2067, shown.Output);
    }

    [Fact]
    public async Task ChecksALimitedCrawlCantSupportAreReportedAsNotChecked()
    {
        var crawl = Path.Combine(_folder, "limited.sicrawl");
        var result = await RunAsync("crawl", _server.BaseUri.AbsoluteUri, "--db", crawl, "--max-urls", "20", "-q");
        Assert.Equal(0, result.ExitCode);

        // Said once, in the summary, not again as a log warning (the console log writes to either stream).
        const string NOTE = "Orphan pages weren't checked: the URL limit";
        Assert.Contains(NOTE, result.Output);
        Assert.Equal(1, Regex.Count(result.Output + result.Error, Regex.Escape(NOTE)));

        var orphans = await RunAsync("issues", crawl, "orphan-page");
        Assert.Equal(0, orphans.ExitCode);
        Assert.Contains(NOTE, orphans.Output);
        Assert.Contains("not checked in this analysis", orphans.Output);

        // The summary counts only the checks that ran, as the crawl's own analysis did, never a skipped one as clean.
        var ran = IssueReader.ReadCounts(crawl).Count;
        Assert.True(ran < IssueCatalog.All.Count);
        Assert.Contains($"of {ran} checks found something", result.Output);
        Assert.Contains($"of {ran} checks found something", (await RunAsync("issues", crawl)).Output);
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
        AssertFails(await RunAsync("crawl", _server.BaseUri.AbsoluteUri, "--db", "", "-q"), "empty");
        AssertFails(await RunAsync("issues", crawl, "--csv", ""), "empty");
    }

    /// <summary>
    /// Stopped with Ctrl+C, resumed without the header (it isn't sent at all, not even empty), stopped again, then
    /// resumed with every option that may change. What the site receives shows each was applied.
    /// </summary>
    [Fact]
    public async Task ResumingAppliesTheOptionsThatMayChange()
    {
        var crawl = Path.Combine(_folder, "resumed.sicrawl");
        var keys = _server.Requests.Watch("X-Crawler-Key");

        var first = await StopAfterAsync(TimeSpan.FromMilliseconds(800),
            "crawl", _server.BaseUri.AbsoluteUri, "--db", crawl, "--rate", "20", "--header", "X-Crawler-Key: first", "-q", "--no-analyze");
        Assert.Equal(130, first.ExitCode);
        Assert.Equal(["first"], keys.Distinct());
        keys.Clear();

        var withoutHeader = await StopAfterAsync(TimeSpan.FromMilliseconds(800), "crawl", "--resume", "--db", crawl, "-q", "--no-analyze");
        Assert.Equal(130, withoutHeader.ExitCode);
        Assert.Contains("X-Crawler-Key", withoutHeader.Error);
        Assert.Empty(keys);

        // One request at a time: the gate that -c sets covers every request to the site, robots.txt and sitemaps too.
        _server.Requests.ResetMostOpen();
        var resumed = await RunAsync("crawl", "--resume", "--db", crawl, "-c", "1", "--rate", "500", "--timeout", "15",
            "--header", "X-Crawler-Key: second", "-q", "--no-analyze");
        Assert.True(resumed.ExitCode == 0, resumed.Error);
        Assert.Contains("Done:", resumed.Output);
        Assert.Equal(["second"], keys.Distinct());
        Assert.Equal(1, _server.Requests.MostOpen);
    }

    [Fact]
    public async Task AProxyPasswordIsNeverStoredAndResumingAsksForTheProxyAgain()
    {
        // The site's own port stands in for the proxy: what matters here is what the crawl file keeps.
        var crawl = Path.Combine(_folder, "proxied.sicrawl");
        var proxy = $"http://crawler:pa55word@127.0.0.1:{_server.BaseUri.Port}/";
        var crawled = await RunAsync("crawl", _server.BaseUri.AbsoluteUri, "--db", crawl, "--max-urls", "5", "--proxy", proxy, "-q", "--no-analyze");
        Assert.True(crawled.ExitCode == 0, crawled.Error);

        Assert.Equal($"http://{CrawlOptions.REDACTED}@127.0.0.1:{_server.BaseUri.Port}/", CrawlDatabase.ReadOptions(crawl).ProxyUrl);
        Assert.True((await File.ReadAllBytesAsync(crawl)).AsSpan().IndexOf("pa55word"u8) < 0);
        AssertFails(await RunAsync("crawl", "--resume", "--db", crawl, "-q", "--no-analyze"), "Pass --proxy again");

        // Giving back the redacted proxy the file stored (as it reads it out) isn't giving the password: still asked for.
        AssertFails(await RunAsync("crawl", "--resume", "--db", crawl, "--proxy", $"http://{CrawlOptions.REDACTED}@127.0.0.1:{_server.BaseUri.Port}/",
            "-q", "--no-analyze"), "Pass --proxy again");
    }

    /// <summary>Credentials in the start URL are refused: the crawler never sends them, and the URL is stored and logged as it is.</summary>
    [Fact]
    public async Task CredentialsInTheStartUrlAreRefused()
    {
        var result = await RunAsync("crawl", "https://user:pa55word@example.com/", "--db", Path.Combine(_folder, "creds.sicrawl"), "-q", "--no-analyze");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("user name or password", result.Error);
        Assert.Contains("Authorization header", result.Error);
        Assert.DoesNotContain("pa55word", result.Error);
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

    /// <summary>
    /// The reader benchmark times every kind of read the grids make on an analysed crawl, writes them as JSON, and fails a
    /// read over budget; a crawl without a URL summary can't be measured fully, so a budget fails it too.
    /// </summary>
    [Fact]
    public async Task ReadbenchTimesTheGridsReadsAndFailsOverBudget()
    {
        var crawl = await CrawlAsync();
        var json = Path.Combine(_folder, "readbench.json");
        var timed = await RunAsync("readbench", crawl, "--runs", "2", "--max-p95-ms", "60000", "--json", json);
        Assert.True(timed.ExitCode == 0, timed.Output + timed.Error);
        Assert.Contains("Readers:", timed.Output);
        var result = JsonNode.Parse(await File.ReadAllTextAsync(json))!;
        Assert.True((bool?)result["summaryAvailable"]);
        Assert.Equal(2, (int?)result["runs"]);
        Assert.Empty(result["failures"]!.AsArray());
        Assert.Equal(["facets", "grid", "groups", "inspector", "links", "links-grid", "lookup", "robots", "rows", "sitemaps"],
            result["cases"]!.AsArray().Select(read => (string)read!["kind"]!).Distinct().Order(StringComparer.Ordinal));

        // The links grid is timed with its link summary and without, and so are the sitemaps and robots.txt.
        Assert.All(new[] { "Links grid: ", "Without the link summary: ", "Sitemaps: ", "robots.txt: " }, name =>
            Assert.Contains(result["cases"]!.AsArray(), read => ((string)read!["name"]!).StartsWith(name, StringComparison.Ordinal)));
        Assert.Equal(["link-grid", "link-pass", "link-targets"], result["cases"]!.AsArray().Where(read => (string)read!["kind"]! == "links-grid" || ((string)read["name"]!).StartsWith("Links grid: facets", StringComparison.Ordinal))
            .Select(read => (string)read!["class"]!).Distinct().Order(StringComparer.Ordinal));
        Assert.All(result["cases"]!.AsArray(), read => Assert.True((double?)read!["firstP95Ms"] >= (double?)read["firstP50Ms"]));

        // Every read has a class, each class its gate; the reads without the summary are timed too, and so is the page
        // just past half way, which is read from the far end.
        Assert.All(result["cases"]!.AsArray(), read => Assert.Contains((string)read!["class"]!, ReaderBenchmark.GATE_MS.Keys));

        // Among the links grid's: the links to a few targets with many between them, held to the grid's own gate, which a
        // page of them read by passing over the links before it would miss.
        var fewTargets = result["cases"]!.AsArray().Where(read => ((string)read!["name"]!).Contains("few targets, many links", StringComparison.Ordinal)).ToList();
        Assert.Equal(5, fewTargets.Count);
        Assert.All(fewTargets, read => Assert.Equal("link-grid", (string)read!["class"]!));
        Assert.Contains(result["cases"]!.AsArray(), read => (string)read!["class"]! == "link-targets");
        Assert.Contains(result["cases"]!.AsArray(), read => ((string)read!["name"]!).StartsWith("Without the summary", StringComparison.Ordinal));
        Assert.Contains(result["cases"]!.AsArray(), read => read!["pastHalfP95Ms"] is not null);
        var gated = await RunAsync("readbench", crawl, "--runs", "1", "--gate", "--only", "sort by depth");
        Assert.True(gated.ExitCode == 0, gated.Output + gated.Error);

        // Only some reads, and a budget every read is over.
        var over = await RunAsync("readbench", crawl, "--runs", "1", "--max-p95-ms", "0.000001", "--only", "facets");
        Assert.Equal(1, over.ExitCode);
        Assert.Contains("FAILED       Facets of every URL", over.Output);
        Assert.DoesNotContain("Sort by", over.Output);

        // A filter that leaves nothing to time is a mistake, not a pass.
        Assert.Equal(2, (await RunAsync("readbench", crawl, "--runs", "1", "--max-p95-ms", "1", "--only", "no such read")).ExitCode);

        // Never analysed: the reads that need the URL summary are skipped, so the budget isn't met.
        var unanalysed = Path.Combine(_folder, "unanalysed.sicrawl");
        Assert.Equal(0, (await RunAsync("crawl", _server.BaseUri.AbsoluteUri, "--db", unanalysed, "--max-urls", "20", "-q", "--no-analyze")).ExitCode);
        var partial = await RunAsync("readbench", unanalysed, "--runs", "1", "--max-p95-ms", "60000");
        Assert.Equal(1, partial.ExitCode);
        Assert.Contains("has no URL summary, as it was never analysed", partial.Output);
        Assert.Contains("skipped", partial.Output);

        Assert.Equal(2, (await RunAsync("readbench", Path.Combine(_folder, "missing.sicrawl"))).ExitCode);
    }

    /// <summary>The benchmark keeps its crawl where --db says, for si readbench, but never over a file already there.</summary>
    [Fact]
    public async Task BenchKeepsItsCrawlOnlyInANewFile()
    {
        var existing = Path.Combine(_folder, "kept.sicrawl");
        await File.WriteAllTextAsync(existing, "a crawl");
        var result = await RunAsync("bench", "--pages", "10", "--db", existing);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("There's already a file at", result.Error);
        Assert.Equal("a crawl", await File.ReadAllTextAsync(existing));
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

    /// <summary>Runs a command and presses Ctrl+C after <paramref name="delay"/>.</summary>
    private static async Task<(int ExitCode, string Output, string Error)> StopAfterAsync(TimeSpan delay, params string[] args)
    {
        using var stop = new CancellationTokenSource(delay);
        return await RunAsync(stop.Token, args);
    }

    private static void AssertFails((int ExitCode, string Output, string Error) result, string message)
    {
        Assert.Equal(2, result.ExitCode);
        Assert.Contains(message, result.Error);
        Assert.DoesNotContain("   at ", result.Error);
    }

    private static Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args) => RunAsync(CancellationToken.None, args);

    /// <param name="cancellationToken">Cancelling it is Ctrl+C.</param>
    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(CancellationToken cancellationToken, params string[] args)
    {
        using StringWriter output = new(), error = new();
        var (originalOutput, originalError) = (Console.Out, Console.Error);
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            var exitCode = await CliApp.Create().Parse(args).InvokeAsync(cancellationToken: cancellationToken);
            return (exitCode, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }
    }
}
