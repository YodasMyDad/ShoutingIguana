namespace ShoutingIguana.Engine.Parsing.Models;

public sealed record ParsedPage(PageFacts Facts, IReadOnlyList<ExtractedLink> Links);
