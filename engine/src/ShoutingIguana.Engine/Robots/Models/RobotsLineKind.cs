namespace ShoutingIguana.Engine.Robots.Models;

/// <summary>What a line of a robots.txt file is, as the crawler's parser reads it.</summary>
public enum RobotsLineKind
{
    UserAgent,
    Allow,
    Disallow,
    Sitemap,
    CrawlDelay,
    Comment,
    Blank,

    /// <summary>A directive the crawler doesn't use, such as <c>Host</c> or <c>Clean-param</c>.</summary>
    Other,

    /// <summary>Not a <c>name: value</c> line.</summary>
    Invalid
}
