namespace ShoutingIguana.Engine.Parsing.Models;

/// <summary>A <c>&lt;link rel="icon"&gt;</c> of a page: where the icon is, and what the page says of it.</summary>
/// <param name="Url">The icon's URL, resolved against the page: http or https, as every link the crawl keeps is.</param>
/// <param name="Sizes">The <c>sizes</c> attribute as written ("32x32", "16x16 32x32", "any"), or null.</param>
/// <param name="Type">The <c>type</c> attribute as written ("image/png"), or null.</param>
public sealed record IconFact(string Url, string? Sizes, string? Type);
