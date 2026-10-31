using ShoutingIguana.Engine.Robots;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Tests.Robots;

public sealed class RobotsTxtParserTests
{
    private const string TOKEN = "ShoutingIguana";

    [Fact]
    public void LongestMatchWinsAndWildcardsWork()
    {
        var robots = RobotsTxtParser.Parse("""
            User-agent: *
            Disallow: /private/
            Allow: /private/public
            Disallow: /*.pdf$
            Disallow: /*/secret
            """, TOKEN);

        Assert.False(robots.IsAllowed("/private/report"));
        Assert.True(robots.IsAllowed("/private/public/page"));
        Assert.False(robots.IsAllowed("/files/doc.pdf"));
        Assert.True(robots.IsAllowed("/files/doc.pdf?download=1"));
        Assert.False(robots.IsAllowed("/a/b/secret"));
        Assert.True(robots.IsAllowed("/secret"));
        Assert.True(robots.IsAllowed("/"));
    }

    [Fact]
    public void AllowWinsATie() =>
        Assert.True(RobotsTxtParser.Parse("User-agent: *\nDisallow: /page\nAllow: /page", TOKEN).IsAllowed("/page"));

    /// <summary>A rule is as specific as it is long once normalised, the form the URLs it's matched against take.</summary>
    [Theory]
    [InlineData("Disallow: /%61bc\nAllow: /abc", "/abc")]
    [InlineData("Disallow: /caf%C3%A9\nAllow: /café", "/caf%C3%A9")]
    [InlineData("Allow: /über\nDisallow: /%C3%BC*", "/%C3%BCber")]
    public void SpecificityIsMeasuredOnTheNormalisedRule(string rules, string path) =>
        Assert.True(RobotsTxtParser.Parse($"User-agent: *\n{rules}", TOKEN).IsAllowed(path));

    [Fact]
    public void OwnGroupReplacesTheWildcardGroup()
    {
        const string content = """
            User-agent: *
            Disallow: /

            User-agent: other-bot
            User-agent: shoutingiguana
            Disallow: /admin
            """;

        var ours = RobotsTxtParser.Parse(content, TOKEN);
        var someoneElse = RobotsTxtParser.Parse(content, "UnknownBot");

        Assert.True(ours.IsAllowed("/"));
        Assert.False(ours.IsAllowed("/admin/users"));
        Assert.False(someoneElse.IsAllowed("/"));
    }

    [Fact]
    public void EmptyDisallowAllowsEverything() =>
        Assert.True(RobotsTxtParser.Parse("User-agent: *\nDisallow:", TOKEN).IsAllowed("/anything"));

    [Fact]
    public void EmptyDisallowEndsTheGroupSoTheNextAgentGetsItsOwnRules()
    {
        const string content = """
            User-agent: *
            Disallow:
            User-agent: AhrefsBot
            Disallow: /
            """;

        Assert.True(RobotsTxtParser.Parse(content, TOKEN).IsAllowed("/anything"));
        Assert.False(RobotsTxtParser.Parse(content, "AhrefsBot").IsAllowed("/anything"));
    }

    [Fact]
    public void EmptyAllowEndsTheGroupToo()
    {
        const string content = """
            User-agent: *
            Allow:
            User-agent: BadBot
            Disallow: /
            """;

        Assert.True(RobotsTxtParser.Parse(content, TOKEN).IsAllowed("/anything"));
        Assert.False(RobotsTxtParser.Parse(content, "BadBot").IsAllowed("/anything"));
    }

    [Fact]
    public void GooglebotPresetGetsItsOwnEmptyGroup()
    {
        const string content = """
            User-agent: Googlebot
            Disallow:
            User-agent: *
            Disallow: /
            """;

        Assert.True(RobotsTxtParser.Parse(content, "Googlebot").IsAllowed("/page"));
        Assert.False(RobotsTxtParser.Parse(content, TOKEN).IsAllowed("/page"));
    }

    [Fact]
    public void ConsecutiveAgentsStillShareTheRulesThatFollow()
    {
        const string content = """
            User-agent: Googlebot
            User-agent: ShoutingIguana
            Disallow: /admin
            User-agent: *
            Disallow: /
            """;

        Assert.False(RobotsTxtParser.Parse(content, "Googlebot").IsAllowed("/admin/users"));
        Assert.True(RobotsTxtParser.Parse(content, "Googlebot").IsAllowed("/page"));
        Assert.True(RobotsTxtParser.Parse(content, TOKEN).IsAllowed("/page"));
    }

    [Theory]
    [InlineData("/a{b}", "https://example.com/a{b}/page")]
    [InlineData("/a|b", "https://example.com/a|b")]
    [InlineData("/x^y`z", "https://example.com/x^y`z")]
    [InlineData("/say\"hi\"<now>", "https://example.com/say\"hi\"<now>")]
    [InlineData("/50%off", "https://example.com/50%off")]
    [InlineData("/end%$", "https://example.com/end%")]
    [InlineData("/back\\slash", "https://example.com/back\\slash")]
    [InlineData("/search?q=a|b", "https://example.com/search?q=a|b")]
    [InlineData("/search?q=a\\b", "https://example.com/search?q=a\\b")]
    public void PatternsMatchCharactersUriPercentEncodes(string pattern, string url)
    {
        var robots = RobotsTxtParser.Parse($"User-agent: *\nDisallow: {pattern}", TOKEN);

        Assert.False(robots.IsAllowed(PathAndQueryOf(url)));
    }

    [Fact]
    public void CrawlDelayIsCapped()
    {
        Assert.Equal(RobotsTxtParser.MAX_CRAWL_DELAY_SECONDS, RobotsTxtParser.Parse("User-agent: *\nCrawl-delay: 1e12", TOKEN).CrawlDelaySeconds);
        Assert.Equal(30, RobotsTxtParser.Parse("User-agent: *\nCrawl-delay: 30", TOKEN).CrawlDelaySeconds);
        Assert.Null(RobotsTxtParser.Parse("User-agent: *\nCrawl-delay: NaN", TOKEN).CrawlDelaySeconds);
    }

    [Fact]
    public void RobotsTxtItselfIsAlwaysAllowed() =>
        Assert.True(RobotsTxtParser.Parse("User-agent: *\nDisallow: /", TOKEN).IsAllowed("/robots.txt"));

    [Fact]
    public void ReadsCrawlDelaySitemapsCommentsAndBom()
    {
        var robots = RobotsTxtParser.Parse("\uFEFFUser-agent: * # everyone\nCrawl-delay: 2.5\nDisallow: /tmp # temp\nSitemap: https://example.com/sitemap.xml", TOKEN);

        Assert.Equal(2.5, robots.CrawlDelaySeconds);
        Assert.Equal(["https://example.com/sitemap.xml"], robots.Sitemaps);
        Assert.False(robots.IsAllowed("/tmp/file"));
    }

    [Fact]
    public void NonAsciiPatternsMatchEncodedPaths()
    {
        var robots = RobotsTxtParser.Parse("User-agent: *\nDisallow: /café", TOKEN);

        Assert.False(robots.IsAllowed(new Uri("https://example.com/café/menu").PathAndQuery));
    }

    [Fact]
    public void RulesBeforeAnyUserAgentAreIgnored() =>
        Assert.True(RobotsTxtParser.Parse("Disallow: /\nUser-agent: *\nAllow: /", TOKEN).IsAllowed("/page"));

    /// <summary>The path the crawler checks: that of the stored (normalised) URL, as PageProcessor reads it.</summary>
    private static string PathAndQueryOf(string url) => new Uri(UrlNormalizer.Normalize(new Uri(url))).PathAndQuery;
}
