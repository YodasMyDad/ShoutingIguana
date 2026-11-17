namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>How a row a check flags now moved since the other crawl (<see cref="CrawlComparisonReader.ReadRowChanges"/>).</summary>
/// <param name="State"><see cref="RowChangeState.Still"/>, <see cref="RowChangeState.New"/>, <see cref="RowChangeState.NewUrl"/> or <see cref="RowChangeState.NotComparedThen"/>.</param>
/// <param name="DetailThen">The issue's detail in the other crawl; null unless <see cref="RowChangeState.Still"/>.</param>
public sealed record RowChange(RowChangeState State, string? DetailThen = null);
