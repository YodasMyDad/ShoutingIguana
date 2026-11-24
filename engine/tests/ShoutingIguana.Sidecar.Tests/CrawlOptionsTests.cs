using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Sidecar.Api.Models;
using ShoutingIguana.Sidecar.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

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

        // Signatures as the engine's default, none, the app's copy of the defaults keeps: an empty object, never values.
        Assert.Equal("{}", options["signatures"]?.ToJsonString());
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

    /// <summary>
    /// The app sends the rendering mode and a rule's selector type by name (<c>javaScript</c>, <c>xPath</c>), as its
    /// types say, and reads them back by name. They were numbers, so choosing a mode in the app was refused as
    /// <c>invalid-options</c>. A number is still read: defaults a 2.0.3 sidecar saved hold one.
    /// </summary>
    [Fact]
    public async Task TheRenderingModeAndASelectorTypeAreNamesAndANumberIsStillRead()
    {
        var engine = (await _sidecar.GetJsonAsync("api/crawl-options/defaults"))["options"]!;
        Assert.Equal("\"automatic\"", engine["javaScriptRendering"]!.ToJsonString());

        var saved = await _sidecar.SendAsync(HttpMethod.Put, "api/crawl-options/defaults", JsonNode.Parse("""
            {"javaScriptRendering":"javaScript",
             "extractionRules":[{"name":"Price","selectorType":"xPath","selector":"//span[@class='price']","enabled":true}]}
            """));
        Assert.Equal(HttpStatusCode.OK, saved.Status);
        Assert.Equal("\"javaScript\"", saved.Body!["options"]!["javaScriptRendering"]!.ToJsonString());
        Assert.Equal("\"xPath\"", saved.Body!["options"]!["extractionRules"]![0]!["selectorType"]!.ToJsonString());

        var numbered = await _sidecar.SendAsync(HttpMethod.Put, "api/crawl-options/defaults", JsonNode.Parse("""
            {"javaScriptRendering":2,"renderPatterns":["/app/"],
             "extractionRules":[{"name":"Price","selectorType":2,"selector":"price: (\\d+)","enabled":true}]}
            """));
        Assert.Equal(HttpStatusCode.OK, numbered.Status);
        Assert.Equal("\"matching\"", numbered.Body!["options"]!["javaScriptRendering"]!.ToJsonString());
        Assert.Equal("\"regex\"", numbered.Body!["options"]!["extractionRules"]![0]!["selectorType"]!.ToJsonString());

        foreach (var unknown in new[] { "\"sometimes\"", "9" })
        {
            var refused = TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Put, "api/crawl-options/defaults",
                JsonNode.Parse($$"""{"javaScriptRendering":{{unknown}}}""")), HttpStatusCode.UnprocessableEntity, "invalid-options");
            Assert.Equal(["javaScriptRendering"], refused["errors"]!.AsObject().Select(field => field.Key));
        }
    }

    /// <summary>
    /// An address typed without a scheme starts where the site answers: the test site serves only http, so http, where
    /// a guess of https read nothing and blamed robots.txt (the closing audit's gap 1). One with its scheme is kept.
    /// </summary>
    [Fact]
    public async Task AnAddressWithoutASchemeStartsWhereTheSiteAnswers()
    {
        await using var site = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 5 });
        var address = site.BaseUri.Authority;

        var bare = await _sidecar.PostAsync("api/crawl-options/start-url", new JsonObject { ["address"] = address });
        Assert.Equal(HttpStatusCode.OK, bare.Status);
        Assert.Equal($$"""{"startUrl":"http://{{address}}","reason":"http"}""", bare.Body!.ToJsonString());

        var typed = await _sidecar.PostAsync("api/crawl-options/start-url",
            new JsonObject { ["address"] = "https://example.invalid/", ["options"] = new JsonObject { ["concurrency"] = 2 } });
        Assert.Equal("""{"startUrl":"https://example.invalid/","reason":"typed"}""", typed.Body!.ToJsonString());

        TestSidecar.AssertProblem(await _sidecar.PostAsync("api/crawl-options/start-url", new JsonObject()), HttpStatusCode.BadRequest, "bad-request");
    }

    /// <summary>
    /// Round 5's review (R2-7): options a crawl couldn't start with reached the fetcher, which threw (a 500 logged as an
    /// engine error). They are refused as the crawl would refuse them, and nothing is asked. The start URL in them is
    /// no reason to refuse: the address is what's asked about.
    /// </summary>
    [Theory]
    [InlineData("not a proxy")]
    [InlineData("ftp://proxy.example:21")]
    public async Task StartUrlRefusesOptionsACrawlCouldntStartWith(string proxyUrl)
    {
        var refused = TestSidecar.AssertProblem(await _sidecar.PostAsync("api/crawl-options/start-url",
                new JsonObject { ["address"] = "example.invalid", ["options"] = new JsonObject { ["startUrl"] = "", ["proxyUrl"] = proxyUrl } }),
            HttpStatusCode.UnprocessableEntity, "invalid-options");
        Assert.Equal(["proxyUrl"], refused["errors"]!.AsObject().Select(field => field.Key));

        // The crawl itself says the same of that proxy.
        var validation = await _sidecar.PostAsync("api/crawl-options/validate",
            new JsonObject { ["startUrl"] = "https://example.com/", ["proxyUrl"] = proxyUrl });
        Assert.Equal(["proxyUrl"], validation.Body!["errors"]!.AsObject().Select(field => field.Key));
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

    /// <summary>
    /// A property the body's own shape doesn't have is a bad request, even when its name is an option's: a top-level
    /// option on the start wrapper, or an option the continue body doesn't take. A wrong-typed value of a real option
    /// stays a field error. The old reader keyed any such name as an option, giving `invalid-options` by mistake.
    /// </summary>
    [Fact]
    public async Task APropertyTheBodyDoesntHaveIsABadRequestEvenWhenItsNameIsAnOption()
    {
        // `concurrency` isn't a field of the start wrapper (it belongs under `options`), so it is unknown here.
        TestSidecar.AssertProblem(await _sidecar.PostAsync("api/crawls", JsonNode.Parse("""{"concurrency":4}""")),
            HttpStatusCode.BadRequest, "bad-request");

        // `startUrl` isn't a field of the continue body, so it is unknown there, not a start-URL field error.
        TestSidecar.AssertProblem(await _sidecar.PostAsync("api/crawls/0123456789abcdef/continue", JsonNode.Parse("""{"startUrl":"https://x/"}""")),
            HttpStatusCode.BadRequest, "bad-request");

        // A real nested option with the wrong type is still a field error under invalid-options.
        var mistyped = TestSidecar.AssertProblem(
            await _sidecar.PostAsync("api/crawls", JsonNode.Parse("""{"options":{"startUrl":"https://x/","concurrency":"many"}}""")),
            HttpStatusCode.UnprocessableEntity, "invalid-options");
        Assert.Equal(["concurrency"], mistyped["errors"]!.AsObject().Select(field => field.Key));
    }

    /// <summary>
    /// Defaults saved before an option existed lack it, and it takes the engine's default, never 0 or false: a new option
    /// with any other default must not turn up switched off in every crawl the saved defaults start.
    /// </summary>
    [Fact]
    public async Task SavedDefaultsWithoutAnOptionTakeTheEnginesDefaultForIt()
    {
        var engine = (await _sidecar.GetJsonAsync("api/crawl-options/defaults"))["options"]!.AsObject();
        var saved = await _sidecar.SendAsync(HttpMethod.Put, "api/crawl-options/defaults", JsonNode.Parse("""
            {"maxUrls":7,"maxDepth":3,"concurrency":2,"externalConcurrency":1,"maxRequestsPerSecond":1.5,"userAgent":"custom",
             "customUserAgent":"Tester/1.0","acceptLanguage":"fr-FR","extraHeaders":{"X-Test":"1"},"respectRobotsTxt":false,
             "respectCrawlDelay":true,"checkResources":false,"checkExternalLinks":false,"includeSubdomains":true,"stayInStartFolder":false,
             "followNofollow":true,"crawlCanonicals":false,"crawlHreflang":true,"crawlPagination":true,"discoverSitemaps":false,
             "extraSitemaps":["https://example.com/extra.xml"],"storeHtml":true,"useCookies":false,"proxyUrl":"http://proxy.example:8080/",
             "requestTimeoutSeconds":9,"maxRetries":1,"maxHtmlBytes":1000000}
            """));
        Assert.Equal(HttpStatusCode.OK, saved.Status);

        var file = Path.Combine(_sidecar.Options.StateDirectory, "defaults.json");
        var stored = JsonNode.Parse(await File.ReadAllTextAsync(file))!.AsObject();
        Assert.Contains("concurrency", stored.Select(property => property.Key));
        foreach (var name in stored.Select(property => property.Key).ToList())
        {
            var without = stored.DeepClone().AsObject();
            without.Remove(name);
            await File.WriteAllTextAsync(file, without.ToJsonString());

            var read = (await _sidecar.GetJsonAsync("api/crawl-options/defaults"))["options"]!.AsObject();
            Assert.True(engine[name]?.ToJsonString() == read[name]?.ToJsonString(), $"{name}: {read[name]?.ToJsonString()}, not the engine's {engine[name]?.ToJsonString()}");
            Assert.All(without.Where(property => property.Key is not "hasRedactedProxyCredentials" and not "hasRedactedAuth"),
                property => Assert.Equal(property.Value?.ToJsonString(), read[property.Key]?.ToJsonString()));
        }
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
            Signatures = new Dictionary<string, CrawlSignature>
            {
                ["example.com"] = new() { SignatureInput = SyntheticShopify.SIGNATURE_INPUT, Signature = SyntheticShopify.SIGNATURE }
            },
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
            MaxHtmlBytes = 1024 * 1024,
            ExtractionRules = [new ExtractionRule { Name = "Price", SelectorType = ExtractionSelector.Css, Selector = ".price", Enabled = false }],
            ListUrls = ["https://example.com/listed"]
        };

        var json = JsonSerializer.Serialize(CrawlOptionsDto.From(options), Serialization.SidecarJsonContext.Default.CrawlOptionsDto);
        var roundTripped = JsonSerializer.Deserialize(json, Serialization.SidecarJsonContext.Default.CrawlOptionsDto)!.ToCrawlOptions(out var errors);

        Assert.Empty(errors);
        Assert.Equal(options with
        {
            ExtraHeaders = roundTripped.ExtraHeaders,
            ExtraSitemaps = roundTripped.ExtraSitemaps,
            RenderPatterns = roundTripped.RenderPatterns,
            IncludePatterns = roundTripped.IncludePatterns,
            ExcludePatterns = roundTripped.ExcludePatterns,
            ExtractionRules = roundTripped.ExtractionRules,
            ListUrls = roundTripped.ListUrls,
            HttpLogins = roundTripped.HttpLogins,
            Cookies = roundTripped.Cookies,
            Signatures = roundTripped.Signatures
        }, roundTripped);
        Assert.Equal(["https://example.com/listed"], roundTripped.ListUrls);
        var rule = Assert.Single(roundTripped.ExtractionRules);
        Assert.Equal("Price", rule.Name);
        Assert.Equal(ExtractionSelector.Css, rule.SelectorType);
        Assert.Equal(".price", rule.Selector);
        Assert.False(rule.Enabled);
        Assert.Equal(options.ExtraHeaders, roundTripped.ExtraHeaders);
        Assert.Equal(options.ExtraSitemaps, roundTripped.ExtraSitemaps);

        // A Shopify signature is taken in requests and never given back in a response.
        Assert.DoesNotContain(SyntheticShopify.KEY_ID, json);
        Assert.Empty(roundTripped.Signatures);
    }
}
