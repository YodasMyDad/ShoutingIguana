namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A comparison's totals: the checks by state, and their rows added up over every check.</summary>
/// <param name="Checks">How many checks are in each state; every state is there, at 0 where none is.</param>
/// <param name="Issues">The rows added up; null when the comparison is <see cref="Comparability.CountsOnly"/>.</param>
public sealed record ComparisonTotals(IReadOnlyDictionary<CheckChangeState, int> Checks, RowSplit? Issues);
