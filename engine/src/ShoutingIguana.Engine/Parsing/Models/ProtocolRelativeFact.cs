namespace ShoutingIguana.Engine.Parsing.Models;

/// <summary>The page's <c>href</c> and <c>src</c> values that start <c>//</c>: how many, and the first as written.</summary>
public sealed record ProtocolRelativeFact(int Count, string First)
{
    /// <summary>
    /// How many of them are in no link of the page's (a resource hint such as <c>preconnect</c>, a <c>&lt;base&gt;</c>):
    /// the rest are in the links marked <see cref="LinkFlags.ProtocolRelative"/>. Counted from collection 6 on
    /// (<see cref="PageFacts.CURRENT_COLLECTION"/>); 0 for a page parsed before.
    /// </summary>
    public int Unlinked { get; init; }

    /// <summary>The first of those in no link, as written, or null when there's none.</summary>
    public string? FirstUnlinked { get; init; }
}
