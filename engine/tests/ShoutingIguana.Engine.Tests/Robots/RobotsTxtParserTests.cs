using ShoutingIguana.Engine.Robots;

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
    public void RobotsTxtItselfIsAlwaysAllowed() =>
        Assert.True(RobotsTxtParser.Parse("User-agent: *\nDisallow: /", TOKEN).IsAllowed("/robots.txt"));

    [Fact]
    public void ReadsCrawlDelaySitemapsCommentsAndBom()
    {
        var robots = RobotsTxtParser.Parse("﻿User-agent: * # everyone\nCrawl-delay: 2.5\nDisallow: /tmp # temp\nSitemap: https://example.com/sitemap.xml", TOKEN);

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
}
