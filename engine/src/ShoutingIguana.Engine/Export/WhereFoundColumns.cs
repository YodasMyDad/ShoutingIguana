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
    public static ExportExtraColumns? For(ExportRun run, string? checkId) => (checkId is null ? null : IssueCatalog.Find(checkId)) switch
    {
        { References: IssueReferences.Sources } => new ExportExtraColumns(["Linked from (pages)", "Linked from", "Listed in sitemaps"], ids => Sources(run, ids)),
        { References: IssueReferences.Targets } check => TargetColumns(run, check),
        _ => null
    };

    /// <summary>
    /// A check whose pages hold the bad references: named by what they are ("Images", "Canonical", "Alternates"), how many
    /// each page's issue names and the first of them, each with what it is and how the page links to it. A canonical check
    /// has one column, a page having one canonical; <c>image-too-large</c> also the largest image's size, as a number to
    /// sort the sheet by.
    /// </summary>
    private static ExportExtraColumns TargetColumns(ExportRun run, IssueDefinition check)
    {
        var noun = check.Category switch
        {
            IssueCategory.Images => "Images",
            IssueCategory.Hreflang => "Alternates",
            IssueCategory.Security => "HTTP resources",
            _ => "Links to"
        };

        if (check.Category == IssueCategory.Canonicals)
        {
            return new ExportExtraColumns(["Canonical"], ids => Targets(run, check, ids, page => [Joined([.. page.Targets.Select(target => Target(check, target))], page.Total - page.Targets.Count)]));
        }

        if (check == IssueCatalog.IMAGE_TOO_LARGE)
        {
            return new ExportExtraColumns([$"{noun} (addresses)", noun, "Largest image (bytes)"], ids => Targets(run, check, ids, page =>
            [
                page.Total,
                Joined([.. page.Targets.Select(target => Target(check, target))], page.Total - page.Targets.Count),
                page.Targets.Select(target => target.Facts?.SizeBytes).Max()
            ]));
        }

        return new ExportExtraColumns([$"{noun} (addresses)", noun], ids => Targets(run, check, ids, page =>
            [page.Total, Joined([.. page.Targets.Select(target => Target(check, target))], page.Total - page.Targets.Count)]));
    }

    private static IReadOnlyDictionary<int, object?[]> Sources(ExportRun run, IReadOnlyList<int> ids) =>
        FoundOnReader.ReadFoundOn(run.CrawlPath, ids, LISTED, run.Settings.Cache, run.Token).ToDictionary(url => url.UrlId, url => new object?[]
        {
            url.Pages,
            Joined([.. Pages(url), .. url.Start ? ["The start address"] : Array.Empty<string>()], url.Pages - url.Sources.Select(source => source.Id).Distinct().Count()),
            Joined([.. url.Sitemaps.Sitemaps.Select(sitemap => sitemap.Url)], url.Sitemaps.Total - url.Sitemaps.Sitemaps.Count)
        });

    private static IReadOnlyDictionary<int, object?[]> Targets(ExportRun run, IssueDefinition check, IReadOnlyList<int> ids, Func<IssueTargetsOfUrl, object?[]> cells) =>
        (FoundOnReader.ReadTargets(run.CrawlPath, check.Id, ids, LISTED, run.Settings.Cache, run.Token) ?? []).ToDictionary(page => page.UrlId, cells);

    /// <summary>Each page linking to the URL once, with where on it its first link is and the link's text.</summary>
    private static IEnumerable<string> Pages(UrlFoundOn url) => url.Sources.GroupBy(source => source.Id).Select(links =>
        Place(links.First()) is { } place ? $"{links.First().Url} ({place})" : links.First().Url);

    /// <summary>Where on its page a link is, what kind it is when not a hyperlink, and its text: "footer, “Old range”"; null with none of them.</summary>
    internal static string? Place(LinkRow link)
    {
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

        return what.Count == 0 ? null : string.Join(", ", what);
    }

    /// <summary>
    /// A URL a page's issue names, with what it is and how the page links to it, as a cell and the report say it:
    /// "https://x/old (301 → https://x/new/ 200; content, “Shop”)", "https://x/hero.jpg (200; 1.8 MB, image/jpeg, 4000×3000 px,
    /// shown at 400×300; content, alt “Sofa”)".
    /// </summary>
    internal static string Target(IssueDefinition check, IssueTarget target)
    {
        return $"{TargetUrl(check, target)} ({TargetFacts(check, target)})";
    }

    /// <summary>What a target is and how the page links to it, without its URL: "301 → https://x/new/ 200; content, “Shop”".</summary>
    internal static string TargetFacts(IssueDefinition check, IssueTarget target)
    {
        List<string> parts = [StatusOf(target.StatusCode, target.Outcome) + (target.Facts?.Final is { } final ? $" → {final.Url} {StatusOf(final.StatusCode, final.Outcome)}" : string.Empty)];
        if (check.Category == IssueCategory.Images)
        {
            parts.AddRange(ImageFacts(check, target));
        }
        else
        {
            if (target.Facts?.Indexability is { } reason and not IndexabilityReason.None and not IndexabilityReason.Redirected)
            {
                parts.Add(target.Facts.Canonical is { } canonical ? $"canonicalised to {canonical.Url}" : ReasonWord(reason));
            }

            if (check.TargetFacts.Contains("responseMs") && target.Facts?.ResponseMs is { } responseMs)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"answered in {responseMs / 1000d:0.0} s"));
            }

            List<string> link = [];
            if (check == IssueCatalog.MIXED_CONTENT && target.Kind is { } kind)
            {
                link.Add($"{KindWord(kind)} in the {PositionWord(target.Position ?? LinkPosition.Content)}");
            }
            else if (check.Category is IssueCategory.Links && target.Position is { } position)
            {
                link.Add(PositionWord(position));
            }

            if (target.Flags.HasFlag(LinkFlags.ImageLink))
            {
                link.Add("image link");
            }

            if (target.Flags.HasFlag(LinkFlags.FromHttpHeader))
            {
                link.Add("from the HTTP header");
            }

            if (!string.IsNullOrWhiteSpace(target.Text) && check.Category is not IssueCategory.Canonicals)
            {
                link.Add($"“{OneLine(target.Text)}”");
            }

            if (link.Count > 0)
            {
                parts.Add(string.Join(", ", link));
            }
        }

        return string.Join("; ", parts.Where(part => part.Length > 0));
    }

    /// <summary>A target's URL as a cell shows it: with the missing #fragment for a broken fragment link.</summary>
    internal static string TargetUrl(IssueDefinition check, IssueTarget target) =>
        target.Fragment is { } fragment && check == IssueCatalog.BROKEN_FRAGMENT_LINKS ? $"{target.Url}#{fragment}" : target.Url;

    /// <summary>An image's file and how the page shows it: "1.8 MB, image/jpeg, 4000×3000 px, shown at 400×300", "content, alt “Sofa”", "no alt".</summary>
    private static IEnumerable<string> ImageFacts(IssueDefinition check, IssueTarget target)
    {
        List<string> file = [];
        if (target.Facts?.SizeBytes is { } size && check == IssueCatalog.IMAGE_TOO_LARGE)
        {
            file.Add(Analysis.Rules.RuleText.Size(size));
        }

        if (target.Facts?.ContentType is { } type && check == IssueCatalog.IMAGE_TOO_LARGE)
        {
            file.Add(type);
        }

        if (target.Facts is { PixelWidth: { } width, PixelHeight: { } height })
        {
            file.Add(string.Create(CultureInfo.InvariantCulture, $"{width}×{height} px"));
        }

        if (target.Img is { Width: { } shownWidth, Height: { } shownHeight })
        {
            file.Add($"shown at {shownWidth}×{shownHeight}");
        }
        else if (target.Img is { } img && check == IssueCatalog.IMAGE_MISSING_DIMENSIONS)
        {
            file.Add(img.Width is null && img.Height is null ? "no width or height" : img.Width is null ? "no width" : "no height");
        }

        if (file.Count > 0)
        {
            yield return string.Join(", ", file);
        }

        List<string> shown = [];
        if (target.Position is { } position)
        {
            shown.Add(PositionWord(position));
        }

        shown.Add(target.Img switch
        {
            { HasAlt: false } => "no alt",
            { Alt: { Length: > 0 } alt } => $"alt “{OneLine(alt)}”",
            null when !string.IsNullOrWhiteSpace(target.Text) => $"alt “{OneLine(target.Text)}”",
            _ => string.Empty
        });
        if (shown.Any(part => part.Length > 0))
        {
            yield return string.Join(", ", shown.Where(part => part.Length > 0));
        }
    }

    private static string StatusOf(int? statusCode, UrlOutcome? outcome) =>
        statusCode is { } code ? code.ToString(CultureInfo.InvariantCulture) : outcome is { } known ? OutcomeWord(known) : "not checked";

    private static string ReasonWord(IndexabilityReason reason) => reason switch
    {
        IndexabilityReason.Noindex => "noindex",
        IndexabilityReason.Canonicalised => "canonicalised",
        IndexabilityReason.BlockedByRobots => "blocked by robots.txt",
        IndexabilityReason.Blocked => "blocked",
        IndexabilityReason.Unreachable => "no answer",
        _ => "not indexable"
    };

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
