using System.Globalization;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// How a page links out and how far in it is: links search engines can't follow, files it loads that are broken, links to
/// private addresses, too few links onward or too many, links whose text says nothing, links to slow pages elsewhere, and
/// a page many clicks deep. Only <c>&lt;a href&gt;</c> links count as links here, not a head's <c>&lt;link&gt;</c>.
/// </summary>
/// <param name="readUrl">The stored URL with an id, or null: a detail names the first private address linked.</param>
/// <param name="start">
/// Where the crawl started. On a site at a private address, as a development copy on the machine is, that machine's own
/// addresses (the same host, or loopback for loopback) are its neighbours, not a fault.
/// </param>
internal sealed class LinkStructureRules(AnalysisSettings settings, UrlTable urls, Func<int, string?> readUrl, Uri? start = null) : IPageRule
{
    private readonly string? _privateStart = start is not null && PrivateNetworkGuard.IsPrivateLiteral(start) ? MachineOf(start) : null;

    /// <summary>Link texts that say nothing of where they go (Google's "too generic" examples, and their like), as normalised.</summary>
    private static readonly HashSet<string> GENERIC_TEXTS = new(
    [
        "click here", "click", "here", "read more", "more", "learn more", "find out more", "see more", "view more", "more info",
        "more information", "continue", "continue reading", "details", "link", "this", "this page", "this link", "go", "website",
        "article"
    ], StringComparer.Ordinal);

    private const int EXAMPLE_TEXT_MAX = 30;

    private static readonly LinkKind[] FILE_KINDS = [LinkKind.Script, LinkKind.Stylesheet, LinkKind.Iframe, LinkKind.Media];

    public IEnumerable<IssueHit> Check(AnalyzedPage page)
    {
        if (page.Facts.UncrawlableLinks is { Count: > 0 } uncrawlable)
        {
            // Each says where on the page it is, as a page parsed since positions were noted gives it.
            var examples = uncrawlable.Examples.Select(example =>
                $"{(example.Text.Length > 0 ? RuleText.Quote(example.Text, EXAMPLE_TEXT_MAX) : "no text")} " +
                $"({example.Why}{(example.Position is { } position ? $", {PositionWord(position)}" : string.Empty)})");
            var more = uncrawlable.Count > uncrawlable.Examples.Count ? " …" : string.Empty;
            yield return new IssueHit(IssueCatalog.UNCRAWLABLE_LINKS, page.UrlId,
                RuleText.Fit($"{RuleText.Count(uncrawlable.Count, "link", "links")}: {string.Join(", ", examples)}{more}"));
        }

        if (BrokenFiles(page) is { Count: > 0 } broken)
        {
            yield return new IssueHit(IssueCatalog.BROKEN_RESOURCES, page.UrlId,
                RuleText.BrokenTargets(urls, broken, "broken file", "broken files"), broken.First) { Targets = broken.Ids };
        }

        if (PrivateAddresses(page) is ({ Count: > 0 } addresses, var links) && readUrl(addresses.First!.Value) is { } first)
        {
            yield return new IssueHit(IssueCatalog.LINKS_TO_LOCALHOST, page.UrlId,
                RuleText.Fit($"{RuleText.Count(links, "link", "links")}, such as {first}"), addresses.First) { Targets = addresses.Ids };
        }

        var hyperlinks = page.Links.Where(link => link is { Kind: LinkKind.Hyperlink } && link.Position != LinkPosition.Head).ToList();
        if (!page.IsIndexable)
        {
            if (GenericTexts(page, hyperlinks) is { } genericHit)
            {
                yield return genericHit;
            }

            yield break;
        }

        // Distinct pages of the site linked, the page itself and its sections aside.
        var onward = hyperlinks.Where(link => link.ToId != page.UrlId && !link.Flags.HasFlag(LinkFlags.SamePage) && urls[link.ToId].IsInternal)
            .Select(link => link.ToId).Distinct().Count();
        if (onward < settings.MinInternalOutlinks)
        {
            yield return new IssueHit(IssueCatalog.FEW_INTERNAL_OUTLINKS, page.UrlId, onward == 0
                ? "No links to other pages"
                : string.Create(CultureInfo.InvariantCulture,
                    $"{RuleText.Count(onward, "link to another page", "links to other pages")}, under {settings.MinInternalOutlinks:N0}"));
        }

        if (GenericTexts(page, hyperlinks) is { } generic)
        {
            yield return generic;
        }

        if (page.Depth > settings.MaxClickDepth && page.Depth < CrawlDepth.UNLINKED)
        {
            yield return new IssueHit(IssueCatalog.DEEP_PAGE, page.UrlId,
                string.Create(CultureInfo.InvariantCulture, $"{RuleText.Count(page.Depth, "click", "clicks")} from the start, over {settings.MaxClickDepth:N0}"));
        }

        var internalLinks = hyperlinks.Count(link => urls[link.ToId].IsInternal);
        var externalLinks = hyperlinks.Count - internalLinks;
        List<string> tooMany = [];
        if (internalLinks > settings.MaxInternalLinks)
        {
            tooMany.Add(RuleText.Over(RuleText.Count(internalLinks, "internal link", "internal links"), settings.MaxInternalLinks));
        }

        if (externalLinks > settings.MaxExternalLinks)
        {
            tooMany.Add(RuleText.Over(RuleText.Count(externalLinks, "link to other sites", "links to other sites"), settings.MaxExternalLinks));
        }

        if (tooMany.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.TOO_MANY_LINKS, page.UrlId, string.Join("; ", tooMany));
        }

        if (SlowExternal(hyperlinks) is ({ Count: > 0 } slow, var slowestMs))
        {
            yield return new IssueHit(IssueCatalog.SLOW_EXTERNAL_LINKS, page.UrlId,
                string.Create(CultureInfo.InvariantCulture, $"{RuleText.Count(slow.Count, "slow link", "slow links")}, the slowest {slowestMs / 1000d:0.0} s"),
                slow.First) { Targets = slow.Ids };
        }
    }

    /// <summary>The scripts, stylesheets, frames and media the page loads that are broken, through redirects as broken links are.</summary>
    private TargetSet BrokenFiles(AnalyzedPage page)
    {
        TargetSet broken = new();
        foreach (var link in page.Links)
        {
            if (FILE_KINDS.Contains(link.Kind) && link.ToId != page.UrlId && urls.IsBrokenThroughRedirects(link.ToId))
            {
                broken.Add(link.ToId);
            }
        }

        return broken;
    }

    /// <summary>The private addresses on other hosts the page links to or loads from, and how many links lead there.</summary>
    private (TargetSet Addresses, int Links) PrivateAddresses(AnalyzedPage page)
    {
        TargetSet addresses = new();
        var links = 0;
        foreach (var link in page.Links)
        {
            if (urls[link.ToId] is { IsInternal: false, IsPrivateHost: true } && !IsStartsOwnMachine(link.ToId))
            {
                addresses.Add(link.ToId);
                links++;
            }
        }

        return (addresses, links);
    }

    /// <summary>The private address is the machine a crawl of a private address started on: its host, or loopback for any loopback name.</summary>
    private bool IsStartsOwnMachine(int id) =>
        _privateStart is not null && readUrl(id) is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri) && MachineOf(uri) == _privateStart;

    /// <summary>The machine a private address names: "loopback" for localhost, its subdomains, 127.x and ::1; else the host as written.</summary>
    private static string MachineOf(Uri uri) =>
        uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            ? "loopback"
            : uri.Host.ToLowerInvariant();

    /// <summary>The page's links to its own site whose whole text is generic: "3 links: "Read more" ×2, "here"".</summary>
    private IssueHit? GenericTexts(AnalyzedPage page, List<StoredLink> hyperlinks)
    {
        TargetSet targets = new();
        List<(string Written, int Times)> texts = [];
        var count = 0;
        foreach (var link in hyperlinks)
        {
            if (link.ToId == page.UrlId || !urls[link.ToId].IsInternal || link.Text is not { } text || !IsGeneric(text, link.Flags))
            {
                continue;
            }

            count++;
            targets.Add(link.ToId);
            var at = texts.FindIndex(seen => Normalise(seen.Written) == Normalise(text));
            if (at < 0)
            {
                texts.Add((text, 1));
            }
            else
            {
                texts[at] = (texts[at].Written, texts[at].Times + 1);
            }
        }

        if (count == 0)
        {
            return null;
        }

        var listed = texts.Select(text => text.Times > 1
            ? string.Create(CultureInfo.InvariantCulture, $"{RuleText.Quote(text.Written, EXAMPLE_TEXT_MAX)} ×{text.Times}")
            : RuleText.Quote(text.Written, EXAMPLE_TEXT_MAX));
        return new IssueHit(IssueCatalog.GENERIC_ANCHOR_TEXT, page.UrlId, RuleText.Fit($"{RuleText.Count(count, "link", "links")}: {string.Join(", ", listed)}"),
            targets.First) { Targets = targets.Ids };
    }

    /// <summary>
    /// Whether a hyperlink's text says nothing of where it goes, as the check judges it: one of the generic texts, written
    /// on the link itself (an image link's alt text and a jump within the page don't count).
    /// </summary>
    internal static bool IsGeneric(string text, LinkFlags flags) =>
        !flags.HasFlag(LinkFlags.ImageLink) && !flags.HasFlag(LinkFlags.SamePage) && GENERIC_TEXTS.Contains(Normalise(text));

    /// <summary>Where on the page a link is, as a detail says it: "header".</summary>
    private static string PositionWord(LinkPosition position) => position switch
    {
        LinkPosition.Navigation => "navigation",
        LinkPosition.Header => "header",
        LinkPosition.Footer => "footer",
        LinkPosition.Sidebar => "sidebar",
        LinkPosition.Head => "head",
        _ => "content"
    };

    /// <summary>Lowercase, with the spaces and punctuation round it taken off: "Read more…" and "Read more →" are "read more".</summary>
    private static string Normalise(string text) =>
        string.Join(' ', text.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Trim([' ', .. text.Where(character => char.IsPunctuation(character) || char.IsSymbol(character)).Distinct()]);

    /// <summary>The pages on other sites the page links to that answered, with a page or a redirect, slower than the limit; and the slowest time.</summary>
    private (TargetSet Slow, int SlowestMs) SlowExternal(List<StoredLink> hyperlinks)
    {
        TargetSet slow = new();
        var slowest = 0;
        foreach (var link in hyperlinks)
        {
            if (urls[link.ToId] is { IsInternal: false, Fetched: true, Outcome: FetchOutcome.Success or FetchOutcome.Redirect } target &&
                target.ResponseMs > settings.SlowExternalLinkMs)
            {
                slow.Add(link.ToId);
                slowest = Math.Max(slowest, target.ResponseMs);
            }
        }

        return (slow, slowest);
    }
}
