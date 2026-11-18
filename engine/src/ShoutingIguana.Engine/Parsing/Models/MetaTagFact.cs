namespace ShoutingIguana.Engine.Parsing.Models;

/// <param name="Name">Lowercase name or property, e.g. <c>description</c> or <c>og:title</c>.</param>
public sealed record MetaTagFact(string Name, string Content)
{
    /// <summary>
    /// Written only as <c>property=</c> (as Open Graph's RDFa is), not <c>name=</c>: X reads its own tags by name. False in a
    /// crawl made before it was noted (<see cref="PageFacts.CURRENT_COLLECTION"/> 5).
    /// </summary>
    public bool IsProperty { get; init; }
}
