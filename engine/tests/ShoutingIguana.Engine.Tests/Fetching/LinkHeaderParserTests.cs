using ShoutingIguana.Engine.Fetching;

namespace ShoutingIguana.Engine.Tests.Fetching;

public sealed class LinkHeaderParserTests
{
    [Fact]
    public void ReadsCanonical()
    {
        var entry = Assert.Single(LinkHeaderParser.Parse(["<https://example.com/>; rel=\"canonical\""]));

        Assert.Equal("https://example.com/", entry.Url);
        Assert.Equal("canonical", entry.Rel);
    }

    [Fact]
    public void ReadsSeveralEntriesInOneHeader()
    {
        var entries = LinkHeaderParser.Parse([
            "<https://example.com/de>; rel=\"alternate\"; hreflang=\"de\", <https://example.com/fr>; rel=alternate; hreflang=fr"
        ]);

        Assert.Equal(["de", "fr"], entries.Select(entry => entry.Hreflang));
    }

    [Fact]
    public void CommasInsideUrlsDoNotSplitEntries() =>
        Assert.Equal("https://example.com/a,b", Assert.Single(LinkHeaderParser.Parse(["<https://example.com/a,b>; rel=\"canonical\""])).Url);
}
