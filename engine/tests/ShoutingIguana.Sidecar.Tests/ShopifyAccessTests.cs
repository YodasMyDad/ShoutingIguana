using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Sidecar.Library;
using ShoutingIguana.Sidecar.Tests.Support;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>
/// A Shopify store's crawler access through the API: the signatures a crawl, a Continue and a recheck take (validated,
/// never given back or kept), and <c>GET /shopify</c>, what the results say of the pages the store refused.
/// </summary>
public sealed class ShopifyAccessTests(RecheckSite site) : IClassFixture<RecheckSite>, IAsyncLifetime
{
    private TestSidecar _sidecar = null!;

    public async Task InitializeAsync() => _sidecar = await TestSidecar.StartAsync();

    public async Task DisposeAsync() => await _sidecar.DisposeAsync();

    [Fact]
    public async Task ValidationRefusesAnExpiredOrMalformedSignatureByItsFieldWithoutRepeatingIt()
    {
        var expired = await _sidecar.PostAsync("api/crawl-options/validate", Options("https://shop.example/", "shop.example", SyntheticShopify.EXPIRED_SIGNATURE_INPUT));
        var malformed = await _sidecar.PostAsync("api/crawl-options/validate", Options("https://shop.example/", "shop.example", SyntheticShopify.KEY_ID));
        var valid = await _sidecar.PostAsync("api/crawl-options/validate", Options("https://shop.example/", "shop.example", SyntheticShopify.SIGNATURE_INPUT));

        Assert.Equal(
            ["The Shopify signature for shop.example expired on 1 January 2026. Create a new one in Shopify admin: Online Store › Preferences › Crawler access."],
            expired.Body!["errors"]!["signatures"]!.AsArray().Select(message => (string?)message));
        var problem = (string?)malformed.Body!["errors"]!["signatures"]![0];
        Assert.StartsWith("The Shopify signature for shop.example: Signature-Input isn't in the form Shopify gives", problem);
        Assert.DoesNotContain(SyntheticShopify.KEY_ID, problem);
        Assert.Equal("""{"errors":{}}""", valid.Body!.ToJsonString());
    }

    [Fact]
    public async Task ACrawlsSignatureIsNeverGivenBackSavedOrKept()
    {
        var started = await _sidecar.PostAsync("api/crawls", new JsonObject
        {
            ["options"] = Options(site.Server.BaseUri.AbsoluteUri, site.Server.BaseUri.Authority, SyntheticShopify.SIGNATURE_INPUT, maxUrls: 10),
            ["analyzeWhenDone"] = false
        });
        Assert.Equal(HttpStatusCode.Created, started.Status);
        var id = (string)started.Body!["id"]!;
        await using (var events = await EventStreamReader.ConnectAsync(_sidecar.Client))
        {
            await events.CrawlStatusAsync(id, "completed");
        }

        var saved = await _sidecar.SendAsync(HttpMethod.Put, "api/crawl-options/defaults",
            Options("https://shop.example/", "shop.example", SyntheticShopify.SIGNATURE_INPUT));
        Assert.Equal(HttpStatusCode.OK, saved.Status);

        var options = await _sidecar.GetJsonAsync($"api/crawls/{id}/options");
        Assert.DoesNotContain(SyntheticShopify.KEY_ID, options.ToJsonString());
        Assert.DoesNotContain(SyntheticShopify.KEY_ID, saved.Body!.ToJsonString());
        foreach (var file in Directory.EnumerateFiles(_sidecar.Root, "*", SearchOption.AllDirectories).Where(file => !file.EndsWith(".lock", StringComparison.Ordinal)))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, System.Text.Encoding.Latin1);
            Assert.DoesNotContain(SyntheticShopify.KEY_ID, await reader.ReadToEndAsync());
        }
    }

    [Fact]
    public async Task ARecheckTakesASignatureAndRefusesAnExpiredOne()
    {
        var path = FixLoopCrawls.AddTo(_sidecar, site.Then, "signed");
        var id = CrawlIds.For(path);
        var home = UrlGridReader.Lookup(path, "/")!.Id;
        var ask = new JsonObject
        {
            ["urlIds"] = new JsonArray(home),
            ["signatures"] = Signatures(site.Server.BaseUri.Authority, SyntheticShopify.EXPIRED_SIGNATURE_INPUT)
        };

        var refused = TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/{id}/rechecks", ask), HttpStatusCode.UnprocessableEntity, "invalid-options");
        Assert.Equal(["signatures"], refused["errors"]!.AsObject().Select(field => field.Key));

        await using var events = await EventStreamReader.ConnectAsync(_sidecar.Client);
        var given = new JsonObject
        {
            ["urlIds"] = new JsonArray(home),
            ["signatures"] = Signatures(site.Server.BaseUri.Authority, SyntheticShopify.SIGNATURE_INPUT)
        };
        var started = await _sidecar.PostAsync($"api/crawls/{id}/rechecks", given);
        Assert.True(started.Status == HttpStatusCode.Accepted, started.Body?.ToJsonString());
        await events.CrawlStatusAsync(id, "completed");
    }

    [Fact]
    public async Task TheShopifyReadCountsWhatTheStoreRefusedAndIsNullForAnyOtherSite()
    {
        var ordinary = CrawlIds.For(FixLoopCrawls.AddTo(_sidecar, site.Then, "ordinary"));
        var store = FixLoopCrawls.AddTo(_sidecar, site.Then, "store", change: path => DressAsAStore(path, refusedUnsigned: 3, refusedSigned: 1));

        using var none = await _sidecar.Client.GetAsync($"api/crawls/{ordinary}/shopify");
        var access = await _sidecar.GetJsonAsync($"api/crawls/{CrawlIds.For(store)}/shopify");

        Assert.Equal(HttpStatusCode.OK, none.StatusCode);
        Assert.Equal("null|application/json", $"{await none.Content.ReadAsStringAsync()}|{none.Content.Headers.ContentType?.MediaType}");
        Assert.Equal(site.Server.BaseUri.Authority, (string?)access["host"]);
        Assert.Equal("powered-by: Shopify", (string?)access["signal"]);
        Assert.Equal(3, (int?)access["notCheckedUnsigned"]);
        Assert.Equal(1, (int?)access["notCheckedSigned"]);
        Assert.Equal(4, access["urlIds"]!.AsArray().Count);
    }

    private static JsonObject Options(string startUrl, string host, string signatureInput, int? maxUrls = null)
    {
        var options = new JsonObject { ["startUrl"] = startUrl, ["signatures"] = Signatures(host, signatureInput) };
        if (maxUrls is { } limit)
        {
            options["maxUrls"] = limit;
        }

        return options;
    }

    private static JsonObject Signatures(string host, string signatureInput) => new()
    {
        [host] = new JsonObject { ["signatureInput"] = signatureInput, ["signature"] = SyntheticShopify.SIGNATURE }
    };

    /// <summary>
    /// The crawl as if of a Shopify store: the fact kept, and some product pages refused, without a signature and with one,
    /// in the words the writer stores.
    /// </summary>
    private void DressAsAStore(string path, int refusedUnsigned, int refusedSigned)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        var refused = $"{RetryingFetcher.RATE_LIMITED_ERROR} (429 Too Many Requests)";
        command.CommandText = $"""
            CREATE TABLE site_platform (id INTEGER PRIMARY KEY CHECK (id = 1), name TEXT NOT NULL, host TEXT NOT NULL, signal TEXT NOT NULL, url_id INTEGER);
            INSERT INTO site_platform VALUES (1, 'shopify', $host, 'powered-by: Shopify', 1);
            UPDATE responses SET error = $unsigned, outcome = 7, status_code = NULL
              WHERE url_id IN (SELECT u.id FROM urls u WHERE u.is_internal = 1 AND u.url LIKE '%/p/%' ORDER BY u.id LIMIT {refusedUnsigned});
            UPDATE responses SET error = $signed, outcome = 7, status_code = NULL
              WHERE url_id IN (SELECT u.id FROM urls u WHERE u.is_internal = 1 AND u.url LIKE '%/p/%' ORDER BY u.id LIMIT {refusedSigned} OFFSET {refusedUnsigned});
            """;
        command.Parameters.AddWithValue("$host", site.Server.BaseUri.Authority);
        command.Parameters.AddWithValue("$unsigned", $"{refused}: {ShopifySignals.UNSIGNED_REFUSAL}");
        command.Parameters.AddWithValue("$signed", $"{refused}: {ShopifySignals.SIGNED_REFUSAL}");
        command.ExecuteNonQuery();
    }
}
