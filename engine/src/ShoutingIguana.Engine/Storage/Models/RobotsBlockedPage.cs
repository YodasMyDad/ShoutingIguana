namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One page of an origin's blocked URLs, in crawl order.</summary>
/// <param name="Total">The origin's blocked URLs.</param>
/// <param name="Matching">Those the line, reason or conflict asked for narrows them to.</param>
public sealed record RobotsBlockedPage(string Origin, long Total, long Matching, int Offset, IReadOnlyList<RobotsBlockedUrl> Rows)
{
    /// <summary>
    /// The origin's blocked URLs that a sitemap lists or a menu links to (<see cref="RobotsBlockedUrl.Conflict"/>), of all
    /// of them whatever is narrowed to; null when the crawl has changed since its analysis, or has none.
    /// </summary>
    public long? Conflicts { get; init; }
}
