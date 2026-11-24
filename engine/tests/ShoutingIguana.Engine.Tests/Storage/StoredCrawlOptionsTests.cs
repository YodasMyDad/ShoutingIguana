using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Fetching;
using ShoutingIguana.Engine.Tests.Support;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// The browser fallback became on by default after crawls had been stored without it. A crawl file that doesn't say
/// reads as off, which is how that crawl ran, wherever its options are read: to continue or recheck it, to analyse
/// it, to list it, and to compare it. A file that says keeps what it says.
/// </summary>
public sealed class StoredCrawlOptionsTests : IDisposable
{
    private readonly CrawlFiles _files = new();

    public void Dispose() => _files.Dispose();

    [Fact]
    public void OptionsStoredBeforeTheBrowserFallbackReadAsOffAndStoredOnesAsStored()
    {
        var before = StoredCrawlOptions.Deserialize("""{"startUrl":"https://example.com/"}""")!;
        Assert.False(before.BrowserFallback);

        // Every other option the file lacks still takes the engine's default.
        var defaults = new CrawlOptions { StartUrl = "https://example.com/" };
        Assert.True(defaults.BrowserFallback);
        Assert.Equal(
            (defaults.UseInstalledBrowser, defaults.SolveChallenges, defaults.ChallengeWaitSeconds, defaults.Concurrency, defaults.RespectRobotsTxt, defaults.Rendering),
            (before.UseInstalledBrowser, before.SolveChallenges, before.ChallengeWaitSeconds, before.Concurrency, before.RespectRobotsTxt, before.Rendering));
        Assert.True(before.UseInstalledBrowser);

        Assert.True(StoredCrawlOptions.Deserialize("""{"startUrl":"https://example.com/","browserFallback":true}""")!.BrowserFallback);
        Assert.False(StoredCrawlOptions.Deserialize("""{"startUrl":"https://example.com/","browserFallback":false}""")!.BrowserFallback);
    }

    [Fact]
    public async Task ACrawlFileFromBeforeTheBrowserFallbackIsReadAsOffWhereverItsOptionsAreRead()
    {
        await using var site = await LoopbackServer.StartAsync(context =>
        {
            context.Response.ContentType = "text/html";
            return context.Response.WriteAsync("<!doctype html><title>Home</title><p>Home</p>");
        });
        var now = _files.NewPath();
        var options = new CrawlOptions { StartUrl = site.BaseUri.AbsoluteUri, DiscoverSitemaps = false };
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(options, now)).Status);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(now);

        // A new crawl stores that it had the fallback on.
        Assert.True(Stored(now)["browserFallback"]!.GetValue<bool>());
        Assert.True(CrawlDatabase.ReadOptions(now).BrowserFallback);
        var on = CrawlComparability.OptionsFingerprint(options);
        var off = CrawlComparability.OptionsFingerprint(options with { BrowserFallback = false });
        Assert.NotEqual(on, off);
        Assert.Equal(on, CrawlSummaryReader.Read(now).OptionsFingerprint);

        // The same crawl as a file from before the option: its options don't say.
        var then = _files.NewPath();
        File.Copy(now, then);
        var before = Stored(then);
        Assert.True(before.Remove("browserFallback"));
        Change(then, before.ToJsonString());

        // To continue or recheck it, and to report on it.
        Assert.False(CrawlDatabase.ReadOptions(then).BrowserFallback);
        Assert.False(CrawlDatabase.ReadOptions(then).PlanResume(new ResumeChanges()).Options.BrowserFallback);

        // To analyse it.
        using (var reader = AnalysisReader.Open(then))
        {
            Assert.False(reader.ReadOptions().BrowserFallback);
        }

        // To list it: its fingerprint is that of a crawl without the fallback.
        Assert.Equal(off, CrawlSummaryReader.Read(then).OptionsFingerprint);

        // To compare it: the difference is said as off then and on now, never as no difference.
        var difference = Assert.Single(CrawlComparisonReader.ReadSummary(now, then)!.Differences, difference => difference.Field == "browserFallback");
        Assert.Equal((ComparisonDifferenceCode.Fetching, false, true), (difference.Code, difference.Then!.GetValue<bool>(), difference.Now!.GetValue<bool>()));

        // A file that says off keeps it, and one that says on keeps that.
        before["browserFallback"] = false;
        Change(then, before.ToJsonString());
        Assert.False(CrawlDatabase.ReadOptions(then).BrowserFallback);
        before["browserFallback"] = true;
        Change(then, before.ToJsonString());
        Assert.True(CrawlDatabase.ReadOptions(then).BrowserFallback);
        Assert.Equal(on, CrawlSummaryReader.Read(then).OptionsFingerprint);
        Assert.DoesNotContain(CrawlComparisonReader.ReadSummary(now, then)!.Differences, difference => difference.Field == "browserFallback");
    }

    private static JsonObject Stored(string path)
    {
        using var connection = CrawlDatabase.OpenReader(path);
        return JsonNode.Parse((string)CrawlDatabase.Scalar(connection, "SELECT options_json FROM crawl WHERE id = 1")!)!.AsObject();
    }

    private static void Change(string path, string json)
    {
        var connection = CrawlDatabase.OpenWriter(path, allowNew: false);
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE crawl SET options_json = $json";
            command.Parameters.AddWithValue("$json", json);
            command.ExecuteNonQuery();
        }
        finally
        {
            CrawlDatabase.CloseWriter(connection, NullLogger.Instance);
        }
    }
}
