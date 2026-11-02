namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// One condition of a grid query, compiled. <see cref="Sql"/> may use an index; <see cref="Disabled"/> is the same
/// condition kept off every index, so a plan can choose which index drives it: SQLite can't tell a rare value from a
/// common one, so the reader counts and chooses.
/// </summary>
/// <param name="Driving">It can drive a query through an index, or by row id from a subquery.</param>
/// <param name="Key">The condition with its values, to cache counts by.</param>
internal sealed record GridPredicate(string Sql, string Disabled, IReadOnlyList<KeyValuePair<string, object>> Parameters, bool Driving, string Key)
{
    /// <summary>Its count is already known, so choosing a plan needn't count it: the rows of a check or of one group.</summary>
    public long? KnownCount { get; init; }

    /// <summary>
    /// SQL giving the URL ids it matches, for a condition read through a subquery (a title's text, a search): read once,
    /// and kept while few enough, so the scan of the crawl's text isn't run again for the count and each page.
    /// </summary>
    public string? Ids { get; init; }
}
