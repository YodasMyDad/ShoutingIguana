namespace ShoutingIguana.TestSite.Models;

/// <summary>What a correct crawl of one revision of a <see cref="SyntheticSite"/> makes of a URL.</summary>
public enum SyntheticUrlFate
{
    /// <summary>Nothing leads to it: the crawl doesn't have it.</summary>
    NotFound,

    /// <summary>Found, and never asked for: robots.txt keeps the crawler out.</summary>
    NotRead,

    /// <summary>A page the analysis audits.</summary>
    Page,

    /// <summary>Any other answer: a redirect, an error status, a file, a page on another site.</summary>
    Answered,

    /// <summary>No answer (the connection dropped), or a firewall's block page in place of one.</summary>
    Failed
}
