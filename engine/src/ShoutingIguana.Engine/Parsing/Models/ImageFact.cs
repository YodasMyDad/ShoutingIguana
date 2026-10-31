namespace ShoutingIguana.Engine.Parsing.Models;

/// <param name="HasAlt">The alt attribute exists (an empty alt is valid for decorative images).</param>
public sealed record ImageFact(string? Url, string? Alt, bool HasAlt, string? Width, string? Height, bool HasSrcset, string? Loading);
