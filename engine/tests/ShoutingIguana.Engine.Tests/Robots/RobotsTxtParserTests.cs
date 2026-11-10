using ShoutingIguana.Engine.Robots;
using ShoutingIguana.Engine.Robots.Models;
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

    /// <summary>
    /// The parse can keep every line as it reads it, for showing the file: what each is, whether its group is the crawler's,
    /// and why nothing was made of it. A rule knows its line, and the rule that decides a URL is the one the crawl obeys.
    /// </summary>
    [Fact]
    public void TheParseCanKeepEveryLineWithWhatItIs()
    {
        const string FILE = "\uFEFF# Rules\r\nDisallow: /early\r\n\r\nUser-agent: Googlebot\rUser-agent: ShoutingIguana/2.0\nDisallow: /private/ # ours\nAllow: /private/open\n" +
                            "Crawl-delay: soon\nDisallow:\n\nUser-agent: *\nDisallow: /\nCrawl-delay: 2\nSitemap: /sitemap.xml\nSitemap:\nHost: example.com\njust words\n: no name\n";
        List<RobotsLine> lines = [];
        var robots = RobotsTxtParser.Parse(FILE, TOKEN, lines);

        (int, string, RobotsLineKind, bool?, RobotsLineProblem?)[] expected =
        [
            (1, "# Rules", RobotsLineKind.Comment, null, null),
            (2, "Disallow: /early", RobotsLineKind.Disallow, false, RobotsLineProblem.NoUserAgent),
            (3, "", RobotsLineKind.Blank, null, null),
            (4, "User-agent: Googlebot", RobotsLineKind.UserAgent, true, null),
            (5, "User-agent: ShoutingIguana/2.0", RobotsLineKind.UserAgent, true, null),
            (6, "Disallow: /private/ # ours", RobotsLineKind.Disallow, true, null),
            (7, "Allow: /private/open", RobotsLineKind.Allow, true, null),
            (8, "Crawl-delay: soon", RobotsLineKind.CrawlDelay, true, RobotsLineProblem.InvalidValue),
            (9, "Disallow:", RobotsLineKind.Disallow, true, null),
            (10, "", RobotsLineKind.Blank, null, null),
            (11, "User-agent: *", RobotsLineKind.UserAgent, false, null),
            (12, "Disallow: /", RobotsLineKind.Disallow, false, null),
            (13, "Crawl-delay: 2", RobotsLineKind.CrawlDelay, false, null),
            (14, "Sitemap: /sitemap.xml", RobotsLineKind.Sitemap, true, null),
            (15, "Sitemap:", RobotsLineKind.Sitemap, false, RobotsLineProblem.InvalidValue),
            (16, "Host: example.com", RobotsLineKind.Other, null, RobotsLineProblem.UnknownDirective),
            (17, "just words", RobotsLineKind.Invalid, null, RobotsLineProblem.NotADirective),
            (18, ": no name", RobotsLineKind.Invalid, null, RobotsLineProblem.NotADirective)
        ];
        Assert.Equal(expected, lines.Select(line => (line.Number, line.Text, line.Kind, line.Applies, line.Problem)));

        // The rule that decides a URL names its line: the longest match, an allow winning a tie, none for what nothing refuses.
        Assert.Equal((6, false), (robots.Match("/private/report")!.Line, robots.Match("/private/report")!.Allow));
        Assert.Equal((7, true), (robots.Match("/private/open/day")!.Line, robots.Match("/private/open/day")!.Allow));
        Assert.Null(robots.Match("/early"));
        Assert.Null(robots.Match("/robots.txt"));
        Assert.Null(robots.CrawlDelaySeconds);
        Assert.Equal(["/sitemap.xml"], robots.Sitemaps);

        // Kept or not, the rules are the same, and what's allowed is what the deciding rule says.
        var unkept = RobotsTxtParser.Parse(FILE, TOKEN);
        string[] paths = ["/", "/early", "/private/", "/private/open", "/private/x?y=1", "/sitemap.xml", "/robots.txt"];
        Assert.All(paths, path => Assert.Equal((unkept.IsAllowed(path), robots.Match(path)?.Line), (robots.Match(path)?.Allow ?? true, unkept.Match(path)?.Line)));
        Assert.Equal(12, RobotsTxtParser.Parse(FILE, "OtherBot").Match("/anything")!.Line);

        // No text has no lines; a last line without a line break is a line.
        List<RobotsLine> none = [];
        RobotsTxtParser.Parse(string.Empty, TOKEN, none);
        Assert.Empty(none);
        List<RobotsLine> two = [];
        RobotsTxtParser.Parse("User-agent: *\nDisallow: /", TOKEN, two);
        Assert.Equal([1, 2], two.Select(line => line.Number));
        Assert.Equal(0, RobotsTxt.DisallowAll.Match("/x")!.Line);
    }

    /// <summary>The path the crawler checks: that of the stored (normalised) URL, as PageProcessor reads it.</summary>
    private static string PathAndQueryOf(string url) => new Uri(UrlNormalizer.Normalize(new Uri(url))).PathAndQuery;
}
