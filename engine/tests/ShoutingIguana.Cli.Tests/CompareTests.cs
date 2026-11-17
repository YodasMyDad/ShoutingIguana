using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ShoutingIguana.Cli.Benchmarking;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;
using static ShoutingIguana.Cli.Tests.CliTests;

namespace ShoutingIguana.Cli.Tests;

/// <summary>
/// The fix loop's commands end to end, against a small synthetic site crawled at revision 0 and again at revision 1:
/// <c>si compare</c> as a table, one check's rows and causes, and JSON; the reader benchmark's comparison class; and the
/// benchmark crawling both revisions and holding their comparison to what the site planted.
/// </summary>
public sealed class CompareTests : IAsyncLifetime
{
    private const int PAGES = 60;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"si-compare-{Guid.NewGuid():N}");
    private SyntheticSiteServer _server = null!;
    private string _then = string.Empty;
    private string _now = string.Empty;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_folder);
        _server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = PAGES });
        _then = await CrawlAsync("then");
        _server.Revision = 1;
        _now = await CrawlAsync("now");
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        Directory.Delete(_folder, recursive: true);
    }

    private string Url(string path) => new Uri(_server.BaseUri, path).AbsoluteUri;

    /// <summary>
    /// The table: how far the two compare, the totals, and a line for each check that moved with how its URLs did; the
    /// JSON is the comparison the readers give, every check's state as the site planted it.
    /// </summary>
    [Fact]
    public async Task CompareSaysWhatWasFixedWhatIsNewAndWhatWasntChecked()
    {
        var table = await RunAsync("compare", _now, _then);
        Assert.True(table.ExitCode == 0, table.Error);
        Assert.Contains("Comparable: fully", table.Output);
        Assert.Matches(new Regex(@"^Check +State +Then +Now +Fixed +Gone +Not checked +Still +New +New URL +Not compared\r?$", RegexOptions.Multiline), table.Output);

        // The long titles: one fixed, one not checked (the page now errors), one still, one new page.
        Assert.Matches(new Regex(@"^title-too-long +fewer +3 +2 +1 +0 +1 +1 +0 +1 +0\r?$", RegexOptions.Multiline), table.Output);
        Assert.Matches(new Regex(@"^security-headers-missing +new +0 +1 ", RegexOptions.Multiline), table.Output);
        Assert.Matches(new Regex(@"^  and \d+ checks the same, on the same URLs\.\r?$", RegexOptions.Multiline), table.Output);
        Assert.DoesNotMatch(new Regex(@"^h1-missing ", RegexOptions.Multiline), table.Output);

        var json = await RunAsync("compare", _now, _then, "--json");
        Assert.True(json.ExitCode == 0, json.Error);
        var comparison = JsonNode.Parse(json.Output)!;
        Assert.Equal("full", (string?)comparison["comparable"]);
        Assert.Empty(comparison["differences"]!.AsArray());
        var expected = _server.Site.GetChanges(0, 1, Judging);
        var checks = comparison["checks"]!.AsArray();
        Assert.Equal(IssueCatalog.All.Count, checks.Count);
        Assert.All(checks, check =>
        {
            var id = (string)check!["checkId"]!;
            var state = expected.Checks.TryGetValue(id, out var change) ? change.State : SyntheticCheckState.Same;
            Assert.Equal((id, char.ToLowerInvariant(state.ToString()[0]) + state.ToString()[1..]), (id, (string)check["state"]!));
        });

        var titles = checks.Single(check => (string)check!["checkId"]! == "title-too-long")!;
        Assert.Equal((3, 2, 1, 1, 1, 1), ((int)titles["then"]!, (int)titles["now"]!, (int)titles["rows"]!["fixed"]!, (int)titles["rows"]!["notChecked"]!,
            (int)titles["rows"]!["still"]!, (int)titles["rows"]!["newUrl"]!));
        Assert.Equal(IssueCatalog.TITLE_TOO_LONG.Severity.ToString().ToLowerInvariant(), (string?)titles["severity"]);
        Assert.Equal(expected.Checks.Values.Count(check => check.State == SyntheticCheckState.New), (int)comparison["totals"]!["checks"]!["new"]!);
        Assert.Equal(expected.Checks.Values.Sum(check => check.Count(SyntheticRowState.Gone)), (int)comparison["totals"]!["issues"]!["gone"]!);
    }

    /// <summary>One check: each URL it flags now and how it moved, each it flagged then and why it's not flagged now, and its causes.</summary>
    [Fact]
    public async Task CompareListsOneChecksRowsAndCauses()
    {
        var titles = await RunAsync("compare", _now, _then, "--check", "title-too-long");
        Assert.True(titles.ExitCode == 0, titles.Error);
        Assert.Contains("title-too-long: fewer, 3 then, 2 now", titles.Output);
        Assert.Contains($"  new URL          {Url("/plants/title-long-new")}", titles.Output);
        Assert.Contains($"  still            {Url("/plants/title-wide-long")}", titles.Output);
        Assert.Contains($"  not checked      {Url("/plants/title-caps")}  (failed: 500 Internal Server Error)", titles.Output);
        Assert.Contains($"  fixed            {Url("/plants/title-long")}", titles.Output);

        var json = JsonNode.Parse((await RunAsync("compare", _now, _then, "--check", "title-too-long", "--json")).Output)!;
        Assert.Equal("fewer", (string?)json["check"]!["state"]);
        Assert.Equal(2, json["rows"]!.AsArray().Count);
        var caps = json["cleared"]!.AsArray().Single(row => (string)row!["url"]! == Url("/plants/title-caps"))!;
        Assert.Equal(("notChecked", "failed", "500 Internal Server Error"), ((string)caps["state"]!, (string)caps["reason"]!["code"]!, (string)caps["reason"]!["text"]!));
        Assert.Empty(json["groups"]!.AsArray());

        // A check grouped by the broken URL: the URLs whose links were restored or dropped have no page linking them now.
        var broken = await RunAsync("compare", _now, _then, "--check", "broken-internal-links");
        Assert.Contains("Causes now (", broken.Output);
        Assert.Contains("Causes then with no URL now (", broken.Output);
        Assert.Contains($"1 then: 1 fixed, 0 gone, 0 not checked  {Url("/broken/29")}", broken.Output);
    }

    /// <summary>The user's mistakes get one line each and exit code 2: a missing file, one never analysed, one that isn't a crawl, an unknown check.</summary>
    [Fact]
    public async Task CompareMistakesGetOneLineMessages()
    {
        AssertFails(await RunAsync("compare", _now, Path.Combine(_folder, "missing.sicrawl")), "No crawl file at");
        AssertFails(await RunAsync("compare", Path.Combine(_folder, "missing.sicrawl"), _then), "No crawl file at");

        var unanalysed = Path.Combine(_folder, "unanalysed.sicrawl");
        Assert.Equal(0, (await RunAsync("crawl", _server.BaseUri.AbsoluteUri, "--db", unanalysed, "--max-urls", "20", "-q", "--no-analyze")).ExitCode);
        AssertFails(await RunAsync("compare", _now, unanalysed), "hasn't been analysed yet");

        var text = Path.Combine(_folder, "notes.sicrawl");
        await File.WriteAllTextAsync(text, "not a crawl");
        AssertFails(await RunAsync("compare", text, _then), "That file isn't a Shouting Iguana crawl.");

        AssertFails(await RunAsync("compare", _now, _then, "--check", "no-such-check"), "Unknown check 'no-such-check'");
    }

    /// <summary>
    /// The reader benchmark times the comparison as its own class, held to the design's 300 ms: the summary, a check's rows
    /// and groups with the other crawl, and the first page of what it no longer flags; and fails a read over a budget.
    /// </summary>
    [Fact]
    public async Task ReadbenchTimesTheComparison()
    {
        var path = Path.Combine(_folder, "readbench.json");
        var timed = await RunAsync("readbench", _now, "--compare-with", _then, "--runs", "2", "--only", "Compare", "--gate", "--json", path);
        Assert.True(timed.ExitCode == 0, timed.Output + timed.Error);
        var cases = JsonNode.Parse(await File.ReadAllTextAsync(path))!["cases"]!.AsArray();
        Assert.Equal(4, cases.Count);
        Assert.All(cases, read => Assert.Equal(("compare", "compare"), ((string)read!["kind"]!, (string)read["class"]!)));
        Assert.Equal(300d, ReaderBenchmark.GATE_MS["compare"]);
        Assert.Contains(cases, read => ((string)read!["name"]!).StartsWith("Compare: the summary", StringComparison.Ordinal));
        Assert.Contains(cases, read => ((string)read!["name"]!).StartsWith("Compare: rows of ", StringComparison.Ordinal));
        Assert.Contains(cases, read => ((string)read!["name"]!).StartsWith("Compare: groups of ", StringComparison.Ordinal));
        Assert.Contains(cases, read => ((string)read!["name"]!).Contains("no longer flags", StringComparison.Ordinal));

        var over = await RunAsync("readbench", _now, "--compare-with", _then, "--runs", "1", "--only", "Compare", "--max-p95-ms", "0.000001");
        Assert.Equal(1, over.ExitCode);
        Assert.Contains("FAILED       Compare: the summary", over.Output);

        // Without --compare-with, no comparison is timed; with a file that isn't there, nothing is.
        var plain = await RunAsync("readbench", _now, "--runs", "1", "--only", "Compare");
        Assert.Equal(2, plain.ExitCode);
        AssertFails(await RunAsync("readbench", _now, "--compare-with", Path.Combine(_folder, "missing.sicrawl")), "No crawl file at");
    }

    /// <summary>
    /// The benchmark with --revisions: crawls the site, switches it to revision 1 through its control route, crawls it again
    /// into a second file, and holds the comparison of the two to what the site planted; with --db both are kept, never
    /// over a file already there.
    /// </summary>
    [Fact]
    public async Task BenchComparesTheSitesTwoRevisions()
    {
        var crawl = Path.Combine(_folder, "bench.sicrawl");
        var json = Path.Combine(_folder, "bench.json");
        var result = await RunAsync("bench", "--pages", PAGES.ToString(System.Globalization.CultureInfo.InvariantCulture), "--revisions", "--db", crawl, "--json", json);
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        Assert.Contains("Switching the site to revision 1", result.Output);
        Assert.Contains("Comparison   revision 1 with revision 0: exactly what the site planted", result.Output);
        var bench = JsonNode.Parse(await File.ReadAllTextAsync(json))!;
        Assert.Empty(bench["comparisonMismatches"]!.AsArray());
        Assert.Empty(bench["failures"]!.AsArray());
        Assert.True((long)bench["comparedRows"]! > 0);
        Assert.True((bool)bench["afterFixes"]!["correct"]!);
        Assert.True(File.Exists(crawl));
        var second = Path.Combine(_folder, "bench.revision1.sicrawl");
        Assert.True(File.Exists(second));

        // The comparison si compare reads from the two is the one the benchmark held.
        Assert.Equal(0, (await RunAsync("compare", second, crawl)).ExitCode);

        // Either file already there: refused, before any crawl.
        File.Delete(crawl);
        var again = await RunAsync("bench", "--pages", "10", "--revisions", "--db", crawl);
        Assert.Equal(2, again.ExitCode);
        Assert.Contains($"There's already a file at {second}", again.Error);
    }

    private async Task<string> CrawlAsync(string name)
    {
        var crawl = Path.Combine(_folder, $"{name}.sicrawl");
        var result = await RunAsync("crawl", _server.BaseUri.AbsoluteUri, "--db", crawl, "-q");
        Assert.True(result.ExitCode == 0, result.Error);
        return crawl;
    }

    private static SyntheticJudging Judging(string checkId) => Enum.Parse<SyntheticJudging>(CheckJudging.Of(IssueCatalog.Find(checkId)!).ToString());
}
