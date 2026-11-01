namespace ShoutingIguana.Engine.Crawling.Models;

/// <summary>
/// Every URL fetched, once, by how it answered: together they make up <see cref="CrawlProgress.Fetched"/>. A firewall or
/// bot challenge is Blocked whatever its status; the rest go by their status code, and a URL with no status recorded
/// (robots.txt refused it, it timed out, it couldn't be reached or wasn't asked) is NoResponse.
/// </summary>
/// <param name="Success">2xx.</param>
/// <param name="Redirect">3xx.</param>
/// <param name="ClientError">4xx.</param>
/// <param name="ServerError">5xx.</param>
/// <param name="OtherStatus">A status outside 200–599, such as LinkedIn's 999.</param>
public sealed record CrawlStatusClasses(long Success, long Redirect, long ClientError, long ServerError, long OtherStatus, long NoResponse, long Blocked)
{
    public static CrawlStatusClasses None { get; } = new(0, 0, 0, 0, 0, 0, 0);
}
