namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One origin's robots.txt, as the crawl last read it.</summary>
/// <param name="Origin">Scheme, host and port unless it's the scheme's default: <c>https://www.example.com</c>.</param>
/// <param name="State">Null when the crawl stored no robots.txt for the origin, though it stored URLs there as refused.</param>
/// <param name="Error">How it failed, when there was no status.</param>
/// <param name="Bytes">The size of the text kept, or null when none was.</param>
/// <param name="Blocked">The origin's URLs the crawl stored as refused by robots.txt.</param>
public sealed record RobotsOrigin(string Origin, bool IsInternal, RobotsState? State, int? StatusCode, DateTimeOffset? FetchedAt, string? Error, long? Bytes, long Blocked);
