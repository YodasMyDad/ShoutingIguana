namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>The best few URLs a search found, best first (<see cref="UrlSearchReader.Search"/>).</summary>
/// <param name="Matching">
/// Every URL whose URL or title contains the text: the URL grid's <c>matching</c> under the same <c>search</c>, so "all
/// 1,918 in URLs" lands on that many rows.
/// </param>
/// <param name="Total">Every URL in the crawl.</param>
public sealed record UrlSearchResults(IReadOnlyList<UrlSearchResult> Results, long Matching, long Total);
