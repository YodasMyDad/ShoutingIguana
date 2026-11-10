namespace ShoutingIguana.Engine.Robots.Models;

/// <summary>Why the crawler's parser made nothing of a line of a robots.txt file.</summary>
public enum RobotsLineProblem
{
    /// <summary>A rule or a crawl delay before any <c>User-agent</c> line: it belongs to no group.</summary>
    NoUserAgent,

    /// <summary>A value the directive can't take: a <c>Crawl-delay</c> that isn't a number, a <c>Sitemap</c> without a URL.</summary>
    InvalidValue,

    UnknownDirective,

    NotADirective
}
