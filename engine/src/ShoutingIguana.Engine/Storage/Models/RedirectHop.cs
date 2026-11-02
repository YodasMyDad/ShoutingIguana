namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One URL a redirect chain reaches, and how the URL before it led there.</summary>
/// <param name="Outcome">Null when it wasn't fetched.</param>
public sealed record RedirectHop(int Id, string Url, int? StatusCode, UrlOutcome? Outcome, RedirectVia Via);
