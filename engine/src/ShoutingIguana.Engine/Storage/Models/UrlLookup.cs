namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A URL of the crawl found by its text (<see cref="UrlGridReader.Lookup"/>).</summary>
/// <param name="Url">The URL found, as the crawl stores it.</param>
/// <param name="Requested">
/// The URL the text stood for, normalised, when the crawl has it only on another version of the site (www or not, http or
/// https), as <paramref name="Url"/>; null when the URL found is the one asked for.
/// </param>
public sealed record UrlLookup(int Id, string Url, string? Requested);
