namespace ShoutingIguana.Engine.Parsing.Models;

/// <summary>A <c>&lt;form&gt;</c>: where it sends what's typed, how, and whether it asks for a password.</summary>
/// <param name="ActionUrl">The action resolved against the page's base: the page itself when there's none; null when it isn't a URL.</param>
/// <param name="Method">The method as written, lower case: get when there's none.</param>
/// <param name="HasPassword">The form has an <c>&lt;input type="password"&gt;</c>.</param>
public sealed record FormFact(string? ActionUrl, string Method, bool HasPassword);
