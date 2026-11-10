namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// What an origin's answer for robots.txt meant to the crawl, as its cache took it (<see cref="Robots.RobotsCache"/>): RFC
/// 9309's reading, but for an origin that gave no answer at all and isn't the start URL's, which gets no verdict.
/// </summary>
public enum RobotsState
{
    /// <summary>A 2xx: its rules applied.</summary>
    Read,

    /// <summary>Another 4xx than 429, or redirects that couldn't be followed: no rules, so everything was allowed.</summary>
    Missing,

    /// <summary>
    /// Nothing could be read, and the crawl refused the origin's URLs until it could: a 5xx or a 429 from any origin, or
    /// no answer at all from the start URL's own.
    /// </summary>
    Unreachable,

    /// <summary>
    /// No answer at all from an origin other than the start URL's (another site, the site's www twin, its other scheme):
    /// the crawl gave no verdict and went ahead, so nothing was refused and the origin's URLs show their own errors.
    /// </summary>
    Unanswered
}
