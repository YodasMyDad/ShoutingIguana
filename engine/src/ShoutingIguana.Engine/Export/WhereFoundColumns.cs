using System.Globalization;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Export;

/// <summary>
/// Where to fix each URL a check's file lists, as columns after its own, so a sheet handed to a developer says what to
/// change. A check whose URLs other pages refer to (<see cref="IssueReferences.Sources"/>: a 404, a redirect) gets how
/// many pages link to each, the first of them (each with where on the page the link is and its text), and the sitemaps
/// listing it; a check whose pages hold the bad references (<see cref="IssueReferences.Targets"/>) gets how many URLs each
/// page's issue names and the first of them with their status. Every other one gets nothing. The full list of the links
/// to one URL is the Links screen's: these name the first <see cref="LISTED"/>, then how many more.
/// </summary>
internal static class WhereFoundColumns
{
    /// <summary>The references named in a cell, before "and N more".</summary>
    public const int LISTED = 10;

    /// <summary>What sits between two references in one cell.</summary>
    private const string SEPARATOR = "; ";

    /// <summary>The columns for a check's file; null for a check that refers to nothing, or one the engine doesn't know.</summary>
    public static ExportExtraColumns? For(ExportRun run, string? checkId) => (checkId is null ? null : IssueCatalog.Find(checkId))?.References switch
    {
        IssueReferences.Sources => new ExportExtraColumns(["Linked from (pages)", "Linked from", "Listed in sitemaps"], ids => Sources(run, ids)),
        IssueReferences.Targets => new ExportExtraColumns(["Links to (addresses)", "Links to"], ids => Targets(run, checkId!, ids)),
        _ => null
    };

    private static IReadOnlyDictionary<int, object?[]> Sources(ExportRun run, IReadOnlyList<int> ids) =>
        FoundOnReader.ReadFoundOn(run.CrawlPath, ids, LISTED, run.Settings.Cache, run.Token).ToDictionary(url => url.UrlId, url => new object?[]
        {
            url.Pages,
            Joined([.. Pages(url), .. url.Start ? ["The start address"] : Array.Empty<string>()], url.Pages - url.Sources.Select(source => source.Id).Distinct().Count()),
            Joined([.. url.Sitemaps.Sitemaps.Select(sitemap => sitemap.Url)], url.Sitemaps.Total - url.Sitemaps.Sitemaps.Count)
        });

    private static IReadOnlyDictionary<int, object?[]> Targets(ExportRun run, string checkId, IReadOnlyList<int> ids) =>
        (FoundOnReader.ReadTargets(run.CrawlPath, checkId, ids, LISTED, run.Settings.Cache, run.Token) ?? []).ToDictionary(page => page.UrlId, page => new object?[]
        {
            page.Total,
            Joined([.. page.Targets.Select(Target)], page.Total - page.Targets.Count)
        });

    /// <summary>Each page linking to the URL once, with where on it its first link is and the link's text.</summary>
    private static IEnumerable<string> Pages(UrlFoundOn url) => url.Sources.GroupBy(source => source.Id).Select(links =>
    {
        var link = links.First();
        List<string> what = [];
        if (link.Kind is { } kind and not LinkKind.Hyperlink)
        {
            what.Add(KindWord(kind));
        }

        if (link.Position is { } position)
        {
            what.Add(PositionWord(position));
        }

        if (!string.IsNullOrWhiteSpace(link.Text))
        {
            what.Add($"“{OneLine(link.Text)}”");
        }

        return what.Count == 0 ? link.Url : $"{link.Url} ({string.Join(", ", what)})";
    });

    private static string Target(IssueTarget target)
    {
        var status = target.StatusCode is { } code ? code.ToString(CultureInfo.InvariantCulture) : target.Outcome is { } outcome ? OutcomeWord(outcome) : null;
        return status is null ? target.Url : $"{target.Url} ({status})";
    }

    /// <summary>The references as one cell, then how many more there are; nothing where there are none.</summary>
    private static string? Joined(IReadOnlyList<string> items, long more) => items.Count == 0
        ? null
        : string.Join(SEPARATOR, items) + (more > 0 ? string.Create(CultureInfo.InvariantCulture, $"{SEPARATOR}and {more:N0} more") : string.Empty);

    private static string KindWord(LinkKind kind) => kind switch
    {
        LinkKind.Image => "image",
        LinkKind.Script => "script",
        LinkKind.Stylesheet => "stylesheet",
        LinkKind.Iframe => "iframe",
        LinkKind.Media => "media",
        LinkKind.Icon => "icon",
        LinkKind.Canonical => "canonical",
        LinkKind.Hreflang => "hreflang",
        LinkKind.Pagination => "pagination",
        LinkKind.MetaRefresh => "meta refresh",
        LinkKind.Redirect => "redirect",
        _ => "link"
    };

    private static string PositionWord(LinkPosition position) => position switch
    {
        LinkPosition.Navigation => "navigation",
        LinkPosition.Header => "header",
        LinkPosition.Footer => "footer",
        LinkPosition.Sidebar => "sidebar",
        LinkPosition.Head => "head",
        _ => "content"
    };

    private static string OutcomeWord(UrlOutcome outcome) => outcome switch
    {
        UrlOutcome.RobotsDisallowed => "blocked by robots.txt",
        UrlOutcome.Timeout => "timed out",
        UrlOutcome.NetworkError => "no connection",
        UrlOutcome.Blocked => "blocked",
        UrlOutcome.NotChecked => "not checked",
        UrlOutcome.ProcessingError => "couldn't be read",
        _ => "no answer"
    };

    /// <summary>A link's text on one line, its runs of white space one space.</summary>
    private static string OneLine(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
