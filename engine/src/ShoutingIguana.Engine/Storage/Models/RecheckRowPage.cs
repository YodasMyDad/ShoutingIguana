namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A page of a recheck's URLs, narrowed by outcome and role.</summary>
/// <param name="Total">The URLs that match, on every page.</param>
public sealed record RecheckRowPage(int Total, int Offset, IReadOnlyList<RecheckRow> Rows);
