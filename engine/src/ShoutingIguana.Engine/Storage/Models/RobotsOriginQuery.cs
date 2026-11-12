namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Which of a crawl's origins to list (<see cref="RobotsReader.ReadOrigins(string, RobotsOriginQuery, ReadCache?, CancellationToken)"/>).</summary>
public sealed record RobotsOriginQuery
{
    /// <summary>Only the origins whose name contains this text, case ignored; every origin when null or empty.</summary>
    public string? Search { get; init; }

    /// <summary>True for only the origins that blocked at least one URL, false for only those that blocked none; both when null.</summary>
    public bool? Blocked { get; init; }

    public int Offset { get; init; }

    public int Limit { get; init; } = UrlGridReader.DEFAULT_LIMIT;
}
