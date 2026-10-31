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

    private static CrawlOptions WithHeader(string name, string value) => new()
    {
        StartUrl = "https://example.com/",
        ExtraHeaders = new Dictionary<string, string> { [name] = value }
    };
}
