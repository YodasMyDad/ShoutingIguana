namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// How the site answered a recheck for a URL it judges (<c>recheck_urls.answer</c>), with or without a check to judge it by:
/// read, refused again (and why), or never asked. Stored as its number as the recheck's run ends.
/// </summary>
public enum RecheckAnswer
{
    /// <summary>Read: the site answered with a page, a redirect, or an error code of its own.</summary>
    Read = 0,

    /// <summary>
    /// Never asked: the run ended before it (stopped, or the time a section the site limits may take ran out first), its
    /// host had stopped answering, or the crawl never fetched it either.
    /// </summary>
    NotAsked = 1,

    /// <summary>Refused again: the site asked the crawler to slow down (429 Too Many Requests).</summary>
    RateLimited = 2,

    /// <summary>Refused again: a firewall or a bot check answered in place of the page.</summary>
    Blocked = 3,

    /// <summary>Refused again: robots.txt disallows it, or couldn't be read.</summary>
    RobotsTxt = 4,

    /// <summary>Refused again: no answer (it timed out, or the connection failed).</summary>
    NoAnswer = 5
}
