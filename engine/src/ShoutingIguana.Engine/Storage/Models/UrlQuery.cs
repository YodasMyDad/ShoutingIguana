namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// One page of the URL grid (<see cref="UrlGridReader.Query"/>): the columns to read, the filters every row must match, a
/// quick search, the sort, and where the page starts: <see cref="Offset"/> for random access, or <see cref="After"/>, the
/// cursor the page before gave, to read on in order.
/// </summary>
public sealed record UrlQuery
{
    /// <summary>Column ids from <see cref="UrlGridReader.Columns"/>. The URL's id always comes first; none means the URL.</summary>
    public IReadOnlyList<string> Columns { get; init; } = [];

    /// <summary>Every one must match.</summary>
    public IReadOnlyList<UrlFilter> Filters { get; init; } = [];

    /// <summary>Text the URL or the title contains, ignoring the case of ASCII letters.</summary>
    public string? Search { get; init; }

    /// <summary>Up to three keys, unknown values last whatever the direction. Crawl order breaks ties, and is the default.</summary>
    public IReadOnlyList<UrlSort> Sort { get; init; } = [];

    public int Offset { get; init; }

    public int Limit { get; init; } = UrlGridReader.DEFAULT_LIMIT;

    /// <summary>The <see cref="UrlPage.Next"/> of the page before, to read on from it. Not with <see cref="Offset"/>.</summary>
    public string? After { get; init; }
}
