namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One page of an origin's blocked URLs, in crawl order.</summary>
/// <param name="Total">The origin's blocked URLs.</param>
/// <param name="Matching">Those the line or reason asked for narrows them to.</param>
public sealed record RobotsBlockedPage(string Origin, long Total, long Matching, int Offset, IReadOnlyList<RobotsBlockedUrl> Rows);
