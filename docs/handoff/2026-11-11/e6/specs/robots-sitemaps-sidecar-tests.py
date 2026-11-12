P = 'tests/ShoutingIguana.Sidecar.Tests/LinksSitemapsRobotsEndpointsTests.cs'

EDITS = [
(P,
'''        var origins = await PostOkAsync($"api/crawls/{id}/robots");
        Assert.Equal(["total", "offset", "respected", "userAgent", "blocked", "origins"], origins.AsObject().Select(property => property.Key));
''',
'''        var origins = await PostOkAsync($"api/crawls/{id}/robots");
        Assert.Equal(["total", "offset", "respected", "userAgent", "blocked", "origins", "matching", "counts"], origins.AsObject().Select(property => property.Key));

        // With nothing asked every origin matches; the counts are what a filter bar shows, each the rows its filter gives.
        Assert.Equal((long)origins["total"]!, (long)origins["matching"]!);
        Assert.Equal($$"""{"all":{{(long)origins["total"]!}},"blocked":1,"notBlocked":{{(long)origins["total"]! - 1}}}""", origins["counts"]!.ToJsonString());
        var blocking = await PostOkAsync($"api/crawls/{id}/robots", JsonNode.Parse("""{"blocked":true}"""));
        Assert.Equal((site, 1L, (long)origins["total"]!, origins["counts"]!.ToJsonString()),
            ((string?)Assert.Single(blocking["origins"]!.AsArray())!["origin"], (long)blocking["matching"]!, (long)blocking["total"]!, blocking["counts"]!.ToJsonString()));
        var clear = await PostOkAsync($"api/crawls/{id}/robots", JsonNode.Parse("""{"blocked":false}"""));
        Assert.Equal((long)origins["total"]! - 1, (long)clear["matching"]!);
        Assert.DoesNotContain(clear["origins"]!.AsArray(), origin => (string?)origin!["origin"] == site);

        // Found by a text in the name, whatever its case; the counts are then of what the text finds.
        var found = await PostOkAsync($"api/crawls/{id}/robots", new JsonObject { ["search"] = "HTTPS://" });
        Assert.NotEmpty(found["origins"]!.AsArray());
        Assert.All(found["origins"]!.AsArray(), origin => Assert.StartsWith("https://", (string?)origin!["origin"], StringComparison.Ordinal));
        Assert.Equal(((long)found["matching"]!, 0L, (long)found["matching"]!, (long)origins["total"]!),
            ((long)found["counts"]!["all"]!, (long)found["counts"]!["blocked"]!, (long)found["counts"]!["notBlocked"]!, (long)found["total"]!));
        Assert.True((long)found["matching"]! < (long)origins["total"]!);
        Assert.Equal(0, (long)(await PostOkAsync($"api/crawls/{id}/robots", new JsonObject { ["search"] = "HTTPS://", ["blocked"] = true }))["matching"]!);
        Assert.Empty((await PostOkAsync($"api/crawls/{id}/robots", new JsonObject { ["search"] = "no origin has this" }))["origins"]!.AsArray());
        Assert.Contains("search", TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{id}/robots", new JsonObject { ["search"] = new string('a', 1_001) }),
            HttpStatusCode.UnprocessableEntity, "invalid-query")["errors"]!.AsObject().Select(error => error.Key));
        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{id}/robots", JsonNode.Parse("""{"blocked":"yes"}""")), HttpStatusCode.BadRequest, "bad-request");
        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{id}/robots", JsonNode.Parse("""{"search":7}""")), HttpStatusCode.BadRequest, "bad-request");
        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{id}/robots", JsonNode.Parse("""{"filter":"blocked"}""")), HttpStatusCode.BadRequest, "bad-request");
'''),
(P,
'''        Assert.Equal(["origin", "isInternal", "state", "statusCode", "fetchedAt", "error", "bytes", "blocked", "userAgent", "lines", "unmatched"], file.AsObject().Select(property => property.Key));
''',
'''        Assert.Equal(["origin", "isInternal", "state", "statusCode", "fetchedAt", "error", "bytes", "blocked", "userAgent", "lines", "unmatched", "respected"],
            file.AsObject().Select(property => property.Key));

        // The file says whether the crawl obeyed robots.txt, as the list does, and a crawl told to ignore it says so.
        Assert.Equal(((bool)origins["respected"]!, true), ((bool)file["respected"]!, (bool)file["respected"]!));
        var ignoring = grid.Copy(copy => Change(copy, "UPDATE crawl SET options_json = json_set(options_json, '$.respectRobotsTxt', json('false'))"));
        Assert.False((bool)(await PostOkAsync($"api/crawls/{ignoring}/robots/file", new JsonObject { ["origin"] = site }))["respected"]!);
        Assert.False((bool)(await PostOkAsync($"api/crawls/{ignoring}/robots"))["respected"]!);
'''),
(P,
'''        // The URL the inspector shows for it says the same: refused, with no response to show.
        var inspected = await PostOkAsync($"api/crawls/{id}/urls/inspect", new JsonObject { ["urlId"] = (int)row["id"]! });
        Assert.Equal(("robotsDisallowed", true), ((string?)inspected["response"]!["outcome"], (bool)inspected["response"]!["fetched"]!));
        Assert.Null(inspected["response"]!["statusCode"]);
''',
'''        // The URL the inspector shows for it says the same: refused, with no response to show.
        var inspected = await PostOkAsync($"api/crawls/{id}/urls/inspect", new JsonObject { ["urlId"] = (int)row["id"]! });
        Assert.Equal(("robotsDisallowed", true), ((string?)inspected["response"]!["outcome"], (bool)inspected["response"]!["fetched"]!));
        Assert.Null(inspected["response"]!["statusCode"]);

        // And names the origin and the line that refused it, as the row does, so its page opens that file at that line;
        // and the sitemap that lists it, by the id the Sitemaps screen opens.
        var refused = Assert.Single(inspected["indexability"]!["tests"]!.AsArray(), test => (string?)test!["test"] == "robotsTxt")!;
        Assert.Equal("""{"test":"robotsTxt","outcome":"fail","unreachable":false,"origin":"SITE","reason":"rule","line":2,"rule":"Disallow: /private/"}""".Replace("SITE", site, StringComparison.Ordinal),
            refused.ToJsonString());
        var sitemaps = (await Sidecar.GetJsonAsync($"api/crawls/{id}/sitemaps"))["sitemaps"]!.AsArray();
        var lists = Assert.Single(sitemaps, sitemap => ((string)sitemap!["url"]!).EndsWith("/sitemaps/extra.xml", StringComparison.Ordinal))!;
        Assert.Equal($$"""{"total":1,"sitemaps":[{"id":{{(int)lists["id"]!}},"url":"{{(string)lists["url"]!}}"}]}""", inspected["listedIn"]!.ToJsonString());
        Assert.Equal("listedIn", inspected.AsObject().Last().Key);

        // The list answers for that URL alone: its row, or none for a URL the origin didn't block.
        var one = await PostOkAsync($"api/crawls/{id}/robots/blocked", new JsonObject { ["origin"] = site, ["urlId"] = (int)row["id"]! });
        Assert.Equal((row.ToJsonString(), 1L, 1L), (Assert.Single(one["rows"]!.AsArray())!.ToJsonString(), (long)one["total"]!, (long)one["matching"]!));
        var home = (int)(await PostOkAsync($"api/crawls/{id}/urls/lookup", new JsonObject { ["url"] = "/" }))["id"]!;
        Assert.Equal((1L, 0L), await MatchingAsync(new JsonObject { ["origin"] = site, ["urlId"] = home }));
        Assert.Equal((1L, 0L), await MatchingAsync(new JsonObject { ["origin"] = site, ["urlId"] = 0 }));
        Assert.Equal((1L, 0L), await MatchingAsync(new JsonObject { ["origin"] = site, ["urlId"] = 99_999_999_999 }));
        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{id}/robots/blocked", JsonNode.Parse($$"""{"origin":"{{site}}","urlId":"one"}""")), HttpStatusCode.BadRequest, "bad-request");

        // A page the crawl fetched names its origin, whose robots.txt allowed it, and nothing else; a page no sitemap lists, none.
        var allowed = Assert.Single((await PostOkAsync($"api/crawls/{id}/urls/inspect", new JsonObject { ["urlId"] = home }))["indexability"]!["tests"]!.AsArray(),
            test => (string?)test!["test"] == "robotsTxt")!;
        Assert.Equal("""{"test":"robotsTxt","outcome":"pass","origin":"SITE"}""".Replace("SITE", site, StringComparison.Ordinal), allowed.ToJsonString());
        var unlisted = (int)(await PostOkAsync($"api/crawls/{id}/urls", JsonNode.Parse("""{"filters":[{"column":"type","op":"in","values":["stylesheet"]}],"limit":1}""")))["rows"]![0]!["id"]!;
        Assert.Equal("""{"total":0,"sitemaps":[]}""", (await PostOkAsync($"api/crawls/{id}/urls/inspect", new JsonObject { ["urlId"] = unlisted }))["listedIn"]!.ToJsonString());
'''),
(P,
'''    private async Task<JsonNode> PostOkAsync(string path, JsonNode? body = null)
    {
        var (status, answer) = await Sidecar.PostAsync(path, body);
        Assert.True(status == HttpStatusCode.OK, $"POST {path}: {(int)status} {answer?.ToJsonString()}");
        return answer!;
    }
''',
'''    private async Task<JsonNode> PostOkAsync(string path, JsonNode? body = null)
    {
        var (status, answer) = await Sidecar.PostAsync(path, body);
        Assert.True(status == HttpStatusCode.OK, $"POST {path}: {(int)status} {answer?.ToJsonString()}");
        return answer!;
    }

    private static void Change(string path, string sql)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
'''),
]
