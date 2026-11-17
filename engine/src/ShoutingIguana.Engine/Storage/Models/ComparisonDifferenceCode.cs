namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>What differs between two crawls compared (<see cref="ComparisonDifference"/>), served as its <c>code</c> in camelCase.</summary>
public enum ComparisonDifferenceCode
{
    /// <summary>The start URL's scheme or host: no URL can be matched, so the comparison is counts only.</summary>
    StartOrigin,

    /// <summary>The start URL's path: partial.</summary>
    StartPath,

    /// <summary>What the crawl followed (<c>stayInStartFolder</c>, <c>includeSubdomains</c> and the rest): partial.</summary>
    Scope,

    /// <summary><c>maxUrls</c> or <c>maxDepth</c>: partial.</summary>
    Limits,

    /// <summary>What the crawl fetched (resources, external links, sitemaps, robots.txt): partial.</summary>
    Fetching,

    /// <summary>The user agent, the language, the headers' names or the cookies: noted only.</summary>
    Client,

    /// <summary>The request timeout or the retries: noted only.</summary>
    Timeouts,

    /// <summary>A crawl didn't read all it set out to: partial.</summary>
    StoppedEarly,

    /// <summary>One read fewer than half the pages the other did: partial.</summary>
    Coverage,

    /// <summary>The analyses were made by other rules: counts only, and no score change where the scores' versions differ.</summary>
    Rules
}
