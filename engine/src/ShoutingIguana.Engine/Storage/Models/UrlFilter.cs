namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// A condition on one column of the URL grid. Which of <see cref="Value"/>, <see cref="Values"/>, <see cref="Min"/> and
/// <see cref="Max"/> it needs depends on the <see cref="Operator"/>. The column <c>check</c> is a filter only: the URLs
/// the given checks flagged, narrowed to one check's <see cref="Group"/> when one is given.
/// </summary>
public sealed record UrlFilter(string Column, FilterOperator Operator)
{
    public FilterValue? Value { get; init; }

    public IReadOnlyList<FilterValue>? Values { get; init; }

    /// <summary>For <see cref="FilterOperator.Between"/>: the lowest value matched, if there is one.</summary>
    public double? Min { get; init; }

    /// <summary>For <see cref="FilterOperator.Between"/>: the highest value matched, if there is one.</summary>
    public double? Max { get; init; }

    /// <summary>For <c>check</c> with one check: only its issues with this detail and other URL.</summary>
    public IssueGroupKey? Group { get; init; }
}
