namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Where a URL's redirects end, for a URL an issue names: the URL to link to, or name, instead.</summary>
/// <param name="Hops">The redirects followed to get there, meta refreshes counted.</param>
/// <param name="Loop">They come back on themselves: <paramref name="Id"/> is the URL the loop returns to.</param>
public sealed record RedirectEnd(int Id, string Url, int? StatusCode, UrlOutcome? Outcome, int Hops, bool Loop);
