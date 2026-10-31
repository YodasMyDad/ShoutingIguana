namespace ShoutingIguana.Engine.Parsing.Models;

/// <param name="Target">Absolute http(s) target, fragment included.</param>
/// <param name="Text">Anchor text, image alt text, or the hreflang code for hreflang links.</param>
public sealed record ExtractedLink(Uri Target, LinkKind Kind, LinkFlags Flags, LinkPosition Position, string? Text);
