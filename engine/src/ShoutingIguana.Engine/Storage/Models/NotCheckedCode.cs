namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Why this crawl didn't judge a URL a check flagged then (<see cref="NotCheckedReason"/>), served in camelCase.</summary>
public enum NotCheckedCode
{
    /// <summary>Found but not fetched: a limit, nofollow, stopped early, a host that had stopped answering.</summary>
    NotRead,

    /// <summary>An error or no answer: its status, or the failure, in the text.</summary>
    Failed,

    /// <summary>A firewall or bot protection answered in its place.</summary>
    Blocked,

    /// <summary>The site kept asking the crawler to slow down.</summary>
    RateLimited,

    /// <summary>robots.txt refuses it now.</summary>
    Robots,

    /// <summary>It now forwards elsewhere.</summary>
    Redirects,

    /// <summary>The check didn't run in this crawl's analysis.</summary>
    CheckSkipped
}
