namespace ShoutingIguana.TestSite.Models;

/// <summary>One check's change between two revisions' crawls: its state, its counts, and the state of every URL it flagged in either.</summary>
/// <param name="Then">The URLs it flagged then, or null when it didn't run.</param>
/// <param name="Now">The URLs it flags now, or null when it didn't run.</param>
/// <param name="Rows">Each URL flagged then or now, by its URL.</param>
public sealed record SyntheticCheckChange(
    string CheckId,
    SyntheticCheckState State,
    int? Then,
    int? Now,
    IReadOnlyDictionary<string, SyntheticRowState> Rows)
{
    public int Count(SyntheticRowState state) => Rows.Values.Count(row => row == state);
}
