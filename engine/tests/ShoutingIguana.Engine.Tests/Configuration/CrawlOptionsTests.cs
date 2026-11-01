using System.Text.Json;
using ShoutingIguana.Engine.Configuration.Models;

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

        // The CLI's list is the same messages in the same order, and every key is a property as JSON spells it.
        Assert.Equal(fields.SelectMany(field => field.Value), options.Validate());
        var properties = typeof(CrawlOptions).GetProperties().Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name)).ToList();
        Assert.All(fields.Keys, field => Assert.Contains(field, properties));
        Assert.Empty(new CrawlOptions { StartUrl = "https://example.com/" }.ValidateFields());
    }

    private static CrawlOptions WithHeader(string name, string value) => new()
    {
        StartUrl = "https://example.com/",
        ExtraHeaders = new Dictionary<string, string> { [name] = value }
    };
}
