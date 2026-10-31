namespace ShoutingIguana.Engine.Parsing.Models;

public sealed record HreflangFact(string Language, string Href, string? Url, bool InHead, bool FromHttpHeader);
