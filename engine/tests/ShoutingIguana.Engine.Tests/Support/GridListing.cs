namespace ShoutingIguana.Engine.Tests.Support;

/// <summary>
/// The grid crawl's checks as the report and the workbook list them: the jobs in the order to fix them, numbered from 1,
/// and after the 4xx the two serious checks restoring its URLs clears entirely (a canonical and an hreflang target that
/// fail), with its number and no number of their own, their sheets lettered under it ("2a", "2b"). The report's main
/// test pins those folds against the analysis's clears.
/// </summary>
internal static class GridListing
{
    public const string JOB = "internal-client-error";

    public static readonly string[] FOLDED = ["canonical-broken", "hreflang-broken-target"];

    /// <summary>The checks of <paramref name="order"/> (the order to fix them, hidden ones left out) as they're listed.</summary>
    public static List<(string Id, int Number, string Sheet, bool Folded)> Listed(IReadOnlyList<string> order)
    {
        var folded = order.Contains(JOB) ? FOLDED.Where(order.Contains).ToList() : [];
        List<(string Id, int Number, string Sheet, bool Folded)> listed = [];
        var number = 0;
        foreach (var id in order.Where(id => !folded.Contains(id)))
        {
            number++;
            listed.Add((id, number, $"{number}", false));
            if (id == JOB)
            {
                listed.AddRange(folded.Select((each, index) => (each, number, $"{number}{(char)('a' + index)}", true)));
            }
        }

        return listed;
    }
}
