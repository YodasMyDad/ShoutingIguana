using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Sidecar.Crawls;
using ShoutingIguana.Sidecar.Tests.Support;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>
/// Search as it's typed, as the palette calls it: the JSON it gives, its count against the URL grid's under the same
/// search, the problems for a search it can't run, a crawl of any age or state searched alike, and a keystroke taking
/// no read slot once the crawl's text is kept.
/// </summary>
public sealed class SearchEndpointsTests(GridSidecar grid) : IClassFixture<GridSidecar>
{
    private TestSidecar Sidecar => grid.Sidecar;

    [Fact]
    public async Task ASearchGivesTheBestFewWithWhatAPaletteShows()
    {
        var found = await PostOkAsync($"api/crawls/{grid.Id}/search", JsonNode.Parse("""{"text":"P/1","limit":5}"""));
        Assert.Equal(["results", "matching", "total"], found.AsObject().Select(property => property.Key));
        var results = found["results"]!.AsArray();
        Assert.Equal(5, results.Count);
        Assert.All(results, result =>
        {
            Assert.Equal(["id", "url", "title", "isInternal", "statusCode", "outcome", "rank", "matchedIn"], result!.AsObject().Select(property => property.Key));
            Assert.Contains("/p/1", (string)result["url"]!, StringComparison.Ordinal);
            Assert.Equal(("url", true), ((string?)result["matchedIn"], (bool)result["isInternal"]!));
            Assert.Equal((200, "success"), ((int?)result["statusCode"], (string?)result["outcome"]));
        });

        // The page the text names comes first, then those it starts a segment of; each result is the URL its id opens.
        var first = results[0]!;
        Assert.Equal((grid.Url("/p/1"), "exact"), ((string?)first["url"], (string?)first["rank"]));
        Assert.All(results.Skip(1), result => Assert.Equal("start", (string?)result!["rank"]));
        var inspected = await PostOkAsync($"api/crawls/{grid.Id}/urls/inspect", new JsonObject { ["urlId"] = (int)first["id"]! });
        Assert.Equal((string?)first["url"], (string?)inspected["url"]);
        Assert.Equal((string?)first["title"], (string?)inspected["content"]!["titles"]![0]!["text"]);

        // The count is the URL grid's under the same search, so "all N in URLs" lands on N rows; ten results by default.
        foreach (var text in new[] { "P/1", "product", "/", "nothing matches this at all", "about" })
        {
            var searched = await PostOkAsync($"api/crawls/{grid.Id}/search", new JsonObject { ["text"] = text });
            var listed = await PostOkAsync($"api/crawls/{grid.Id}/urls", new JsonObject { ["search"] = text, ["limit"] = 1 });
            Assert.Equal(((long?)listed["matching"], (long?)listed["total"]), ((long?)searched["matching"], (long?)searched["total"]));
            Assert.Equal(Math.Min(UrlSearchReader.DEFAULT_LIMIT, (long)searched["matching"]!), searched["results"]!.AsArray().Count);
        }

        // By title, when no URL holds the text.
        var byTitle = (await PostOkAsync($"api/crawls/{grid.Id}/search", JsonNode.Parse("""{"text":"Product 1","limit":50}""")))["results"]!.AsArray();
        Assert.NotEmpty(byTitle);
        Assert.All(byTitle, result => Assert.Equal(("title", "start"), ((string?)result!["matchedIn"], (string?)result["rank"])));
        Assert.All(byTitle, result => Assert.StartsWith("Product 1", (string)result!["title"]!, StringComparison.Ordinal));
    }

    /// <summary>Nothing typed yet finds nothing and counts every URL, as the grid does with no search: the palette asks it as it opens.</summary>
    [Fact]
    public async Task AnEmptyTextFindsNothingAndCountsEveryUrl()
    {
        var nothing = await PostOkAsync($"api/crawls/{grid.Id}/search", JsonNode.Parse("""{"text":""}"""));
        Assert.Empty(nothing["results"]!.AsArray());
        Assert.Equal((long?)nothing["total"], (long?)nothing["matching"]);
        Assert.Equal((long?)(await PostOkAsync($"api/crawls/{grid.Id}/urls"))["total"], (long?)nothing["total"]);
    }

    [Fact]
    public async Task ASearchItCantRunIsAProblem()
    {
        var path = $"api/crawls/{grid.Id}/search";
        TestSidecar.AssertProblem(await Sidecar.PostAsync(path), HttpStatusCode.BadRequest, "bad-request");
        TestSidecar.AssertProblem(await Sidecar.PostAsync(path, JsonNode.Parse("""{"text":"a","offset":1}""")), HttpStatusCode.BadRequest, "bad-request");
        TestSidecar.AssertProblem(await Sidecar.PostAsync(path, JsonNode.Parse("""{"text":5}""")), HttpStatusCode.BadRequest, "bad-request");
        TestSidecar.AssertProblem(await Sidecar.PostAsync(path, JsonNode.Parse("""{"text":"a","limit":"ten"}""")), HttpStatusCode.BadRequest, "bad-request");
        Assert.Equal(["text"], Fields(TestSidecar.AssertProblem(await Sidecar.PostAsync(path, JsonNode.Parse("{}")), HttpStatusCode.UnprocessableEntity, "invalid-query")));
        Assert.Equal(["text"], Fields(TestSidecar.AssertProblem(await Sidecar.PostAsync(path, new JsonObject { ["text"] = new string('a', 1_001) }),
            HttpStatusCode.UnprocessableEntity, "invalid-query")));
        Assert.Equal(["text"], Fields(TestSidecar.AssertProblem(await Sidecar.PostAsync(path, new JsonObject { ["text"] = "a\0b" }), HttpStatusCode.UnprocessableEntity, "invalid-query")));
        foreach (var limit in new[] { 0, -1, 51 })
        {
            Assert.Equal(["limit"], Fields(TestSidecar.AssertProblem(await Sidecar.PostAsync(path, new JsonObject { ["text"] = "a", ["limit"] = limit }),
                HttpStatusCode.UnprocessableEntity, "invalid-query")));
        }

        Assert.Equal(["limit", "text"], Fields(TestSidecar.AssertProblem(await Sidecar.PostAsync(path, JsonNode.Parse("""{"limit":0}""")),
            HttpStatusCode.UnprocessableEntity, "invalid-query")).Order());
        TestSidecar.AssertProblem(await Sidecar.PostAsync("api/crawls/0123456789abcdef/search", JsonNode.Parse("""{"text":"a"}""")), HttpStatusCode.NotFound, "crawl-not-found");

        static IEnumerable<string> Fields(JsonNode problem) => problem["errors"]!.AsObject().Select(error => error.Key);
    }

    /// <summary>
    /// Nothing is kept in the crawl file for searching, so a crawl never analysed, one continued since its analysis and
    /// one an engine from before the URL summary analysed are searched as any other is, to the same answer.
    /// </summary>
    [Fact]
    public async Task ACrawlOfAnyAgeIsSearchedAlike()
    {
        var expected = (await PostOkAsync($"api/crawls/{grid.Id}/search", JsonNode.Parse("""{"text":"product","limit":50}"""))).ToJsonString();
        var earlier = grid.Copy(TestCrawlFiles.AsAnalysedByAnEarlierEngine);
        var continued = grid.Copy(TestCrawlFiles.StampAsCrawledAgain);
        var unanalysed = grid.Copy(copy => Change(copy, "DROP TABLE url_summary; DROP TABLE analysis; DROP TABLE issues; DROP TABLE issue_types"));
        foreach (var id in new[] { earlier, continued, unanalysed })
        {
            Assert.Equal(expected, (await PostOkAsync($"api/crawls/{id}/search", JsonNode.Parse("""{"text":"product","limit":50}"""))).ToJsonString());
        }

        Assert.NotEmpty((await PostOkAsync($"api/crawls/{grid.FreshId}/search", JsonNode.Parse("""{"text":"/"}""")))["results"]!.AsArray());
    }

    /// <summary>
    /// The first search of a crawl reads its URLs and titles, in a read slot as any pass over the file is. Every search
    /// after it reads them from memory and takes no slot, until a run of the crawl has stamped it and they're read again.
    /// </summary>
    [Fact]
    public async Task AKeystrokeTakesNoReadSlotOnceTheCrawlsTextIsKept()
    {
        var path = string.Empty;
        var id = grid.Copy(copy => path = copy);
        var data = Sidecar.Host.Services.GetRequiredService<GridData>();
        var reads = 0;
        data.ReadStarting = _ => Interlocked.Increment(ref reads);
        try
        {
            var first = await PostOkAsync($"api/crawls/{id}/search", JsonNode.Parse("""{"text":""}"""));
            Assert.Equal(1, Volatile.Read(ref reads));
            foreach (var text in new[] { "p", "pr", "pro", "prod", "nothing matches this" })
            {
                var typed = await PostOkAsync($"api/crawls/{id}/search", new JsonObject { ["text"] = text });
                Assert.Equal((long?)first["total"], (long?)typed["total"]);
            }

            Assert.Equal(1, Volatile.Read(ref reads));

            // A URL more behind the crawl's back, its stamp as it was: the text kept answers. Stamped, it's read again.
            Change(path, "INSERT INTO urls (url, is_internal, fetch_mode, depth, state, skip_reason, source) VALUES ('https://later.example/found-later', 0, 0, 1, 0, 0, 1)");
            Assert.Equal(0, (long?)(await PostOkAsync($"api/crawls/{id}/search", JsonNode.Parse("""{"text":"found-later"}""")))["matching"]);
            Assert.Equal(1, Volatile.Read(ref reads));
            Change(path, "UPDATE crawl SET updated_at = updated_at + 1000");
            Assert.Equal(1, (long?)(await PostOkAsync($"api/crawls/{id}/search", JsonNode.Parse("""{"text":"found-later"}""")))["matching"]);
            Assert.Equal(2, Volatile.Read(ref reads));
        }
        finally
        {
            data.ReadStarting = null;
        }
    }

    private async Task<JsonNode> PostOkAsync(string path, JsonNode? body = null)
    {
        var (status, answer) = await Sidecar.PostAsync(path, body);
        Assert.True(status == HttpStatusCode.OK, $"POST {path}: {(int)status} {answer?.ToJsonString()}");
        return answer!;
    }

    private static void Change(string path, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
