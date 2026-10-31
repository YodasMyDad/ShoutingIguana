namespace ShoutingIguana.Engine.Parsing.Models;

/// <param name="Href">The value as written.</param>
/// <param name="Url">Resolved and normalised target, or null when the href is not a valid http(s) URL.</param>
public sealed record CanonicalFact(string Href, string? Url, bool InHead, bool FromHttpHeader);
