namespace ShoutingIguana.Engine.Parsing.Models;

/// <param name="Name">Lowercase name or property, e.g. <c>description</c> or <c>og:title</c>.</param>
public sealed record MetaTagFact(string Name, string Content);
