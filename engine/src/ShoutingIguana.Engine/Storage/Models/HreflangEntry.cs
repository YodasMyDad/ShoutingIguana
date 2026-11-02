namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One hreflang annotation of a page.</summary>
/// <param name="Url">The target, resolved and normalised; null when the href isn't a valid http(s) URL.</param>
/// <param name="Target">The target as the crawl has it, with its status; null when the crawl never found it.</param>
public sealed record HreflangEntry(string Language, string Href, string? Url, UrlTarget? Target, bool InHead, bool FromHttpHeader);
