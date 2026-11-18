namespace ShoutingIguana.Engine.Parsing.Models;

public sealed record HeadingFact(int Level, string Text)
{
    /// <summary>The heading has no text of its own: its text is an image's alt, as a logo heading's is (collection 4).</summary>
    public bool FromImageAlt { get; init; }
}
