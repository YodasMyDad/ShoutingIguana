namespace ShoutingIguana.Engine.Crawling.Models;

/// <summary>Why the URLs in <see cref="CrawlStatusClasses.NoResponse"/> have no status. Together they make it up.</summary>
/// <param name="RobotsDisallowed">robots.txt refused them, by a rule or because it couldn't be read: never requested.</param>
/// <param name="NetworkError">The connection failed: DNS, refused, reset, TLS. Not counting <paramref name="NotChecked"/>.</param>
/// <param name="NotChecked">Never asked, because their host had stopped answering: not broken, just unknown.</param>
/// <param name="ProcessingError">A response arrived but the crawler failed to process it, so none was recorded.</param>
public sealed record CrawlNoResponseCounts(long RobotsDisallowed, long Timeout, long NetworkError, long NotChecked, long ProcessingError)
{
    public static CrawlNoResponseCounts None { get; } = new(0, 0, 0, 0, 0);
}
