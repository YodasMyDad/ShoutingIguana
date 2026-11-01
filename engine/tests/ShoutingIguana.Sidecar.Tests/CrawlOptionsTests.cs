using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Sidecar.Api.Models;
using ShoutingIguana.Sidecar.Tests.Support;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>The user's defaults, which never keep a secret, and validation keyed by the field each message is about.</summary>
public sealed class CrawlOptionsTests : IAsyncLifetime
{
    private TestSidecar _sidecar = null!;

    public async Task InitializeAsync() => _sidecar = await TestSidecar.StartAsync();

    public async Task DisposeAsync() => await _sidecar.DisposeAsync();

    [Fact]
    public async Task DefaultsAreTheEnginesUntilTheUserSavesTheirOwn()
    {
        var defaults = await _sidecar.GetJsonAsync("api/crawl-options/defaults");

        var options = defaults["options"]!;
        Assert.Null(options["startUrl"]);
        Assert.Equal(5, (int?)options["concurrency"]);
        Assert.Equal("shoutingIguana", (string?)options["userAgent"]);
        Assert.Equal(20, (int?)options["requestTimeoutSeconds"]);
        Assert.Equal(
            """[{"id":"polite","concurrency":2,"maxRequestsPerSecond":2},{"id":"standard","concurrency":5,"maxRequestsPerSecond":0},{"id":"fast","concurrency":16,"maxRequestsPerSecond":0}]""",
            defaults["speedPresets"]!.ToJsonString());
    }

    [Fact]
    public async Task SavedDefaultsNeverKeepTheStartUrlHeaderValuesOrProxyPassword()
    {
        var saved = await _sidecar.SendAsync(HttpMethod.Put, "api/crawl-options/defaults", JsonNode.Parse("""
            {"startUrl":"https://private.example/","concurrency":3,"maxRequestsPerSecond":1.5,"userAgent":"googlebotSmartphone",
             "extraHeaders":{"X-Crawler-Key":"s3cret-value"},"proxyUrl":"http://crawler:pa55word@proxy.example:8080/","storeHtml":true}
            """));

        Assert.Equal(HttpStatusCode.OK, saved.Status);
        var file = await File.ReadAllTextAsync(Path.Combine(_sidecar.Options.StateDirectory, "defaults.json"));
        Assert.DoesNotContain("s3cret-value", file);
        Assert.DoesNotContain("pa55word", file);
        Assert.DoesNotContain("private.example", file);

        // What was saved comes back: the header name without its value, and the proxy without its password.
        var options = (await _sidecar.GetJsonAsync("api/crawl-options/defaults"))["options"]!;
        Assert.Equal(saved.Body!["options"]!.ToJsonString(), options.ToJsonString());
        Assert.Null(options["startUrl"]);
        Assert.Equal(3, (int?)options["concurrency"]);
        Assert.Equal(1.5, (double?)options["maxRequestsPerSecond"]);
        Assert.Equal("googlebotSmartphone", (string?)options["userAgent"]);
        Assert.True((bool?)options["storeHtml"]);
        Assert.Equal("""{"X-Crawler-Key":""}""", options["extraHeaders"]!.ToJsonString());
        Assert.Equal($"http://{CrawlOptions.REDACTED}@proxy.example:8080/", (string?)options["proxyUrl"]);
        Assert.True((bool?)options["hasRedactedProxyCredentials"]);

        // Defaults that couldn't make a crawl aren't saved, but a missing start URL is no reason to refuse them.
        var refused = TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Put, "api/crawl-options/defaults",
            JsonNode.Parse("""{"concurrency":0}""")), HttpStatusCode.UnprocessableEntity, "invalid-options");
        Assert.Equal(["concurrency"], refused["errors"]!.AsObject().Select(field => field.Key));
        Assert.Equal(3, (int?)(await _sidecar.GetJsonAsync("api/crawl-options/defaults"))["options"]!["concurrency"]);
    }

    [Fact]
    public async Task ValidationKeysEveryMessageByItsField()
    {
        var valid = await _sidecar.PostAsync("api/crawl-options/validate", JsonNode.Parse("""{"startUrl":"https://example.com/"}"""));
        Assert.Equal(HttpStatusCode.OK, valid.Status);
        Assert.Equal("""{"errors":{}}""", valid.Body!.ToJsonString());

        var invalid = await _sidecar.PostAsync("api/crawl-options/validate", JsonNode.Parse("""
            {"startUrl":"ftp://example.com/","concurrency":0,"extraHeaders":{"X-Key":"one","x-key":"two","Host":"example.com"}}
            """));
        Assert.Equal(HttpStatusCode.OK, invalid.Status);
        var errors = invalid.Body!["errors"]!.AsObject();
        Assert.Equal(["concurrency", "extraHeaders", "startUrl"], errors.Select(field => field.Key).Order(StringComparer.Ordinal));
        Assert.Equal(["Start URL must be an absolute http or https URL."], errors["startUrl"]!.AsArray().Select(message => (string?)message));
        Assert.Contains("Header 'x-key' is given more than once.", errors["extraHeaders"]!.AsArray().Select(message => (string?)message));
        Assert.Contains("Header 'Host' is set by the crawler's HTTP client and can't be overridden.",
            errors["extraHeaders"]!.AsArray().Select(message => (string?)message));

        // A value of the wrong type is a message for its field too, still with 200.
        var mistyped = await _sidecar.PostAsync("api/crawl-options/validate", JsonNode.Parse("""{"startUrl":"https://example.com/","maxUrls":"lots"}"""));
        Assert.Equal(HttpStatusCode.OK, mistyped.Status);
        Assert.Equal(["maxUrls"], mistyped.Body!["errors"]!.AsObject().Select(field => field.Key));

        // An enum by number, or a property the options don't have, isn't guessed at.
        Assert.Equal(["userAgent"], (await _sidecar.PostAsync("api/crawl-options/validate", JsonNode.Parse("""{"userAgent":3}""")))
            .Body!["errors"]!.AsObject().Select(field => field.Key));
        TestSidecar.AssertProblem(await _sidecar.PostAsync("api/crawl-options/validate", JsonNode.Parse("""{"concurency":3}""")),
            HttpStatusCode.BadRequest, "bad-request");
    }

    /// <summary>The DTO carries every public option, so a new engine option can't be silently left out of the API.</summary>
    [Fact]
    public void TheOptionsDtoCarriesEveryPublicOption()
    {
        var dtoProperties = typeof(CrawlOptionsDto).GetProperties().Select(property => property.Name).ToHashSet();
        var engineProperties = typeof(CrawlOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name).ToList();
        Assert.All(engineProperties, name => Assert.Contains(name, dtoProperties));
        Assert.DoesNotContain("TrustedCertificate", dtoProperties);

        var options = new CrawlOptions
        {
            StartUrl = "https://example.com/shop/",
            MaxUrls = 10,
            MaxDepth = 2,
            Concurrency = 3,
            ExternalConcurrency = 1,
            MaxRequestsPerSecond = 2.5,
            UserAgent = UserAgentPreset.Custom,
            CustomUserAgent = "TestBot/1.0",
            AcceptLanguage = "de-DE",
            ExtraHeaders = new Dictionary<string, string> { ["X-Key"] = "value" },
            RespectRobotsTxt = false,
            RespectCrawlDelay = true,
            CheckResources = false,
            CheckExternalLinks = false,
            IncludeSubdomains = true,
            StayInStartFolder = false,
            FollowNofollow = true,
            CrawlCanonicals = false,
            CrawlHreflang = true,
            CrawlPagination = true,
            DiscoverSitemaps = false,
            ExtraSitemaps = ["https://example.com/extra.xml"],
            StoreHtml = true,
            UseCookies = false,
            ProxyUrl = "http://proxy.example:8080/",
            RequestTimeoutSeconds = 45,
            MaxRetries = 4,
            MaxHtmlBytes = 1024 * 1024
        };

        var json = JsonSerializer.Serialize(CrawlOptionsDto.From(options), Serialization.SidecarJsonContext.Default.CrawlOptionsDto);
        var roundTripped = JsonSerializer.Deserialize(json, Serialization.SidecarJsonContext.Default.CrawlOptionsDto)!.ToCrawlOptions(out var errors);

        Assert.Empty(errors);
        Assert.Equal(options with { ExtraHeaders = roundTripped.ExtraHeaders, ExtraSitemaps = roundTripped.ExtraSitemaps }, roundTripped);
        Assert.Equal(options.ExtraHeaders, roundTripped.ExtraHeaders);
        Assert.Equal(options.ExtraSitemaps, roundTripped.ExtraSitemaps);
    }
}
