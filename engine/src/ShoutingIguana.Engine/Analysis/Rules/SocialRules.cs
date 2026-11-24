using System.Globalization;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// Batch 9's Open Graph and X (Twitter) card checks (docs/v2-new-checks.md), on indexable pages, as open-graph-missing:
/// no card for X, a card X can't build, a share image without its full address, an og:url that isn't the page, share
/// settings given twice with different values, and share images that don't load, are too large or too small.
/// </summary>
internal sealed class SocialRules(UrlTable urls) : IPageRule
{
    /// <summary>X's limit for a card image, which Facebook's 8 MB is above.</summary>
    public const long MAX_IMAGE_BYTES = 5L * 1024 * 1024;

    /// <summary>Facebook's minimum share image, either side.</summary>
    private const int OPEN_GRAPH_MIN_SIDE = 200;

    private static readonly string[] CARD_TYPES = ["summary", "summary_large_image", "app", "player"];

    /// <summary>The tags that say one thing each: a second with other content leaves a network to pick.</summary>
    private static readonly string[] SINGLE_TAGS =
        ["og:title", "og:description", "og:url", "og:type", "twitter:card", "twitter:title", "twitter:description"];

    /// <summary>The fewest characters a quoted value is cut to: a word or two of it.</summary>
    private const int MIN_QUOTE = 8;

    private static readonly string[] OPEN_GRAPH_IMAGE_TAGS = ["og:image", "og:image:url", "og:image:secure_url"];

    public IEnumerable<IssueHit> Check(AnalyzedPage page)
    {
        if (!page.IsIndexable)
        {
            yield break;
        }

        var tags = page.Facts.MetaTags;
        var card = First(tags, "twitter:card");
        if (card is null)
        {
            yield return new IssueHit(IssueCatalog.X_CARD_MISSING, page.UrlId);
        }
        else if (CardFaults(tags, card) is { Count: > 0 } faults)
        {
            yield return new IssueHit(IssueCatalog.X_CARD_INCOMPLETE, page.UrlId, RuleText.Fit(string.Join("; ", faults)));
        }

        // An image named by part of its address: a network reads it from nowhere, so it may show none.
        var relative = tags.FirstOrDefault(tag => IsImageTag(tag.Name) && tag.Content.Trim() is { Length: > 0 } value && !IsAbsoluteHttp(value));
        if (relative is not null)
        {
            yield return new IssueHit(IssueCatalog.SOCIAL_IMAGE_RELATIVE, page.UrlId, RuleText.Fit($"{relative.Name} is {RuleText.Cut(relative.Content.Trim(), 80)}"));
        }

        if (First(tags, "og:url") is { } openGraphUrl && !NamesThePage(openGraphUrl, page))
        {
            yield return new IssueHit(IssueCatalog.OG_URL_NOT_CANONICAL, page.UrlId, RuleText.Fit($"og:url is {RuleText.Cut(openGraphUrl.Trim(), 100)}"));
        }

        var repeated = SINGLE_TAGS
            .Select(name => (Name: name, Values: tags.Where(tag => tag.Name == name).Select(tag => Normal(tag.Content)).ToList()))
            .Where(tag => tag.Values.Count > 1 && tag.Values.Distinct(StringComparer.Ordinal).Count() > 1)
            .ToList();
        if (repeated.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.SOCIAL_TAGS_MULTIPLE, page.UrlId, Repeated(repeated));
        }

        foreach (var hit in ImageHits(page, card))
        {
            yield return hit;
        }
    }

    private IEnumerable<IssueHit> ImageHits(AnalyzedPage page, string? card)
    {
        TargetSet broken = new(), large = new(), small = new();
        string? brokenDetail = null, largeDetail = null, smallDetail = null;
        // The image X uses: its own, else Open Graph's. One file can be named by both, each judged by its own minimum.
        var xImage = page.Links.Any(link => link.Kind == LinkKind.SocialImage && link.Text == "twitter:image") ? "twitter:image" : "og:image";
        // A network shows the first image a page names; a builder that lists every picture on the page as an og:image
        // (Weebly) names small ones further down, which nobody is shown. So only each tag's first is measured.
        HashSet<string> measured = new(StringComparer.Ordinal);
        foreach (var link in page.Links.Where(link => link.Kind == LinkKind.SocialImage))
        {
            var tag = link.Text ?? "og:image";
            var isFirst = measured.Add(tag);
            if (urls.IsBrokenThroughRedirects(link.ToId))
            {
                broken.Add(link.ToId);
                brokenDetail ??= $"{tag} {RuleText.BrokenStatus(urls, link.ToId)}";
                continue;
            }

            var file = urls[urls.TraceRedirects(link.ToId).FinalId];
            if (!file.Fetched || file.Outcome != Fetching.Models.FetchOutcome.Success)
            {
                continue;
            }

            // Sent as something else: a page, or another kind of file. One with no type, or a malformed one, a network may
            // still read by its bytes, so it isn't called broken.
            if (file.ContentClass is ContentClass.Html or ContentClass.Script or ContentClass.Stylesheet or ContentClass.Other)
            {
                broken.Add(link.ToId);
                brokenDetail ??= file.ContentClass == ContentClass.Html ? $"{tag} is a page, not an image" : $"{tag} isn't sent as an image";
                continue;
            }

            if (!isFirst)
            {
                continue;
            }

            if (file.SizeBytes is { } size && size > MAX_IMAGE_BYTES)
            {
                large.Add(link.ToId);
                largeDetail ??= $"{tag} is {RuleText.Size(size)}, over {RuleText.LimitSize(MAX_IMAGE_BYTES)}";
            }

            if (file is { PixelWidth: { } width, PixelHeight: { } height } &&
                Minimums(tag, xImage, card).FirstOrDefault(minimum => width < minimum.Width || height < minimum.Height) is { Width: > 0 } under)
            {
                small.Add(link.ToId);
                smallDetail ??= string.Create(CultureInfo.InvariantCulture, $"{tag} is {width:N0} × {height:N0} px, under {under.Width} × {under.Height}{under.Card}");
            }
        }

        if (broken.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.SOCIAL_IMAGE_BROKEN, page.UrlId, RuleText.Fit(brokenDetail!), broken.First) { Targets = broken.Ids };
        }

        if (large.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.SOCIAL_IMAGE_TOO_LARGE, page.UrlId, largeDetail!, large.First) { Targets = large.Ids };
        }

        if (small.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.SOCIAL_IMAGE_TOO_SMALL, page.UrlId, smallDetail!, small.First) { Targets = small.Ids };
        }
    }

    /// <summary>
    /// The least an image needs for each network that uses it, Facebook's first: 200 × 200 for an Open Graph image; for the
    /// image X uses (twitter:image, else og:image), 300 × 157 on a large card and 144 × 144 on a summary card, the card
    /// named in the detail.
    /// </summary>
    internal static IEnumerable<(int Width, int Height, string Card)> Minimums(string tag, string xImage, string? card)
    {
        if (tag.StartsWith("og:", StringComparison.Ordinal))
        {
            yield return (OPEN_GRAPH_MIN_SIDE, OPEN_GRAPH_MIN_SIDE, string.Empty);
        }

        if (tag == xImage || (xImage == "og:image" && tag.StartsWith("og:image", StringComparison.Ordinal)))
        {
            switch (card?.Trim().ToLowerInvariant())
            {
                case "summary_large_image":
                    yield return (300, 157, " (X's large card)");
                    break;
                case "summary":
                    yield return (144, 144, " (X's summary card)");
                    break;
            }
        }
    }

    /// <summary>What keeps X from building the card a page asks for, each as the detail says it.</summary>
    internal static List<string> CardFaults(IReadOnlyList<MetaTagFact> tags, string card)
    {
        List<string> faults = [];
        var type = card.Trim().ToLowerInvariant();
        if (!CARD_TYPES.Contains(type))
        {
            faults.Add($"Card type \"{RuleText.Cut(card.Trim(), 30)}\" isn't one X shows");
        }

        if (First(tags, "twitter:title") is null && First(tags, "og:title") is null)
        {
            faults.Add("No title (twitter:title or og:title)");
        }

        if (type is "summary" or "summary_large_image" && First(tags, "twitter:image") is null && OPEN_GRAPH_IMAGE_TAGS.All(name => First(tags, name) is null))
        {
            faults.Add("No image (twitter:image or og:image)");
        }

        if (type == "player" && First(tags, "twitter:player") is null)
        {
            faults.Add("No twitter:player");
        }

        // X reads its tags by name=; written only as property=, they say nothing to it.
        var twitter = tags.Where(tag => tag.Name.StartsWith("twitter:", StringComparison.Ordinal)).ToList();
        if (twitter.Count > 0 && twitter.All(tag => tag.IsProperty))
        {
            faults.Add("twitter: tags written as property=, which X doesn't read");
        }

        return faults;
    }

    private static bool IsImageTag(string name) => name == "twitter:image" || OPEN_GRAPH_IMAGE_TAGS.Contains(name);

    internal static bool IsAbsoluteHttp(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && !value.StartsWith("//", StringComparison.Ordinal);

    /// <summary>An og:url names the page when it's the page's own URL, resolved against it and normalised.</summary>
    private static bool NamesThePage(string value, AnalyzedPage page) =>
        Uri.TryCreate(page.Uri, value.Trim(), out var named) && named.Scheme is "http" or "https" &&
        UrlNormalizer.Normalize(named) == UrlNormalizer.Normalize(page.Uri);

    /// <summary>A tag's first content that says something, or null.</summary>
    private static string? First(IReadOnlyList<MetaTagFact> tags, string name) =>
        tags.FirstOrDefault(tag => tag.Name == name && !string.IsNullOrWhiteSpace(tag.Content))?.Content;

    /// <summary>
    /// Each tag given more than once, counted, with its different values quoted in page order: "2 og:title: "Sofas" ·
    /// "Chairs"; 2 twitter:card: "summary_large_image" · "summary"". The values share what the detail's 120 characters
    /// leave, short ones whole, so every tag's count is in it (its group is the counts, IssueFaults.RepeatedTags).
    /// </summary>
    private static string Repeated(List<(string Name, List<string> Values)> repeated)
    {
        var shown = repeated.Select(tag => (Counted: string.Create(CultureInfo.InvariantCulture, $"{tag.Values.Count} {tag.Name}: "),
            Values: tag.Values.Distinct(StringComparer.Ordinal).ToList())).ToList();
        var quotes = shown.Sum(tag => tag.Values.Count);
        var frame = shown.Sum(tag => tag.Counted.Length) + "; ".Length * (shown.Count - 1) + "\"\"".Length * quotes + " · ".Length * (quotes - shown.Count);
        var cut = Math.Min(RuleText.QUOTE_MAX, ShareOf(RuleText.DETAIL_MAX - frame, [.. shown.SelectMany(tag => tag.Values).Select(RuleText.Length)]));
        return RuleText.Fit(string.Join("; ", shown.Select(tag => tag.Counted + string.Join(" · ", tag.Values.Select(value => RuleText.Quote(value, cut))))));
    }

    /// <summary>
    /// The longest any of these texts may be for all of them to fit <paramref name="room"/>: the short ones whole, the long
    /// ones cut to an equal share of what's left, never under <see cref="MIN_QUOTE"/>.
    /// </summary>
    private static int ShareOf(int room, List<int> lengths)
    {
        lengths.Sort();
        for (var index = 0; index < lengths.Count; index++)
        {
            var share = room / (lengths.Count - index);
            if (lengths[index] > share)
            {
                return Math.Max(share, MIN_QUOTE);
            }

            room -= lengths[index];
        }

        return int.MaxValue;
    }

    private static string Normal(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
