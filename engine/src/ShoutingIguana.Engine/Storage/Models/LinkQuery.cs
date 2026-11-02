using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One page of the links to or from a URL (<see cref="UrlInspectorReader.ReadLinks"/>), in the order they were found.</summary>
public sealed record LinkQuery
{
    /// <summary>The kinds of link; none means every kind.</summary>
    public IReadOnlyList<LinkKind> Kinds { get; init; } = [];

    public int Offset { get; init; }

    public int Limit { get; init; } = UrlGridReader.DEFAULT_LIMIT;

    /// <summary>The <see cref="LinkPage.Next"/> of the page before. Not with <see cref="Offset"/>.</summary>
    public string? After { get; init; }
}
