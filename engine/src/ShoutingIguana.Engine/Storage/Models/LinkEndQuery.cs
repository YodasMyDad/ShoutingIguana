namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One page of the URLs at one end of the links a set of link filters matches (<see cref="LinkGridReader.Ends"/>).</summary>
public sealed record LinkEndQuery
{
    public LinkEnd End { get; init; } = LinkEnd.To;

    /// <summary>The links grid's filters; none for every link.</summary>
    public IReadOnlyList<UrlFilter> Filters { get; init; } = [];

    /// <summary>The links grid's search.</summary>
    public string? Search { get; init; }

    public int Offset { get; init; }

    public int Limit { get; init; } = UrlGridReader.DEFAULT_LIMIT;
}
