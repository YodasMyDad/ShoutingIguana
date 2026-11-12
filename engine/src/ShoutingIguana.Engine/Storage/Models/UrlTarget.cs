namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A URL of the crawl, with the status it answered: null when it wasn't fetched or gave no HTTP answer.</summary>
/// <param name="Outcome">
/// What became of the request for it, which tells the two apart: null when it wasn't fetched, and for a value this engine
/// doesn't know.
/// </param>
public sealed record UrlTarget(int Id, string Url, int? StatusCode, UrlOutcome? Outcome = null);
