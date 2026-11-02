namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// One value of a facet and how many URLs have it. Every count opens its URLs: <see cref="Filter"/>, added to the query's
/// filters, matches exactly <see cref="Count"/> of them.
/// </summary>
/// <param name="Value">A string (an enum's name, a bucket's id), a long, a bool, or null for unknown.</param>
public sealed record FacetBucket(object? Value, long Count, UrlFilter Filter)
{
    /// <summary>A range bucket's lowest value.</summary>
    public long? Min { get; init; }

    /// <summary>A range bucket's highest value, or null when it has none.</summary>
    public long? Max { get; init; }
}
