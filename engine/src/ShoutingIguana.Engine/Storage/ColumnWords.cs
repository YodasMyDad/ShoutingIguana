using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The grids' columns and their values in words: what a header, a cell and an export's file say for an id such as
/// <c>statusClass</c> or a value such as <c>clientError</c>. They are the engine's so that the app's grids and the files
/// exported from them can't name a column or a value two ways: a host shows these, and an export writes them.
/// </summary>
internal static class ColumnWords
{
    /// <summary>A true or false value, as a cell and an export say it.</summary>
    public const string YES = "Yes";

    public const string NO = "No";

    /// <summary>The page a link is on, and where it leads, before one of the URL grid's column names: "Source URL", "Target status".</summary>
    private const string SOURCE = "Source";

    private const string TARGET = "Target";

    private static readonly Dictionary<string, string> URL_TITLES = new(StringComparer.Ordinal)
    {
        ["id"] = "Crawl order",
        ["url"] = "URL",
        ["fetched"] = "Crawled",
        ["isInternal"] = "On this site",
        ["source"] = "Found by",
        ["depth"] = "Depth",
        ["type"] = "Type",
        ["statusCode"] = "Status",
        ["statusClass"] = "Status class",
        ["outcome"] = "Outcome",
        ["redirectTarget"] = "Redirects to",
        ["redirectTargetStatusCode"] = "Redirect’s status",
        ["indexable"] = "Indexable",
        ["indexability"] = "Indexability",
        ["noindex"] = "Noindex",
        ["nofollow"] = "Nofollow",
        ["metaRobots"] = "Meta robots",
        ["xRobotsTag"] = "X-Robots-Tag",
        ["canonicalUrl"] = "Canonical",
        ["contentType"] = "Content type",
        ["title"] = "Title",
        ["description"] = "Description",
        ["h1"] = "H1",
        ["h1Count"] = "H1s",
        ["wordCount"] = "Word count",
        ["outlinks"] = "Outlinks",
        ["sizeBytes"] = "Size",
        ["responseMs"] = "Response time",
        ["ttfbMs"] = "Time to first byte",
        ["titleLength"] = "Title length",
        ["descriptionLength"] = "Description length",
        ["titleWidth"] = "Title width",
        ["descriptionWidth"] = "Description width",
        ["titleInResults"] = "Title in results",
        ["descriptionInResults"] = "Description in results",
        ["finalUrl"] = "Final URL",
        ["finalStatusCode"] = "Final status",
        ["inSitemap"] = "In a sitemap",
        ["inlinks"] = "Inlinks",
        ["followedInlinks"] = "Followed inlinks",
        ["nofollowInlinks"] = "Nofollow inlinks",
        ["linkingPages"] = "Linking pages",
        ["usedOn"] = "Used on pages",
        ["pixelWidth"] = "Image width",
        ["pixelHeight"] = "Image height",
        ["fetchError"] = "Fetch error",
        ["redirectChain"] = "Redirect chain",
        ["robotsRule"] = "robots.txt rule",
        ["issues"] = "Issues",
        ["errors"] = "Errors",
        ["warnings"] = "Warnings",
        ["notices"] = "Notices",
        ["detail"] = "Detail",
        ["relatedUrlId"] = "Related URL’s crawl order",
        ["relatedUrl"] = "Related URL",
        ["relatedStatusCode"] = "Related URL’s status"
    };

    private static readonly Dictionary<string, string> LINK_TITLES = new(StringComparer.Ordinal)
    {
        ["id"] = "Crawl order",
        ["kind"] = "Kind",
        ["text"] = "Anchor or alt text",
        ["position"] = "Position",
        ["noFollow"] = "Nofollow",
        ["sponsored"] = "Sponsored",
        ["ugc"] = "UGC",
        ["imageLink"] = "Image link",
        ["selfLink"] = "Links to its own page",
        ["followed"] = "Followed",
        ["fragment"] = "Fragment"
    };

    private static readonly Dictionary<string, string> UNITS = new(StringComparer.Ordinal)
    {
        ["sizeBytes"] = "bytes",
        ["responseMs"] = "ms",
        ["ttfbMs"] = "ms",
        ["pixelWidth"] = "px",
        ["pixelHeight"] = "px",
        ["titleWidth"] = "px",
        ["descriptionWidth"] = "px"
    };

    private static readonly Dictionary<string, Dictionary<string, string>> VALUES = new(StringComparer.Ordinal)
    {
        ["source"] = new(StringComparer.Ordinal)
        {
            ["seed"] = "The start URL",
            ["link"] = "A link",
            ["sitemap"] = "A sitemap",
            ["siteVersion"] = "A probe of the site’s other versions"
        },
        ["type"] = new(StringComparer.Ordinal)
        {
            ["page"] = "Page",
            ["image"] = "Image",
            ["script"] = "Script",
            ["stylesheet"] = "Stylesheet",
            ["font"] = "Font",
            ["media"] = "Media file",
            ["pdf"] = "PDF",
            ["other"] = "File"
        },
        ["statusClass"] = new(StringComparer.Ordinal)
        {
            ["2xx"] = "2xx",
            ["3xx"] = "3xx",
            ["4xx"] = "4xx",
            ["5xx"] = "5xx",
            ["other"] = "Another code",
            ["noResponse"] = "No response"
        },
        ["outcome"] = new(StringComparer.Ordinal)
        {
            ["success"] = "Answered",
            ["redirect"] = "Redirect",
            ["clientError"] = "Client error",
            ["serverError"] = "Server error",
            ["blocked"] = "Blocked",
            ["robotsDisallowed"] = "Blocked by robots.txt",
            ["timeout"] = "Timed out",
            ["networkError"] = "Network error",
            ["processingError"] = "Couldn’t be read",
            ["notChecked"] = "Not checked"
        },
        ["indexability"] = new(StringComparer.Ordinal)
        {
            ["none"] = "Indexable",
            ["noindex"] = "Noindex",
            ["canonicalised"] = "Canonicalised",
            ["redirected"] = "Redirected",
            ["clientError"] = "Client error",
            ["serverError"] = "Server error",
            ["blockedByRobots"] = "Blocked by robots.txt",
            ["blocked"] = "Blocked",
            ["unreachable"] = "Unreachable"
        },
        ["kind"] = new(StringComparer.Ordinal)
        {
            ["hyperlink"] = "Hyperlink",
            ["image"] = "Image",
            ["script"] = "Script",
            ["stylesheet"] = "Stylesheet",
            ["iframe"] = "Frame",
            ["media"] = "Media",
            ["icon"] = "Icon",
            ["canonical"] = "Canonical",
            ["hreflang"] = "Hreflang",
            ["pagination"] = "Pagination",
            ["metaRefresh"] = "Meta refresh",
            ["redirect"] = "Redirect"
        },
        ["position"] = new(StringComparer.Ordinal)
        {
            ["content"] = "Content",
            ["navigation"] = "Navigation",
            ["header"] = "Header",
            ["footer"] = "Footer",
            ["sidebar"] = "Sidebar",
            ["head"] = "Head"
        }
    };

    /// <summary>A URL grid column with its words: its title, its unit and its values' words.</summary>
    public static UrlColumnInfo Url(UrlColumnInfo column) => column with
    {
        Title = URL_TITLES.GetValueOrDefault(column.Id) ?? FromId(column.Id),
        Unit = UNITS.GetValueOrDefault(column.Id),
        ValueTitles = Values(column.Id, column.Values)
    };

    /// <summary>One of a link's own columns with its words.</summary>
    public static UrlColumnInfo Link(UrlColumnInfo column) => column with
    {
        Title = LINK_TITLES.GetValueOrDefault(column.Id) ?? FromId(column.Id),
        ValueTitles = Values(column.Id, column.Values)
    };

    /// <summary>
    /// A URL grid column as one end of a link has it: the column's own words under "Source" or "Target" ("Target status",
    /// "Source URL", "Target H1s"). An end's id is named for what it stands for, its place in crawl order.
    /// </summary>
    public static UrlColumnInfo End(UrlColumnInfo column, bool target)
    {
        var worded = Url(column);
        return worded with { Title = $"{(target ? TARGET : SOURCE)} {MidPhrase(worded.Title)}" };
    }

    /// <summary>What became of a request, as the outcome column's cells say it: "Timed out", "Blocked by robots.txt".</summary>
    public static string OutcomeTitle(UrlOutcome outcome)
    {
        var name = outcome.ToString();
        var value = char.ToLowerInvariant(name[0]) + name[1..];
        return VALUES["outcome"].GetValueOrDefault(value) ?? FromId(value);
    }

    /// <summary>An enum's values in words, each value that has none standing for itself.</summary>
    private static Dictionary<string, string> Values(string column, IReadOnlyList<string> values)
    {
        var words = VALUES.GetValueOrDefault(column);
        return values.ToDictionary(value => value, value => words?.GetValueOrDefault(value) ?? FromId(value), StringComparer.Ordinal);
    }

    /// <summary>A title as it reads after another word: "status", but "URL", "H1s" and "X-Robots-Tag" as they are.</summary>
    private static string MidPhrase(string title) =>
        title.Length > 1 && (char.IsUpper(title[1]) || char.IsDigit(title[1]) || title[1] == '-') ? title : char.ToLowerInvariant(title[0]) + title[1..];

    /// <summary>Words from an id this list doesn't have, so a new column never reaches a header as an identifier: "responseMs" is "Response ms".</summary>
    private static string FromId(string id)
    {
        var words = new System.Text.StringBuilder(id.Length + 4);
        for (var index = 0; index < id.Length; index++)
        {
            var character = id[index];
            if (index > 0 && char.IsUpper(character) && (char.IsLower(id[index - 1]) || char.IsDigit(id[index - 1])))
            {
                words.Append(' ');
            }

            words.Append(index == 0 ? char.ToUpperInvariant(character) : char.ToLowerInvariant(character));
        }

        return words.ToString();
    }
}
