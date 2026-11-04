using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Tests.Fetching;
using ShoutingIguana.Engine.Tests.Support;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// Options and thresholds stored by an earlier engine lack the ones added since. Each must read as its default, never as
/// 0 or false: a new option with any other default would otherwise change a resumed crawl, and what an analysis checks.
/// </summary>
public sealed class StoredSettingsTests : IDisposable
{
    private readonly CrawlFiles _files = new();

    public void Dispose() => _files.Dispose();

    [Fact]
    public async Task StoredOptionsWithoutAnOptionTakeItsDefault()
    {
        var path = await CrawlAndAnalyseAsync();
        var stored = Stored(path, "SELECT options_json FROM crawl WHERE id = 1");
        var defaults = Serialized(new CrawlOptions { StartUrl = string.Empty }, EngineJsonContext.Default.CrawlOptions);
        Assert.Contains("concurrency", stored.Select(property => property.Key));

        foreach (var name in stored.Select(property => property.Key).Where(name => name != "startUrl").ToList())
        {
            var without = stored.DeepClone().AsObject();
            without.Remove(name);
            Change(path, "UPDATE crawl SET options_json = $json", without.ToJsonString());

            // As a resume reads them, and as the analysis does.
            AssertDefault(name, defaults, without, Serialized(CrawlDatabase.ReadOptions(path), EngineJsonContext.Default.CrawlOptions));
            using var reader = AnalysisReader.Open(path);
            AssertDefault(name, defaults, without, Serialized(reader.ReadOptions(), EngineJsonContext.Default.CrawlOptions));
        }

        // The start URL has no default: options without one are no crawl's.
        stored.Remove("startUrl");
        Change(path, "UPDATE crawl SET options_json = $json", stored.ToJsonString());
        Assert.Throws<JsonException>(() => CrawlDatabase.ReadOptions(path));
    }

    [Fact]
    public async Task StoredSettingsWithoutAThresholdTakeItsDefault()
    {
        var path = await CrawlAndAnalyseAsync();
        var stored = Stored(path, "SELECT settings_json FROM analysis WHERE id = 1");
        var defaults = Serialized(new AnalysisSettings(), EngineJsonContext.Default.AnalysisSettings);
        Assert.Equal(defaults.Select(property => property.Key), stored.Select(property => property.Key));

        foreach (var name in stored.Select(property => property.Key).ToList())
        {
            var without = stored.DeepClone().AsObject();
            without.Remove(name);
            Change(path, "UPDATE analysis SET settings_json = $json", without.ToJsonString());
            AssertDefault(name, defaults, without, Serialized(IssueReader.ReadAnalysis(path)!.Settings!, EngineJsonContext.Default.AnalysisSettings));
        }

        // Read as the thresholds an analysis runs with, so the analysis stands.
        Change(path, "UPDATE analysis SET settings_json = '{}'", null);
        Assert.Equal(new AnalysisSettings(), IssueReader.ReadAnalysis(path)!.Settings);
        Assert.Empty(CrawlSummaryReader.Read(path).OutOfDate(new AnalysisSettings()));
    }

    /// <summary>
    /// Analyses before the 256-bit fingerprint stored the near-duplicate threshold in bits of 64 (nearDuplicateMaxDistance,
    /// 6 by default). The threshold has a new name, so that value is never read as bits of 256: unknown, it's left out, and
    /// the threshold takes its default.
    /// </summary>
    [Fact]
    public async Task ANearDuplicateThresholdOfTheOldScaleIsNeverReadOnTheNew()
    {
        var path = await CrawlAndAnalyseAsync();
        var stored = Stored(path, "SELECT settings_json FROM analysis WHERE id = 1");
        Assert.Equal(7, stored["nearDuplicateMaxBits"]!.GetValue<int>());
        stored.Remove("nearDuplicateMaxBits");
        stored["nearDuplicateMaxDistance"] = 6;
        Change(path, "UPDATE analysis SET settings_json = $json", stored.ToJsonString());

        Assert.Equal(new AnalysisSettings().NearDuplicateMaxBits, IssueReader.ReadAnalysis(path)!.Settings!.NearDuplicateMaxBits);
    }

    /// <summary>The property left out reads as its default, and every other one as stored.</summary>
    private static void AssertDefault(string name, JsonObject defaults, JsonObject without, JsonObject read)
    {
        Assert.Equal(defaults[name]?.ToJsonString(), read[name]?.ToJsonString());
        Assert.All(without, property => Assert.Equal(property.Value?.ToJsonString(), read[property.Key]?.ToJsonString()));
    }

    /// <summary>A small crawl whose options and thresholds are all away from their defaults, so a default can't pass for what was stored.</summary>
    private async Task<string> CrawlAndAnalyseAsync()
    {
        await using var site = await LoopbackServer.StartAsync(context =>
        {
            context.Response.ContentType = "text/html";
            return context.Response.WriteAsync("<!doctype html><title>Home</title><p>Home</p>");
        });
        var path = _files.NewPath();
        var options = new CrawlOptions
        {
            StartUrl = site.BaseUri.AbsoluteUri, MaxUrls = 7, MaxDepth = 3, Concurrency = 2, ExternalConcurrency = 1, MaxRequestsPerSecond = 50,
            UserAgent = UserAgentPreset.Custom, CustomUserAgent = "Tester/1.0", AcceptLanguage = "fr-FR", ExtraHeaders = new Dictionary<string, string> { ["X-Test"] = "1" },
            RespectRobotsTxt = false, RespectCrawlDelay = true, CheckResources = false, CheckExternalLinks = false, IncludeSubdomains = true,
            StayInStartFolder = false, FollowNofollow = true, CrawlCanonicals = false, CrawlHreflang = true, CrawlPagination = true, DiscoverSitemaps = false,
            ExtraSitemaps = [site.Url("/extra.xml").AbsoluteUri], StoreHtml = true, UseCookies = false, RequestTimeoutSeconds = 9, MaxRetries = 1,
            MaxHtmlBytes = 1_000_000
        };
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(options, path)).Status);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path, new AnalysisSettings
        {
            TitleMinLength = 1, TitleMaxLength = 2, DescriptionMinLength = 3, DescriptionMaxLength = 4, MinWordCount = 5, DuplicateMinWordCount = 6,
            NearDuplicateMaxBits = 7, ImageAltMaxLength = 8, LargeImageBytes = 9, LargePageBytes = 10, SlowResponseMs = 11, MaxUrlLength = 12
        });
        return path;
    }

    private static JsonObject Stored(string path, string sql)
    {
        using var connection = CrawlDatabase.OpenReader(path);
        return JsonNode.Parse((string)CrawlDatabase.Scalar(connection, sql)!)!.AsObject();
    }

    private static JsonObject Serialized<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) =>
        JsonSerializer.SerializeToNode(value, typeInfo)!.AsObject();

    private static void Change(string path, string sql, string? json)
    {
        var connection = CrawlDatabase.OpenWriter(path, allowNew: false);
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            if (json is not null)
            {
                command.Parameters.AddWithValue("$json", json);
            }

            command.ExecuteNonQuery();
        }
        finally
        {
            CrawlDatabase.CloseWriter(connection, NullLogger.Instance);
        }
    }
}
