using ShoutingIguana.Engine.Configuration;
using ShoutingIguana.Engine.Configuration.Models;

namespace ShoutingIguana.Engine.Tests.Configuration;

public sealed class ListUrlFileTests
{
    [Fact]
    public void ATextFileIsOneAddressALineInThatOrder()
    {
        const string text = """
            https://example.com/b

            https://example.com/a
            https://example.com/a
            """;

        Assert.True(ListUrlFile.TryParse(text, out var urls, out var error), error);
        Assert.Equal(["https://example.com/b", "https://example.com/a"], urls);
    }

    [Fact]
    public void ACsvCrawlsTheHigherPriorityFirstAndKeepsOneOfEachAddress()
    {
        const string text = """
            Priority,Url
            1,https://example.com/low
            9,"https://example.com/high"
            5,https://Example.com/low
            """;

        Assert.True(ListUrlFile.TryParse(text, out var urls, out var error), error);
        Assert.Equal(["https://example.com/high", "https://Example.com/low"], urls);
    }

    [Fact]
    public void APriorityThatIsNotANumberIsRefused()
    {
        const string text = """
            Url,Priority
            https://example.com/a,soon
            """;

        Assert.False(ListUrlFile.TryParse(text, out _, out var error));
        Assert.Equal("Priority 'soon' in the list isn't a whole number.", error);
    }

    [Fact]
    public void AnEmptyFileIsRefused()
    {
        Assert.False(ListUrlFile.TryParse("Url,Priority\n", out _, out var error));
        Assert.Equal("The list has no URLs.", error);
    }

    [Fact]
    public void AListAddressWithAPasswordIsRefusedWithoutRepeatingIt()
    {
        var options = new CrawlOptions
        {
            StartUrl = "https://example.com/",
            ListUrls = ["https://user:secret@example.com/private", "notaurl"]
        };

        var messages = options.Validate();
        Assert.Contains(messages, message => message.Contains("user name or password", StringComparison.Ordinal));
        Assert.Contains(messages, message => message.Contains("notaurl", StringComparison.Ordinal));
        Assert.DoesNotContain(messages, message => message.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public void NoListFollowsLinks()
    {
        var options = new CrawlOptions { StartUrl = "https://example.com/", ListUrls = ["  ", ""] };
        Assert.False(options.IsListMode);
        Assert.Empty(options.Validate());
    }
}
