namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A URL of the crawl, with the status it answered: null when it wasn't fetched or gave no HTTP answer.</summary>
public sealed record UrlTarget(int Id, string Url, int? StatusCode);
