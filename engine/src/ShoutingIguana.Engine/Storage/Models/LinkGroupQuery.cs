namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One page of the links a set of link filters matches, grouped by target (<see cref="LinkGridReader.Groups"/>).</summary>
public sealed record LinkGroupQuery
{
    /// <summary>The links grid's filters; none for every link.</summary>
    public IReadOnlyList<UrlFilter> Filters { get; init; } = [];

    /// <summary>The links grid's search.</summary>
    public string? Search { get; init; }

    /// <summary>
    /// The order of the targets: <see cref="LinkGroupPage.PAGES"/> (the default, the most first), <see cref="LinkGroupPage.LINKS"/>,
    /// or a column of the target the links grid sorts by (<c>to.url</c>, <c>to.statusCode</c>, <c>to.sizeBytes</c>).
    /// </summary>
    public UrlSort? Sort { get; init; }

    public int Offset { get; init; }

    public int Limit { get; init; } = LinkGroupPage.DEFAULT_LIMIT;

    /// <summary>Checks the user hid (<see cref="UrlQuery.HiddenChecks"/>): a link filter or a sort on the target's issue counts leaves them out.</summary>
    public IReadOnlyList<string> HiddenChecks { get; init; } = [];
}
