using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// What each of some URLs is (<see cref="UrlFacts"/>), read in one snapshot through the URL grid's own columns, so a
/// target's size or status reads exactly as the grid shows it: one read of the columns for the URLs, one for the ends of
/// those that redirect, and the inspector's own walk of each redirect and canonical.
/// </summary>
internal static class UrlFactsReader
{
    private static readonly string[] COLUMNS = ["statusCode", "outcome", "contentType", "sizeBytes", "pixelWidth", "pixelHeight", "indexability"];

    public static Dictionary<int, UrlFacts> Read(GridReadContext context, IReadOnlyCollection<int> ids)
    {
        Dictionary<int, UrlFacts> facts = [];
        if (ids.Count == 0)
        {
            return facts;
        }

        var own = Values(context, ids);
        Dictionary<int, RedirectEnd> ends = [];
        foreach (var id in ids)
        {
            if (UrlInspectorReader.RedirectsOf(context, id) is { } chain)
            {
                ends[id] = new RedirectEnd(chain.Final.Id, chain.Final.Url, chain.Final.StatusCode, chain.Final.Outcome, chain.Hops.Count, chain.Loop);
            }
        }

        var atEnds = ends.Count == 0 ? [] : Values(context, [.. ends.Values.Select(end => end.Id).Distinct()]);
        foreach (var id in ids)
        {
            var row = own.GetValueOrDefault(id);
            var end = ends.GetValueOrDefault(id);
            var response = end is null ? row : atEnds.GetValueOrDefault(end.Id);
            facts[id] = new UrlFacts(
                Int(row?[0]),
                Enum<UrlOutcome>(row?[1] as string),
                response?[2] as string,
                response?[3] as long?,
                Int(response?[4]),
                Int(response?[5]),
                end,
                Enum<IndexabilityReason>(row?[6] as string),
                Canonical(context, id));
        }

        return facts;
    }

    private static Dictionary<int, object?[]> Values(GridReadContext context, IReadOnlyCollection<int> ids)
    {
        var query = UrlQueryCompiler.Compile(new UrlQuery { Columns = COLUMNS }, context.Mode, GridScope.AllUrls, context.Reason);

        // The compiled columns lead with the id; a column the mode has no value for is left out, so look each up by name.
        var values = UrlGridReader.Values(context, query, [.. ids.Select(id => (long)id)]);
        var at = COLUMNS.Select(column => query.Columns.ToList().FindIndex(compiled => compiled.Id == column)).ToArray();
        return values.ToDictionary(entry => (int)entry.Key, entry => at.Select(index => index < 0 ? null : entry.Value[index]).ToArray());
    }

    /// <summary>The URL the URL's canonical names, when that's another URL the crawl has.</summary>
    private static UrlTarget? Canonical(GridReadContext context, int id)
    {
        using var command = context.Command("SELECT canonical_url_id FROM responses WHERE url_id = $id", [new("$id", id)]);
        return command.ExecuteScalar() is long canonical && canonical != id && canonical is > 0 and <= int.MaxValue
            ? UrlInspectorReader.TargetOf(context, (int)canonical)
            : null;
    }

    private static int? Int(object? value) => value is long number ? (int)Math.Clamp(number, int.MinValue, int.MaxValue) : null;

    private static T? Enum<T>(string? name) where T : struct, System.Enum =>
        name is not null && System.Enum.TryParse<T>(name, ignoreCase: true, out var value) ? value : null;
}
