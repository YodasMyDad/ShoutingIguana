using System.Globalization;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Export;

/// <summary>
/// Where to fix each URL a check's file lists, as columns after its own, so a sheet handed to a developer says what to
/// change. A check whose URLs other pages refer to (<see cref="IssueReferences.Sources"/>: a 404, a redirect) gets the
/// first page that links to each in cells of its own (the page, where on it the link is, the link's text: a sheet can be
/// sorted and filtered by the page), how many pages link to it, the others of them in one cell, and the sitemaps listing
/// it; for a check fixed at the link (<see cref="FixedAtTheLink"/>) the page's three cells lead the row; a check whose pages hold the bad references (<see cref="IssueReferences.Targets"/>) gets how many URLs each
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
        { References: IssueReferences.Sources } check => new ExportExtraColumns(SOURCE_TITLES, ids => Sources(run, ids)) { Leading = FixedAtTheLink(check) ? 3 : 0 },
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
        var noun = check == IssueCatalog.PROTOCOL_RELATIVE_LINKS
            ? "URLs without a scheme"
            : check.Category switch
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

    /// <summary>
    /// The columns of a check whose URLs other pages refer to. The first three are the first page that links to the URL, in
    /// cells of their own: the UX audit's S-05 found the page, the place and the link's text in one cell ("https://x/blog/
    /// (content, “Buy Now”)"), which can't be sorted or filtered by page.
    /// </summary>
    internal static readonly IReadOnlyList<string> SOURCE_TITLES =
        ["Page that links to it", "Where on the page", "Link text", "Linked from (pages)", "Other pages that link to it", "Listed in sitemaps"];

    /// <summary>
    /// The checks of addresses whose fix is at the link to them: a missing page is mended by changing the link on the page
    /// that holds it, so that page leads its row, in a sheet as in the report. Any other check of addresses is fixed at the
    /// address itself (a server error, a chain of redirects), which leads.
    /// </summary>
    internal static bool FixedAtTheLink(IssueDefinition check) => check == IssueCatalog.INTERNAL_CLIENT_ERROR;

    private static IReadOnlyDictionary<int, object?[]> Sources(ExportRun run, IReadOnlyList<int> ids) =>
        FoundOnReader.ReadFoundOn(run.CrawlPath, ids, LISTED, run.Settings.Cache, run.Token).ToDictionary(url => url.UrlId, url =>
        {
            // Each page once, by its first link; the first of them apart, the others as one cell says them.
            List<LinkRow> pages = [.. url.Sources.GroupBy(source => source.Id).Select(links => links.First())];
            var first = pages.FirstOrDefault();
            return new object?[]
            {
                first?.Url,
                first is null ? null : Where(first),
                first is null || string.IsNullOrWhiteSpace(first.Text) ? null : OneLine(first.Text),
                url.Pages,
                Joined([.. pages.Skip(1).Select(Page), .. url.Start ? ["The start address"] : Array.Empty<string>()], url.Pages - pages.Count),
                Joined([.. url.Sitemaps.Sitemaps.Select(sitemap => sitemap.Url)], url.Sitemaps.Total - url.Sitemaps.Sitemaps.Count)
            };
        });

    private static IReadOnlyDictionary<int, object?[]> Targets(ExportRun run, IssueDefinition check, IReadOnlyList<int> ids, Func<IssueTargetsOfUrl, object?[]> cells) =>
        (FoundOnReader.ReadTargets(run.CrawlPath, check.Id, ids, LISTED, run.Settings.Cache, run.Token) ?? []).ToDictionary(page => page.UrlId, cells);

    /// <summary>A page linking to the URL, with where on it its first link is and the link's text.</summary>
    private static string Page(LinkRow link) => Place(link) is { } place ? $"{link.Url} ({place})" : link.Url;

    /// <summary>Where on its page a link is, and what kind it is when not a hyperlink: "footer", "image, content"; null with neither.</summary>
    private static string? Where(LinkRow link)
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

        return what.Count == 0 ? null : string.Join(", ", what);
    }

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

            if (check.TargetFacts.Contains("redirectKind") && RedirectKindWords(target.Facts?.RedirectKind) is { } redirectKind)
            {
                parts.Add(redirectKind);
            }

            if (check.TargetFacts.Contains("responseMs") && target.Facts?.ResponseMs is { } responseMs)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"answered in {responseMs / 1000d:0.0} s"));
            }

            List<string> link = [];
            // What the page loads or links at the URL, and where: a resource of mixed content's, any of a // URL's.
            if ((check == IssueCatalog.MIXED_CONTENT || check == IssueCatalog.PROTOCOL_RELATIVE_LINKS) && target.Kind is { } kind)
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

    /// <summary>What a redirect changes, in words: "HTTP to HTTPS, adds www", "adds a trailing slash", "moves elsewhere".</summary>
    internal static string? RedirectKindWords(string? kinds) => kinds is null
        ? null
        : string.Join(", ", kinds.Split(',').Select(kind => kind switch
        {
            RedirectKinds.HTTP_TO_HTTPS => "HTTP to HTTPS",
            RedirectKinds.HTTPS_TO_HTTP => "HTTPS to HTTP",
            RedirectKinds.ADDS_WWW => "adds www",
            RedirectKinds.REMOVES_WWW => "removes www",
            RedirectKinds.OTHER_HOST => "to another host",
            RedirectKinds.ADDS_SLASH => "adds a trailing slash",
            RedirectKinds.REMOVES_SLASH => "removes the trailing slash",
            RedirectKinds.CHANGES_CASE => "changes case",
            _ => "moves elsewhere"
        }));

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
        LinkKind.SocialImage => "share image",
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
