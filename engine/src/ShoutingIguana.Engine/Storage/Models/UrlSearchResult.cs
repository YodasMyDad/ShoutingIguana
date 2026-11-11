namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One URL a search found, with what a list of results shows of it and its id to open it by.</summary>
/// <param name="Title">The page's first title, when it has one.</param>
/// <param name="StatusCode">The HTTP status, as the URL grid's column has it: null with no answer, or when not fetched.</param>
/// <param name="Outcome">What became of the request for it, as the URL grid's column has it: null when not fetched.</param>
/// <param name="Rank">How well it matches.</param>
/// <param name="MatchedIn">Where the text was found: the match the rank is for, the URL's when both match as well.</param>
public sealed record UrlSearchResult(int Id, string Url, string? Title, bool IsInternal, int? StatusCode, UrlOutcome? Outcome, UrlSearchRank Rank, UrlSearchField MatchedIn);
