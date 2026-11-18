using System.Text.Json;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Tests.Support;

namespace ShoutingIguana.Engine.Tests.Configuration;

public sealed class CrawlOptionsTests
{
    [Theory]
    [InlineData("X Api Key")]
    [InlineData("X-Key:")]
    [InlineData("Bad(Name)")]
    [InlineData("Ünicode")]
    [InlineData("")]
    public void RejectsHeaderNamesThatAreNotTokens(string name) =>
        Assert.Equal([$"Header '{name}' is not valid."], WithHeader(name, "value").Validate());

    [Theory]
    [InlineData("Content-Type")]
    [InlineData("content-length")]
    [InlineData("Allow")]
    [InlineData("Expires")]
    [InlineData("Last-Modified")]
    [InlineData("Host")]
    [InlineData("Transfer-Encoding")]
    [InlineData("Connection")]
    public void RejectsHeadersTheHttpClientControls(string name) =>
        Assert.Equal([$"Header '{name}' is set by the crawler's HTTP client and can't be overridden."], WithHeader(name, "value").Validate());

    [Theory]
    [InlineData("line\nbreak")]
    [InlineData("nul\0byte")]
    [InlineData("naïve")]
    public void RejectsValuesARequestCantCarry(string value) =>
        Assert.Equal(["Header 'X-Token' is not valid."], WithHeader("X-Token", value).Validate());

    [Fact]
    public void RejectsAMissingValueFromDeserializedOptions() =>
        Assert.Equal(["Header 'X-Token' is not valid."], WithHeader("X-Token", null!).Validate());

    /// <summary>
    /// A user name or password in the start URL or a sitemap is refused: the crawler never sends it, and the URL is
    /// stored, listed and logged as it is, so it would leak. The message never repeats the password back.
    /// </summary>
    [Fact]
    public void RejectsCredentialsInTheStartUrlOrSitemaps()
    {
        var start = new CrawlOptions { StartUrl = "https://user:pa55word@example.com/" }.ValidateFields();
        Assert.Single(start["startUrl"]);
        Assert.Contains("user name or password", start["startUrl"][0]);
        Assert.Contains("Authorization header", start["startUrl"][0]);
        Assert.DoesNotContain("pa55word", start["startUrl"][0]);

        var sitemap = new CrawlOptions
        {
            StartUrl = "https://example.com/",
            ExtraSitemaps = ["https://user:pa55word@example.com/sitemap.xml"]
        }.ValidateFields();
        Assert.Contains("user name or password", Assert.Single(sitemap["extraSitemaps"]));
        Assert.DoesNotContain("pa55word", sitemap["extraSitemaps"][0]);

        // A proxy may carry credentials, so it is left alone.
        Assert.Empty(new CrawlOptions { StartUrl = "https://example.com/", ProxyUrl = "http://user:pw@proxy:8080/" }.Validate());
    }

    [Theory]
    [InlineData("X-Api-Key", "s3cret")]
    [InlineData("Accept-Language", "de-DE,de;q=0.9")]
    [InlineData("Authorization", "Bearer abc.def")]
    [InlineData("Cookie", "session=1; theme=dark")]
    [InlineData("X_Custom.Header~2", "tab\tand ~punctuation!")]
    public void AcceptsOrdinaryHeaders(string name, string value) => Assert.Empty(WithHeader(name, value).Validate());

    /// <summary>Both are sent with every request, so a line break or non-ASCII would make every one of them fail.</summary>
    [Theory]
    [InlineData("Bot/1.0\r\nX-Injected: yes")]
    [InlineData("Bøt/1.0")]
    public void RefusesAUserAgentOrLanguageARequestCantCarry(string value)
    {
        Assert.Equal(["A custom user agent must be printable ASCII on one line."],
            new CrawlOptions { StartUrl = "https://example.com/", UserAgent = UserAgentPreset.Custom, CustomUserAgent = value }.Validate());
        Assert.Equal(["Accept-Language must be printable ASCII on one line."],
            new CrawlOptions { StartUrl = "https://example.com/", AcceptLanguage = value }.Validate());

        // A custom string that isn't used (another preset is chosen) isn't sent, so it doesn't matter.
        Assert.Empty(new CrawlOptions { StartUrl = "https://example.com/", CustomUserAgent = value }.Validate());
    }

    [Fact]
    public void KeysEachProblemByThePropertyItIsAbout()
    {
        var options = new CrawlOptions
        {
            StartUrl = "ftp://example.com/",
            MaxUrls = -1,
            MaxDepth = -1,
            Concurrency = 0,
            ExternalConcurrency = 51,
            MaxRequestsPerSecond = -1,
            UserAgent = UserAgentPreset.Custom,
            CustomUserAgent = " ",
            AcceptLanguage = "en\nde",
            RequestTimeoutSeconds = 301,
            MaxRetries = -1,
            MaxHtmlBytes = 1024,
            ProxyUrl = "not a url",
            ExtraHeaders = new Dictionary<string, string> { ["Bad Name"] = "value", ["Host"] = "example.com" },
            ExtraSitemaps = ["https://example.com/sitemap.xml", "sitemap.xml"]
        };

        var fields = options.ValidateFields();

        Assert.Equal(new Dictionary<string, IReadOnlyList<string>>
        {
            ["startUrl"] = ["Start URL must be an absolute http or https URL."],
            ["maxUrls"] = ["Max URLs cannot be negative."],
            ["maxDepth"] = ["Max depth cannot be negative."],
            ["concurrency"] = ["Concurrency must be between 1 and 100."],
            ["externalConcurrency"] = ["External concurrency must be between 1 and 50."],
            ["maxRequestsPerSecond"] = ["Max requests per second cannot be negative."],
            ["customUserAgent"] = ["A custom user agent needs a value."],
            ["acceptLanguage"] = ["Accept-Language must be printable ASCII on one line."],
            ["requestTimeoutSeconds"] = ["Request timeout must be between 1 and 300 seconds."],
            ["maxRetries"] = ["Max retries must be between 0 and 10."],
            ["maxHtmlBytes"] = ["Max HTML size must be between 64 KB and 100 MB."],
            ["proxyUrl"] = ["Proxy must be an absolute URL."],
            ["extraHeaders"] = ["Header 'Bad Name' is not valid.", "Header 'Host' is set by the crawler's HTTP client and can't be overridden."],
            ["extraSitemaps"] = ["Sitemap 'sitemap.xml' is not an absolute http or https URL."]
        }, fields);

        // The CLI's flat list is the same messages, in the order the checks run: an explicit list, so a reordering is
        // caught here and not only in the grouped form above (both come from the same source).
        Assert.Equal(
        [
            "Start URL must be an absolute http or https URL.",
            "Max URLs cannot be negative.",
            "Max depth cannot be negative.",
            "Concurrency must be between 1 and 100.",
            "External concurrency must be between 1 and 50.",
            "Max requests per second cannot be negative.",
            "A custom user agent needs a value.",
            "Accept-Language must be printable ASCII on one line.",
            "Request timeout must be between 1 and 300 seconds.",
            "Max retries must be between 0 and 10.",
            "Max HTML size must be between 64 KB and 100 MB.",
            "Proxy must be an absolute URL.",
            "Header 'Bad Name' is not valid.",
            "Header 'Host' is set by the crawler's HTTP client and can't be overridden.",
            "Sitemap 'sitemap.xml' is not an absolute http or https URL."
        ], options.Validate());
        var properties = typeof(CrawlOptions).GetProperties().Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name)).ToList();
        Assert.All(fields.Keys, field => Assert.Contains(field, properties));
        Assert.Empty(new CrawlOptions { StartUrl = "https://example.com/" }.ValidateFields());
    }

    [Fact]
    public void AcceptsAShopifySignatureForAHost() =>
        Assert.Empty(WithSignatures(FakeSignatures.For("shop.example")).Validate());

    [Fact]
    public void RefusesASignatureForWhatIsNoHost() =>
        Assert.Equal(["A Shopify signature is for 'shop.example/products', which isn't a domain."],
            WithSignatures(FakeSignatures.For("shop.example/products")).Validate());

    [Fact]
    public void RefusesTwoSignaturesForOneHost()
    {
        var options = WithSignatures(new Dictionary<string, CrawlSignature> { ["shop.example"] = FakeSignatures.Valid(), ["SHOP.example."] = FakeSignatures.Valid() });

        Assert.Equal(["There are two Shopify signatures for shop.example: keep one."], options.Validate());
    }

    [Fact]
    public void RefusesAMalformedSignatureByItsFieldWithoutRepeatingIt()
    {
        var options = WithSignatures(new Dictionary<string, CrawlSignature>
        {
            ["shop.example"] = FakeSignatures.Valid() with { Signature = $"sig1={FakeSignatures.MARKER}" }
        });

        var problem = Assert.Single(Assert.Single(options.ValidateFields(), field => field.Key == "signatures").Value);
        Assert.StartsWith("The Shopify signature for shop.example: Signature isn't in the form Shopify gives", problem);
        Assert.DoesNotContain(FakeSignatures.MARKER, problem);
    }

    [Fact]
    public void RefusesAnExpiredSignatureAndSaysWhenItEnded()
    {
        var options = WithSignatures(FakeSignatures.For("shop.example", expires: FakeSignatures.CREATED + 60));

        Assert.Equal(
            ["The Shopify signature for shop.example expired on 1 January 2026. Create a new one in Shopify admin: Online Store › Preferences › Crawler access."],
            options.Validate());
    }

    [Fact]
    public void ACrawlFileKeepsNoSignature()
    {
        var options = WithSignatures(FakeSignatures.For("shop.example"));

        var stored = options.WithoutSecrets();
        var json = JsonSerializer.Serialize(stored, EngineJsonContext.Default.CrawlOptions);

        Assert.Empty(stored.Signatures);
        Assert.DoesNotContain(FakeSignatures.MARKER, json);
        Assert.DoesNotContain("shop.example", json.Replace("https://example.com/", string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void AResumeSendsTheSignaturesGivenAgainOnly()
    {
        var stored = WithSignatures(FakeSignatures.For("shop.example")).WithoutSecrets();

        var given = stored.PlanResume(new ResumeChanges { Signatures = FakeSignatures.For("shop.example") });
        var notGiven = stored.PlanResume(new ResumeChanges());

        Assert.Equal(FakeSignatures.SIGNATURE, given.Options.Signatures["shop.example"].Signature);
        Assert.Empty(notGiven.Options.Signatures);
    }

    private static CrawlOptions WithSignatures(IReadOnlyDictionary<string, CrawlSignature> signatures) => new()
    {
        StartUrl = "https://example.com/",
        Signatures = signatures
    };

    private static CrawlOptions WithHeader(string name, string value) => new()
    {
        StartUrl = "https://example.com/",
        ExtraHeaders = new Dictionary<string, string> { [name] = value }
    };
}
