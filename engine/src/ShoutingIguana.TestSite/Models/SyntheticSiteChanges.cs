namespace ShoutingIguana.TestSite.Models;

/// <summary>
/// What a correct comparison of a crawl of one revision of a <see cref="SyntheticSite"/> with a crawl of another must
/// find: every check that flagged a URL in either, or ran in only one, with its state, its counts and each URL's state.
/// A check that isn't here ran in both and flagged nothing in either: the same, at 0. slow-response is left out, as a
/// busy machine makes pages slow that the site didn't plant (<see cref="SyntheticSiteExpectations.SLOW_RESPONSE"/>).
/// </summary>
public sealed record SyntheticSiteChanges(IReadOnlyDictionary<string, SyntheticCheckChange> Checks)
{
    private const int LISTED_ROWS = 5;

    /// <summary>Differences between a comparison and these, one line per check, naming the URLs involved.</summary>
    /// <param name="checks">Each check the comparison gave: its id, its state's name, and its counts then and now.</param>
    /// <param name="rows">Each URL a check flagged in either crawl, with its state's name.</param>
    /// <param name="allCheckIds">Every check the analysis can run, so those that weren't planted are held to nothing.</param>
    public IReadOnlyList<string> Compare(
        IEnumerable<(string CheckId, string State, int? Then, int? Now)> checks,
        IEnumerable<(string CheckId, string Url, string State)> rows,
        IEnumerable<string> allCheckIds)
    {
        var found = checks.Where(check => check.CheckId != SyntheticSiteExpectations.SLOW_RESPONSE)
            .ToDictionary(check => check.CheckId, StringComparer.Ordinal);
        var foundRows = rows.Where(row => row.CheckId != SyntheticSiteExpectations.SLOW_RESPONSE)
            .GroupBy(row => row.CheckId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToDictionary(row => row.Url, row => row.State, StringComparer.Ordinal), StringComparer.Ordinal);

        List<string> mismatches = [];
        foreach (var id in allCheckIds.Where(id => id != SyntheticSiteExpectations.SLOW_RESPONSE).Order(StringComparer.Ordinal))
        {
            var expected = Checks.GetValueOrDefault(id) ?? new SyntheticCheckChange(id, SyntheticCheckState.Same, 0, 0, new Dictionary<string, SyntheticRowState>());
            if (!found.TryGetValue(id, out var actual))
            {
                mismatches.Add($"{id}: expected {expected.State} ({expected.Then} then, {expected.Now} now), found no change");
                continue;
            }

            if (actual.State != expected.State.ToString() || actual.Then != expected.Then || actual.Now != expected.Now)
            {
                mismatches.Add($"{id}: expected {expected.State} ({expected.Then} then, {expected.Now} now), found {actual.State} ({actual.Then} then, {actual.Now} now)");
            }

            var actualRows = foundRows.GetValueOrDefault(id) ?? new Dictionary<string, string>(StringComparer.Ordinal);
            var wrong = expected.Rows
                .Where(row => actualRows.GetValueOrDefault(row.Key) != row.Value.ToString())
                .Select(row => $"{row.Key} {row.Value} (found {actualRows.GetValueOrDefault(row.Key) ?? "nothing"})")
                .Concat(actualRows.Where(row => !expected.Rows.ContainsKey(row.Key)).Select(row => $"{row.Key} unexpected {row.Value}"))
                .Order(StringComparer.Ordinal)
                .ToList();
            if (wrong.Count > 0)
            {
                mismatches.Add($"{id} rows: {string.Join("; ", wrong.Take(LISTED_ROWS))}{(wrong.Count > LISTED_ROWS ? $" and {wrong.Count - LISTED_ROWS:N0} more" : string.Empty)}");
            }
        }

        mismatches.AddRange(found.Keys.Where(id => !allCheckIds.Contains(id)).Select(id => $"Found a change for '{id}', which isn't in the catalog"));
        return mismatches;
    }
}
