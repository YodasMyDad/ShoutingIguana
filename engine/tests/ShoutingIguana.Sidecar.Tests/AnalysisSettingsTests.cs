using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Sidecar.Crawls;
using ShoutingIguana.Sidecar.Library;
using ShoutingIguana.Sidecar.Options;
using ShoutingIguana.Sidecar.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>
/// The analysis's limits as Settings reads and changes them: every limit described, saved to the state folder and kept
/// across a restart, checked field by field, put back one at a time or all at once; and what a change does to the crawls
/// already analysed, and to an analysis under way.
/// </summary>
public sealed class AnalysisSettingsTests : IAsyncLifetime
{
    private const string PATH = "api/analysis-settings";

    private SyntheticSiteServer _site = null!;
    private TestSidecar _sidecar = null!;

    public async Task InitializeAsync()
    {
        _site = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 60 });
        _sidecar = await TestSidecar.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _sidecar.DisposeAsync();
        await _site.DisposeAsync();
    }

    [Fact]
    public async Task EveryLimitIsDescribedWithItsDefaultUnitAndRange()
    {
        var state = await _sidecar.GetJsonAsync(PATH);
        Assert.Equal(["settings", "limits", "isDefault"], state.AsObject().Select(property => property.Key));
        Assert.True((bool)state["isDefault"]!);

        // Every limit the engine judges by is one Settings can change: its JSON name, its value the engine's.
        var engine = typeof(AnalysisSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name), property => Convert.ToInt64(property.GetValue(new AnalysisSettings())));
        var limits = state["limits"]!.AsArray();
        Assert.Equal(engine.Keys.Order(), limits.Select(limit => (string)limit!["id"]!).Order());
        Assert.Equal(limits.Select(limit => (string)limit!["id"]!), state["settings"]!.AsObject().Select(setting => setting.Key));
        foreach (var limit in limits)
        {
            Assert.Equal(["id", "value", "default", "unit", "min", "max", "description"], limit!.AsObject().Select(property => property.Key));
            var id = (string)limit["id"]!;
            Assert.Equal((engine[id], engine[id], engine[id]), ((long)limit["value"]!, (long)limit["default"]!, (long)state["settings"]![id]!));
            Assert.InRange((long)limit["default"]!, (long)limit["min"]!, (long)limit["max"]!);
            Assert.Contains((string)limit["unit"]!, new[] { "characters", "words", "bits", "bytes", "ms" });
            Assert.EndsWith(".", (string)limit["description"]!, StringComparison.Ordinal);
            Assert.InRange(((string)limit["description"]!).Length, 20, 200);
        }

        Assert.Equal("""{"id":"titleMaxLength","value":60,"default":60,"unit":"characters","min":1,"max":10000,"description":"A title longer than this is flagged as too long: search results cut it short."}""",
            limits.Single(limit => (string?)limit!["id"] == "titleMaxLength")!.ToJsonString());
        Assert.Equal(("ms", "bytes", "bits", 32L), ((string?)Limit(state, "slowResponseMs")["unit"], (string?)Limit(state, "largeImageBytes")["unit"],
            (string?)Limit(state, "nearDuplicateMaxBits")["unit"], (long)Limit(state, "nearDuplicateMaxBits")["max"]!));

        // Whatever the sidecar's ranges allow, the engine's own check allows too.
        foreach (var limit in AnalysisLimits.All)
        {
            foreach (var value in new[] { limit.Min, limit.Max })
            {
                var others = limit.Id switch { "titleMaxLength" => "titleMinLength", "descriptionMaxLength" => "descriptionMinLength", _ => null };
                var settings = AnalysisLimits.Apply(new Dictionary<string, long> { [limit.Id] = value }.Concat(others is null ? [] : [KeyValuePair.Create(others, 0L)]).ToDictionary(), out var errors);
                Assert.True(errors.Count == 0 || limit.Id.EndsWith("MinLength", StringComparison.Ordinal), $"{limit.Id} = {value}: {string.Join("; ", errors.SelectMany(error => error.Value))}");
                Assert.Empty(errors.Count == 0 ? settings.Validate() : []);
            }
        }
    }

    [Fact]
    public async Task SavedLimitsAreInForceAndKeptAcrossARestart()
    {
        var root = TestSidecar.NewRoot();
        try
        {
            await using (var first = await TestSidecar.StartAsync(root))
            {
                var saved = await first.SendAsync(HttpMethod.Put, PATH, JsonNode.Parse("""{"titleMaxLength":65,"minWordCount":300,"largeImageBytes":5000000000,"slowResponseMs":1000}"""));
                Assert.Equal(HttpStatusCode.OK, saved.Status);
                Assert.Equal((65, 300, 5_000_000_000, false), ((long)saved.Body!["settings"]!["titleMaxLength"]!, (long)saved.Body["settings"]!["minWordCount"]!,
                    (long)saved.Body["settings"]!["largeImageBytes"]!, (bool)saved.Body["isDefault"]!));
                Assert.Equal((65, 60), ((long)Limit(saved.Body, "titleMaxLength")["value"]!, (long)Limit(saved.Body, "titleMaxLength")["default"]!));
                Assert.Equal(saved.Body.ToJsonString(), (await first.GetJsonAsync(PATH)).ToJsonString());

                // Only what differs from the engine's is written, so a limit never changed follows the engine's default.
                Assert.Equal("""{"titleMaxLength":65,"minWordCount":300,"largeImageBytes":5000000000}""",
                    await File.ReadAllTextAsync(Path.Combine(first.Options.StateDirectory, "analysis-settings.json")));
                Assert.Equal(65, first.Host.Services.GetRequiredService<CrawlManager>().AnalysisSettings.TitleMaxLength);
            }

            await using (var second = await TestSidecar.StartAsync(root))
            {
                var kept = await second.GetJsonAsync(PATH);
                Assert.Equal((65, 300, 5_000_000_000, 30, false), ((long)kept["settings"]!["titleMaxLength"]!, (long)kept["settings"]!["minWordCount"]!,
                    (long)kept["settings"]!["largeImageBytes"]!, (long)kept["settings"]!["titleMinLength"]!, (bool)kept["isDefault"]!));

                // A save takes what it's given: a limit left out, or null, goes back to the engine's.
                var replaced = (await second.SendAsync(HttpMethod.Put, PATH, JsonNode.Parse("""{"minWordCount":250,"titleMaxLength":null}"""))).Body!;
                Assert.Equal((60, 250, 204_800), ((long)replaced["settings"]!["titleMaxLength"]!, (long)replaced["settings"]!["minWordCount"]!, (long)replaced["settings"]!["largeImageBytes"]!));
                Assert.True((bool)(await second.SendAsync(HttpMethod.Put, PATH, JsonNode.Parse("{}"))).Body!["isDefault"]!);
            }

            // A file edited by hand: what can't be used is passed over, and the rest kept.
            await File.WriteAllTextAsync(Path.Combine(root, "state", "analysis-settings.json"), """{"titleMaxLength":"long","minWordCount":123,"slowResponseMs":-5,"noSuchLimit":1}""");
            await using (var third = await TestSidecar.StartAsync(root))
            {
                var read = await third.GetJsonAsync(PATH);
                Assert.Equal((60, 123, 1_000), ((long)read["settings"]!["titleMaxLength"]!, (long)read["settings"]!["minWordCount"]!, (long)read["settings"]!["slowResponseMs"]!));
            }

            await File.WriteAllTextAsync(Path.Combine(root, "state", "analysis-settings.json"), "not json at all");
            await using var fourth = await TestSidecar.StartAsync(root);
            Assert.True((bool)(await fourth.GetJsonAsync(PATH))["isDefault"]!);
        }
        finally
        {
            TestSidecar.DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ALimitIsCheckedByItselfAndNothingIsSavedWhenOneIsWrong()
    {
        (string Body, string[] Fields)[] wrong =
        [
            ("""{"titleMaxLength":0}""", ["titleMaxLength"]),
            ("""{"titleMaxLength":10001}""", ["titleMaxLength"]),
            ("""{"titleMinLength":-1,"minWordCount":250}""", ["titleMinLength"]),
            ("""{"titleMinLength":60}""", ["titleMaxLength"]),
            ("""{"titleMinLength":70,"titleMaxLength":65}""", ["titleMaxLength"]),
            ("""{"descriptionMinLength":200}""", ["descriptionMaxLength"]),
            ("""{"nearDuplicateMaxBits":33}""", ["nearDuplicateMaxBits"]),
            ("""{"slowResponseMs":0,"maxUrlLength":0,"largePageBytes":0}""", ["largePageBytes", "maxUrlLength", "slowResponseMs"]),
            ("""{"minWordCount":"many"}""", ["minWordCount"]),
            ("""{"minWordCount":12.5}""", ["minWordCount"]),
            ("""{"minWordCount":true,"imageAltMaxLength":[125]}""", ["imageAltMaxLength", "minWordCount"]),
            ("""{"largeImageBytes":99999999999999999999}""", ["largeImageBytes"])
        ];
        foreach (var (body, fields) in wrong)
        {
            var refused = TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Put, PATH, JsonNode.Parse(body)), HttpStatusCode.UnprocessableEntity, "invalid-settings");
            Assert.Equal(fields, refused["errors"]!.AsObject().Select(error => error.Key).Order(StringComparer.Ordinal));
            Assert.All(refused["errors"]!.AsObject(), error => Assert.False(string.IsNullOrWhiteSpace((string?)error.Value![0])));

            // The same messages as they're typed, without a refusal.
            var (status, validated) = await _sidecar.PostAsync(PATH + "/validate", JsonNode.Parse(body));
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(refused["errors"]!.ToJsonString(), validated!["errors"]!.ToJsonString());
        }

        Assert.Equal("This must be from 1 to 10,000 characters.", (string?)TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Put, PATH, JsonNode.Parse("""{"titleMaxLength":0}""")),
            HttpStatusCode.UnprocessableEntity, "invalid-settings")["errors"]!["titleMaxLength"]![0]);
        Assert.Equal("The longest title allowed must be more than the shortest (70).",
            (string?)(await _sidecar.PostAsync(PATH + "/validate", JsonNode.Parse("""{"titleMinLength":70,"titleMaxLength":65}"""))).Body!["errors"]!["titleMaxLength"]![0]);
        Assert.Equal("This must be a whole number.", (string?)(await _sidecar.PostAsync(PATH + "/validate", JsonNode.Parse("""{"minWordCount":12.5}"""))).Body!["errors"]!["minWordCount"]![0]);
        Assert.Equal("{}", (await _sidecar.PostAsync(PATH + "/validate", JsonNode.Parse("""{"titleMinLength":10,"titleMaxLength":11,"minWordCount":null}"""))).Body!["errors"]!.ToJsonString());

        // Nothing was saved by any of it, and checking saves nothing.
        Assert.True((bool)(await _sidecar.GetJsonAsync(PATH))["isDefault"]!);
        Assert.False(File.Exists(Path.Combine(_sidecar.Options.StateDirectory, "analysis-settings.json")));

        // A limit that doesn't exist, or a body that isn't an object of them, is a bad request, as everywhere.
        foreach (var path in new[] { PATH, PATH + "/validate" })
        {
            var method = path == PATH ? HttpMethod.Put : HttpMethod.Post;
            var unknown = TestSidecar.AssertProblem(await _sidecar.SendAsync(method, path, JsonNode.Parse("""{"titleMaxLenght":65}""")), HttpStatusCode.BadRequest, "bad-request");
            Assert.Contains("titleMaxLenght", (string?)unknown["detail"], StringComparison.Ordinal);
            TestSidecar.AssertProblem(await _sidecar.SendAsync(method, path, JsonNode.Parse("[1,2]")), HttpStatusCode.BadRequest, "bad-request");
            TestSidecar.AssertProblem(await _sidecar.SendAsync(method, path), HttpStatusCode.BadRequest, "bad-request");
        }
    }

    [Fact]
    public async Task OneLimitOrAllOfThemGoBackToTheEngines()
    {
        await _sidecar.SendAsync(HttpMethod.Put, PATH, JsonNode.Parse("""{"titleMinLength":70,"titleMaxLength":90,"minWordCount":300,"slowResponseMs":2000}"""));

        var one = (await _sidecar.PostAsync(PATH + "/reset", JsonNode.Parse("""{"limits":["minWordCount"]}"""))).Body!;
        Assert.Equal((200, 70, 90, 2_000), ((long)one["settings"]!["minWordCount"]!, (long)one["settings"]!["titleMinLength"]!, (long)one["settings"]!["titleMaxLength"]!,
            (long)one["settings"]!["slowResponseMs"]!));
        Assert.Equal(one.ToJsonString(), (await _sidecar.GetJsonAsync(PATH)).ToJsonString());

        // The longest title back at the engine's 60 would be under the shortest, 70: the pair goes back together.
        var pair = (await _sidecar.PostAsync(PATH + "/reset", JsonNode.Parse("""{"limits":["titleMaxLength"]}"""))).Body!;
        Assert.Equal((30, 60, 2_000), ((long)pair["settings"]!["titleMinLength"]!, (long)pair["settings"]!["titleMaxLength"]!, (long)pair["settings"]!["slowResponseMs"]!));

        var unknown = TestSidecar.AssertProblem(await _sidecar.PostAsync(PATH + "/reset", JsonNode.Parse("""{"limits":["slowResponseMs","noSuchLimit",null]}""")),
            HttpStatusCode.UnprocessableEntity, "invalid-settings");
        Assert.Equal(["limits[1]", "limits[2]"], unknown["errors"]!.AsObject().Select(error => error.Key));
        Assert.Equal(2_000, (long)(await _sidecar.GetJsonAsync(PATH))["settings"]!["slowResponseMs"]!);
        TestSidecar.AssertProblem(await _sidecar.PostAsync(PATH + "/reset", JsonNode.Parse("""{"limit":"slowResponseMs"}""")), HttpStatusCode.BadRequest, "bad-request");
        TestSidecar.AssertProblem(await _sidecar.PostAsync(PATH + "/reset", JsonNode.Parse("""{"limits":"slowResponseMs"}""")), HttpStatusCode.BadRequest, "bad-request");

        // All of them: with no body, with none named.
        Assert.True((bool)(await _sidecar.PostAsync(PATH + "/reset")).Body!["isDefault"]!);
        await _sidecar.SendAsync(HttpMethod.Put, PATH, JsonNode.Parse("""{"maxUrlLength":200}"""));
        Assert.True((bool)(await _sidecar.PostAsync(PATH + "/reset", JsonNode.Parse("""{"limits":[]}"""))).Body!["isDefault"]!);
        Assert.Equal("{}", await File.ReadAllTextAsync(Path.Combine(_sidecar.Options.StateDirectory, "analysis-settings.json")));
    }

    /// <summary>
    /// An analysis that ran with other limits than the ones now in force is out of date, and says why: for a crawl listed
    /// before the change as for one asked about after it. Analysing it again runs it with the new limits, which it stores,
    /// and it's up to date until they change again.
    /// </summary>
    [Fact]
    public async Task AChangeOfLimitsPutsEveryAnalysisOutOfDateUntilItRunsAgain()
    {
        var path = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "analysed.sicrawl"), analyse: true);
        var other = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "other.sicrawl"), analyse: true);
        var (id, otherId) = (CrawlIds.For(path), CrawlIds.For(other));
        Assert.All((await _sidecar.GetJsonAsync("api/crawls")).AsArray(), item => Assert.False((bool)item!["outOfDate"]!));
        Assert.Empty((await _sidecar.GetJsonAsync($"api/crawls/{id}/analysis"))["outOfDateReasons"]!.AsArray());
        var before = (long)(await _sidecar.GetJsonAsync($"api/crawls/{id}/analysis"))["counts"]!.AsArray().Single(count => (string?)count!["checkId"] == "title-too-long")!["count"]!;

        // Saving the limits as they are changes nothing.
        await _sidecar.SendAsync(HttpMethod.Put, PATH, JsonNode.Parse("""{"titleMaxLength":60}"""));
        Assert.All((await _sidecar.GetJsonAsync("api/crawls")).AsArray(), item => Assert.False((bool)item!["outOfDate"]!));

        // A shorter longest title: both crawls' analyses ran with another, in the library's list and each one's own answer.
        Assert.Equal(HttpStatusCode.OK, (await _sidecar.SendAsync(HttpMethod.Put, PATH, JsonNode.Parse("""{"titleMinLength":5,"titleMaxLength":12}"""))).Status);
        Assert.All((await _sidecar.GetJsonAsync("api/crawls")).AsArray(), item => Assert.True((bool)item!["outOfDate"]!));
        foreach (var crawl in new[] { id, otherId })
        {
            Assert.True((bool)(await _sidecar.GetJsonAsync($"api/crawls/{crawl}"))["outOfDate"]!);
            var stale = await _sidecar.GetJsonAsync($"api/crawls/{crawl}/analysis");
            Assert.True((bool)stale["outOfDate"]!);
            Assert.Equal(["settingsChanged"], stale["outOfDateReasons"]!.AsArray().Select(reason => (string?)reason));
        }

        // Its results are still the old limits', and its grids still read: only the notice changes.
        var home = (int)(await _sidecar.PostAsync($"api/crawls/{id}/urls/lookup", new JsonObject { ["url"] = "/" })).Body!["id"]!;
        var inspected = (await _sidecar.PostAsync($"api/crawls/{id}/urls/inspect", new JsonObject { ["urlId"] = home })).Body!;
        Assert.Equal((60, true, true), ((int)inspected["limits"]!["titleMax"]!, (bool)inspected["limits"]!["fromAnalysis"]!, (bool)inspected["summary"]!["available"]!));

        // Analysed again, it ran with the limits in force, flags more titles as too long, and stands.
        await using var events = await EventStreamReader.ConnectAsync(_sidecar.Client);
        Assert.Equal(HttpStatusCode.Accepted, (await _sidecar.PostAsync($"api/crawls/{id}/analyze")).Status);
        await events.CrawlStatusAsync(id, "analyzing");
        Assert.Null((await events.CrawlStatusAsync(id, "completed"))["error"]);
        var again = await _sidecar.GetJsonAsync($"api/crawls/{id}/analysis");
        Assert.Equal((false, "[]"), ((bool)again["outOfDate"]!, again["outOfDateReasons"]!.ToJsonString()));
        Assert.True((long)again["counts"]!.AsArray().Single(count => (string?)count!["checkId"] == "title-too-long")!["count"]! > before);
        Assert.Equal(12, IssueReader.ReadAnalysis(path)!.Settings!.TitleMaxLength);
        Assert.Equal(12, (int)(await _sidecar.PostAsync($"api/crawls/{id}/urls/inspect", new JsonObject { ["urlId"] = home })).Body!["limits"]!["titleMax"]!);
        Assert.False((bool)(await _sidecar.GetJsonAsync($"api/crawls/{id}"))["outOfDate"]!);

        // The other still ran with the old ones; put back, it stands again and the one analysed since doesn't.
        Assert.True((bool)(await _sidecar.GetJsonAsync($"api/crawls/{otherId}"))["outOfDate"]!);
        Assert.True((bool)(await _sidecar.PostAsync(PATH + "/reset")).Body!["isDefault"]!);
        Assert.False((bool)(await _sidecar.GetJsonAsync($"api/crawls/{otherId}"))["outOfDate"]!);
        Assert.Equal(["settingsChanged"], (await _sidecar.GetJsonAsync($"api/crawls/{id}/analysis"))["outOfDateReasons"]!.AsArray().Select(reason => (string?)reason));
    }

    /// <summary>
    /// Limits saved while an analysis runs don't reach it: it runs to its end with the ones in force as it began, stores
    /// those, and is out of date the moment it's done, as any analysis that ran with other limits is.
    /// </summary>
    [Fact]
    public async Task AnAnalysisUnderWayKeepsTheLimitsItBeganWith()
    {
        var path = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "under-way.sicrawl"));
        var id = CrawlIds.For(path);
        await _sidecar.SendAsync(HttpMethod.Put, PATH, JsonNode.Parse("""{"titleMaxLength":40}"""));

        var manager = _sidecar.Host.Services.GetRequiredService<CrawlManager>();
        var store = _sidecar.Host.Services.GetRequiredService<AnalysisSettingsStore>();
        var changed = 0;
        manager.AnalysisStarting = starting =>
        {
            // The analysis has taken its limits and is about to run: they change under it.
            if (starting == id && Interlocked.Exchange(ref changed, 1) == 0)
            {
                store.Save(new Dictionary<string, JsonElement> { ["titleMinLength"] = JsonSerializer.SerializeToElement(5), ["titleMaxLength"] = JsonSerializer.SerializeToElement(12) });
            }
        };
        try
        {
            await using var events = await EventStreamReader.ConnectAsync(_sidecar.Client);
            Assert.Equal(HttpStatusCode.Accepted, (await _sidecar.PostAsync($"api/crawls/{id}/analyze")).Status);
            await events.CrawlStatusAsync(id, "analyzing");
            Assert.Null((await events.CrawlStatusAsync(id, "completed"))["error"]);
        }
        finally
        {
            manager.AnalysisStarting = null;
        }

        Assert.Equal(1, Volatile.Read(ref changed));
        Assert.Equal(12, (long)(await _sidecar.GetJsonAsync(PATH))["settings"]!["titleMaxLength"]!);
        Assert.Equal(40, IssueReader.ReadAnalysis(path)!.Settings!.TitleMaxLength);
        var analysis = await _sidecar.GetJsonAsync($"api/crawls/{id}/analysis");
        Assert.Equal((true, """["settingsChanged"]"""), ((bool)analysis["outOfDate"]!, analysis["outOfDateReasons"]!.ToJsonString()));
        Assert.True((bool)(await _sidecar.GetJsonAsync($"api/crawls/{id}"))["outOfDate"]!);
    }

    private static JsonNode Limit(JsonNode state, string id) => state["limits"]!.AsArray().Single(limit => (string?)limit!["id"] == id)!;
}
