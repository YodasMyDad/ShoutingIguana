using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using ShoutingIguana.Engine;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Sidecar.Api.Models;
using ShoutingIguana.Sidecar.Crawls;
using ShoutingIguana.Sidecar.Library;
using ShoutingIguana.Sidecar.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>Health and the check catalog.</summary>
public sealed class AppTests : IAsyncLifetime
{
    private TestSidecar _sidecar = null!;

    public async Task InitializeAsync() => _sidecar = await TestSidecar.StartAsync();

    public async Task DisposeAsync() => await _sidecar.DisposeAsync();

    [Fact]
    public async Task HealthNamesTheVersionsFoldersAndProcess()
    {
        var health = await _sidecar.GetJsonAsync("api/health");

        Assert.Equal(EngineInfo.Version, (string?)health["engineVersion"]);
        Assert.Equal(EngineInfo.Version, (string?)health["version"]);
        Assert.Equal(_sidecar.DataDirectory, (string?)health["dataDir"]);
        Assert.Equal(_sidecar.Options.StateDirectory, (string?)health["stateDir"]);
        Assert.Equal(Environment.ProcessId, (int?)health["pid"]);
    }

    [Fact]
    public async Task ChecksComeFromTheCatalogWithATitleForEveryCategory()
    {
        var checks = await _sidecar.GetJsonAsync("api/checks");

        var categories = checks["categories"]!.AsArray();
        Assert.Equal(Enum.GetValues<IssueCategory>().Length, categories.Count);
        Assert.All(categories, category => Assert.False(string.IsNullOrWhiteSpace((string?)category!["title"])));
        Assert.Contains(categories, category => (string?)category!["id"] == "metaDescriptions" && (string?)category["title"] == "Meta descriptions");

        // In catalog order, each in a listed category, with the texts the UI shows.
        var listed = checks["checks"]!.AsArray();
        Assert.Equal(IssueCatalog.All.Select(check => check.Id), listed.Select(check => (string?)check!["id"]));
        var categoryIds = categories.Select(category => (string?)category!["id"]).ToHashSet();
        Assert.All(listed, check => Assert.Contains((string?)check!["category"], categoryIds));
        var tooLong = listed.Single(check => (string?)check!["id"] == IssueCatalog.TITLE_TOO_LONG.Id)!;
        Assert.Equal(JsonNamingPolicy.CamelCase.ConvertName(IssueCatalog.TITLE_TOO_LONG.Severity.ToString()), (string?)tooLong["severity"]);
        Assert.Equal(IssueCatalog.TITLE_TOO_LONG.Title, (string?)tooLong["title"]);
        Assert.Equal(IssueCatalog.TITLE_TOO_LONG.Description, (string?)tooLong["description"]);
        Assert.Equal(IssueCatalog.TITLE_TOO_LONG.Why, (string?)tooLong["why"]);
        Assert.Equal(IssueCatalog.TITLE_TOO_LONG.Fix, (string?)tooLong["fix"]);
        Assert.Equal(IssueCatalog.TITLE_TOO_LONG.LearnMore, (string?)tooLong["learnMore"]);
        Assert.Equal("titles", (string?)tooLong["category"]);
        Assert.Null((string?)listed.Single(check => (string?)check!["id"] == IssueCatalog.IMAGE_BROKEN.Id)!["learnMore"]);
    }

    /// <summary>What the score and the wording of a count go by: the faults of the whole site, and the checks reported once.</summary>
    [Fact]
    public async Task ChecksSayWhenTheyAreAFaultOfTheWholeSiteAndWhenTheyAreReportedOnce()
    {
        var listed = (await _sidecar.GetJsonAsync("api/checks"))["checks"]!.AsArray();

        Assert.Equal(100, listed.Count);
        Assert.Equal(["duplicate-site-version", "no-https", "robots-txt-unreachable", "robots-txt-unreachable-other-origin", "site-hidden-from-search"],
            listed.Where(check => (bool)check!["siteWide"]!).Select(check => (string)check!["id"]!).Order(StringComparer.Ordinal));
        Assert.Equal(
        [
            "duplicate-site-version", "no-https", "robots-txt-missing", "robots-txt-unreachable", "robots-txt-unreachable-other-origin",
            "security-headers-missing", "site-hidden-from-search", "sitemap-error", "sitemap-missing"
        ], listed.Where(check => (bool)check!["reportedOnce"]!).Select(check => (string)check!["id"]!).Order(StringComparer.Ordinal));

        // The new check, with the texts the catalog gives it. What it flags is a page: the home page, noindex itself.
        var hidden = listed.Single(check => (string?)check!["id"] == "site-hidden-from-search")!;
        Assert.Equal(("indexability", "error", "page", "none", IssueCatalog.SITE_HIDDEN_FROM_SEARCH.Title),
            ((string)hidden["category"]!, (string)hidden["severity"]!, (string)hidden["subject"]!, (string)hidden["grouping"]!, (string)hidden["title"]!));
    }

    /// <summary>
    /// The plain words beside the technical ones: every check has them, in every part, and every category a plain title.
    /// A check with none (one a later catalog adds before its words are written) says null in every part: never an empty
    /// text, never the technical one.
    /// </summary>
    [Fact]
    public async Task ChecksAndCategoriesComeWithTheirPlainWordsOrNull()
    {
        var checks = await _sidecar.GetJsonAsync("api/checks");
        var listed = checks["checks"]!.AsArray();
        string[] required = ["plainTitle", "plainWhat", "plainFix", "fixer", "plainPassed"];
        string[] optional = ["plainOneFix", "plainLooksFor"];

        Assert.All(listed, node =>
        {
            var check = node!.AsObject();
            var id = (string)check["id"]!;
            Assert.All(required.Concat(optional), field => Assert.True(check.ContainsKey(field), $"{id} has no {field}"));

            // All of the required parts, or none: as the engine has them, with their limits filled.
            if (PlainLimits.Fill(IssueCatalog.Find(id)!.Plain, new AnalysisSettings()) is not { } plain)
            {
                Assert.All(required.Concat(optional), field => Assert.True(check[field] is null, $"{id}: {field} should be null"));
                return;
            }

            Assert.Equal((plain.Title, plain.Passed, JsonNamingPolicy.CamelCase.ConvertName(plain.Fixer.ToString()), plain.OneFix, plain.LooksFor),
                ((string?)check["plainTitle"], (string?)check["plainPassed"], (string?)check["fixer"], (string?)check["plainOneFix"], (string?)check["plainLooksFor"]));
            Assert.Equal(plain.What, check["plainWhat"]!.AsArray().Select(sentence => (string)sentence!));
            Assert.Equal(plain.Fix, check["plainFix"]!.AsArray().Select(step => (string)step!));
            Assert.All(check["plainWhat"]!.AsArray().Concat(check["plainFix"]!.AsArray()), text => Assert.DoesNotContain("{", (string)text!));
        });

        // Every one of the 100 has its words.
        var withWords = listed.Where(check => check!["plainTitle"] is not null).Select(check => (string)check!["id"]!).ToList();
        Assert.Equal(IssuePlainTexts.All.Keys.Order(StringComparer.Ordinal), withWords.Order(StringComparer.Ordinal));
        Assert.Equal(100, withWords.Count);
        var broken = listed.Single(check => (string?)check!["id"] == "broken-internal-links")!;
        Assert.Equal(("Links that lead to broken pages", "you", "Every link between your pages leads to a working page", 3, 3),
            ((string)broken["plainTitle"]!, (string)broken["fixer"]!, (string)broken["plainPassed"]!, broken["plainWhat"]!.AsArray().Count, broken["plainFix"]!.AsArray().Count));
        Assert.Equal("That's almost certainly your logo, which sits in the part of the site every page shares.",
            (string?)listed.Single(check => (string?)check!["id"] == "empty-anchor-text")!["plainOneFix"]);

        // The two optional parts: null where a check has none, the words where it has.
        var unreachable = listed.Single(check => (string?)check!["id"] == "robots-txt-unreachable")!;
        Assert.Equal(("A rules file that search engines can't read", (string?)null), ((string?)unreachable["plainLooksFor"], (string?)unreachable["plainOneFix"]));
        Assert.Equal(12, listed.Count(check => check!["plainLooksFor"] is not null));
        Assert.Equal(30, listed.Count(check => check!["plainOneFix"] is not null));

        // A check with no words says null in every part.
        IssueDefinition unwritten = new("check-of-a-later-catalog", IssueCategory.Links, IssueSeverity.Warning, "A later check", "What it is.", "How to fix it.")
        {
            Why = "Why it matters.",
            Subject = IssueSubject.Page,
            Grouping = IssueGrouping.None
        };
        var later = CheckDto.From(unwritten, new AnalysisSettings());
        Assert.Equal("A later check", later.Title);
        Assert.True(later is { PlainTitle: null, PlainWhat: null, PlainFix: null, Fixer: null, PlainPassed: null, PlainOneFix: null, PlainLooksFor: null });

        var categories = checks["categories"]!.AsArray();
        Assert.All(categories, category => Assert.False(string.IsNullOrWhiteSpace((string?)category!["plainTitle"])));
        Assert.Contains(categories, category => (string?)category!["id"] == "responseCodes" && (string?)category["plainTitle"] == "Pages that load");
        Assert.Contains(categories, category => (string?)category!["id"] == "canonicals" && (string?)category["plainTitle"] == "Duplicate addresses");
        Assert.Equal(
            Enum.GetValues<IssueCategory>().Select(PlainWords.CategoryTitle),
            Enum.GetValues<IssueCategory>().Select(category => (string?)categories.Single(served => (string?)served!["id"] == JsonNamingPolicy.CamelCase.ConvertName(category.ToString()))!["plainTitle"]));
    }

    /// <summary>
    /// The plain words say their limits by the settings an analysis would run with now, or, given a crawl, by the ones its
    /// last analysis ran with. A crawl never analysed has none of its own; an id no crawl has is an error, as everywhere.
    /// </summary>
    [Fact]
    public async Task TheChecksCanBeAskedForACrawlWhoseAnalysisSettingsFillTheirLimits()
    {
        await using var site = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 60 });
        var analysed = await TestCrawlFiles.CrawlAsync(site, Path.Combine(_sidecar.DataDirectory, "longer-titles.sicrawl"));
        await TestCrawlFiles.AnalyseWithAsync(analysed, new AnalysisSettings { TitleMaxPixels = 658, SlowResponseMs = 2_500 });
        var never = await TestCrawlFiles.CrawlAsync(site, Path.Combine(_sidecar.DataDirectory, "never-analysed.sicrawl"));
        var data = _sidecar.Host.Services.GetRequiredService<CrawlData>();
        static QueryCollection Query(params string[] crawls) => new(new Dictionary<string, StringValues> { ["crawl"] = crawls });

        // Whose settings: the crawl's analysis's own, or the current ones.
        Assert.Equal(new AnalysisSettings(), data.LimitsFor(new QueryCollection()));
        Assert.Equal(new AnalysisSettings { TitleMaxPixels = 658, SlowResponseMs = 2_500 }, data.LimitsFor(Query(CrawlIds.For(analysed))));
        Assert.Equal(new AnalysisSettings(), data.LimitsFor(Query(CrawlIds.For(never))));

        // And the catalog is served for either: the same checks, every plain text filled from those settings.
        var current = await _sidecar.GetJsonAsync("api/checks");
        var forCrawl = await _sidecar.GetJsonAsync($"api/checks?crawl={CrawlIds.For(analysed)}");
        Assert.Equal(current["checks"]!.AsArray().Select(check => (string)check!["id"]!), forCrawl["checks"]!.AsArray().Select(check => (string)check!["id"]!));
        var settings = new AnalysisSettings { TitleMaxPixels = 658, SlowResponseMs = 2_500 };
        Assert.All(forCrawl["checks"]!.AsArray(), check => Assert.Equal(
            PlainLimits.Fill(IssueCatalog.Find((string)check!["id"]!)!.Plain, settings)?.What, check["plainWhat"]?.AsArray().Select(sentence => (string)sentence!)));

        // What a reader gets differs, sentence for sentence: this crawl's analysis took titles to 658 px (about 70 characters)
        // and gave pages 2.5 seconds to answer, so its checks say so, while the current settings' say about 60 and 1 second.
        static string FirstSentence(JsonNode checks, string id) => (string)checks["checks"]!.AsArray().Single(check => (string)check!["id"]! == id)!["plainWhat"]![0]!;
        Assert.Equal("These pages have a title too long to show in full in search results, where there's room for about 60 characters.",
            FirstSentence(current, "title-too-long"));
        Assert.Equal("These pages have a title too long to show in full in search results, where there's room for about 70 characters.",
            FirstSentence(forCrawl, "title-too-long"));
        Assert.Equal("These pages took more than 1 second to arrive when the check asked for them.", FirstSentence(current, "slow-response"));
        Assert.Equal("These pages took more than 2.5 seconds to arrive when the check asked for them.", FirstSentence(forCrawl, "slow-response"));

        // A limit that analysis left as it was reads the same for both, and no token is served as written.
        Assert.Equal("Some pages show images whose files are over 200 KB.", FirstSentence(forCrawl, "image-too-large"));
        Assert.Equal(FirstSentence(current, "image-too-large"), FirstSentence(forCrawl, "image-too-large"));
        Assert.DoesNotContain("{limit:", forCrawl.ToJsonString());
        Assert.DoesNotContain("{limit:", current.ToJsonString());

        // A crawl never analysed has no settings of its own: the current ones.
        var forNever = await _sidecar.GetJsonAsync($"api/checks?crawl={CrawlIds.For(never)}");
        Assert.Equal(FirstSentence(current, "title-too-long"), FirstSentence(forNever, "title-too-long"));

        TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Get, "api/checks?crawl=0123456789abcdef"), HttpStatusCode.NotFound, "crawl-not-found");
        TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Get, "api/checks?crawl="), HttpStatusCode.BadRequest, "bad-request");
        TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Get, $"api/checks?crawl={CrawlIds.For(analysed)}&crawl={CrawlIds.For(never)}"),
            HttpStatusCode.BadRequest, "bad-request");
    }
}
