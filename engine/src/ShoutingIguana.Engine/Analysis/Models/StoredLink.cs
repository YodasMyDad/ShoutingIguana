using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>An outgoing link of an analysed page, from the links table.</summary>
/// <param name="Text">Anchor text, image alt text, or the hreflang code, depending on the kind.</param>
internal readonly record struct StoredLink(int ToId, LinkKind Kind, LinkFlags Flags, LinkPosition Position, string? Text, string? Fragment);
