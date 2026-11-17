using System.Globalization;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The URL grid's columns worked out for the rows of the page read rather than by its SQL (<see cref="ColumnSource.Computed"/>):
/// a URL's redirects hop by hop, the robots.txt rule that refused it, and an image's own size in pixels, which a crawl
/// made before the crawler read them has no column for. Each costs what the inspector's same read does, for each row,
/// and only when the column is asked for.
/// </summary>
internal static class UrlComputedColumns
{
    public const string PIXEL_WIDTH = "pixelWidth";
    public const string PIXEL_HEIGHT = "pixelHeight";
    public const string REDIRECT_CHAIN = "redirectChain";
    public const string ROBOTS_RULE = "robotsRule";

    /// <summary>Fills the computed columns of <paramref name="values"/>, rows by URL id, in place.</summary>
    public static void Fill(GridReadContext context, IReadOnlyList<UrlColumnDefinition> columns, Dictionary<long, object?[]> values)
    {
        var width = Index(columns, PIXEL_WIDTH);
        var height = Index(columns, PIXEL_HEIGHT);
        if ((width >= 0 || height >= 0) && context.HasPixelSizes && values.Count > 0)
        {
            using var command = context.Command(
                $"SELECT url_id, pixel_width, pixel_height FROM responses WHERE url_id IN ({string.Join(",", values.Keys.Select(id => id.ToString(CultureInfo.InvariantCulture)))})", []);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var row = values[reader.GetInt64(0)];
                if (width >= 0)
                {
                    row[width] = reader.IsDBNull(1) ? null : reader.GetInt64(1);
                }

                if (height >= 0)
                {
                    row[height] = reader.IsDBNull(2) ? null : reader.GetInt64(2);
                }
            }
        }

        if (Index(columns, REDIRECT_CHAIN) is var chain and >= 0)
        {
            foreach (var (id, row) in values)
            {
                row[chain] = UrlInspectorReader.RedirectsOf(context, (int)id);
            }
        }

        if (Index(columns, ROBOTS_RULE) is var robots and >= 0)
        {
            foreach (var (id, row) in values)
            {
                row[robots] = RobotsReader.RuleOf(context, (int)id);
            }
        }
    }

    private static int Index(IReadOnlyList<UrlColumnDefinition> columns, string id)
    {
        for (var index = 0; index < columns.Count; index++)
        {
            if (columns[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// A computed column's value as one line of text, for an export: "301 → https://x/b/ (301) → https://x/c/ (200)", or
    /// "Disallow: /cart/ (line 12 of https://x/robots.txt)". Null for null.
    /// </summary>
    public static string? Text(object? value) => value switch
    {
        RedirectChain chain => ChainText(chain),
        RobotsRefusal rule => rule.Reason switch
        {
            RobotsBlockReason.Rule => string.Create(CultureInfo.InvariantCulture, $"{rule.Rule} (line {rule.Line:N0} of {rule.Origin}/robots.txt)"),
            RobotsBlockReason.Unreachable => "robots.txt couldn’t be read",
            _ => "No rule kept"
        },
        null => null,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };

    private static string ChainText(RedirectChain chain)
    {
        var text = new System.Text.StringBuilder();
        foreach (var hop in chain.Hops)
        {
            text.Append(text.Length == 0 ? string.Empty : " ").Append("→ ");
            if (hop.Via == RedirectVia.MetaRefresh)
            {
                text.Append("(meta refresh) ");
            }

            text.Append(hop.Url).Append(" (").Append(Status(hop.StatusCode, hop.Outcome)).Append(')');
        }

        if (chain.Loop)
        {
            text.Append(", a loop");
        }
        else if (chain.TooLong)
        {
            text.Append(string.Create(CultureInfo.InvariantCulture, $", more than {Analysis.UrlTable.MAX_REDIRECT_HOPS} hops"));
        }

        return text.ToString();
    }

    private static string Status(int? statusCode, UrlOutcome? outcome) =>
        statusCode?.ToString(CultureInfo.InvariantCulture) ?? (outcome is { } known ? ColumnWords.OutcomeTitle(known) : "not checked");
}
