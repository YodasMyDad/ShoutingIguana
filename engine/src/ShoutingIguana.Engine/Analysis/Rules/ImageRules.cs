using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// Alt text, dimensions, broken and oversized images, on every analysed page. Each issue names the images at fault as its
/// targets, so an image on every page (a logo without alt text) is one group of them.
/// </summary>
/// <param name="readUrlId">
/// The id of a stored URL, given normalised, or 0 when the crawl has none: an image's URL is stored normalised as the link
/// to it was (<c>OutLinkFactory</c>), so the image is the URL the page's image link leads to. One past the page's link cap,
/// which has no link, is found where another page linked to it, or not at all.
/// </param>
internal sealed class ImageRules(AnalysisSettings settings, UrlTable urls, Func<string, int> readUrlId) : IPageRule
{
    public IEnumerable<IssueHit> Check(AnalyzedPage page)
    {
        ImageCount missingAlt = new(), longAlt = new(), missingSize = new();
        Dictionary<string, int>? ids = null;
        foreach (var image in page.Facts.Images)
        {
            if (image.Url is not { } url || IsTrackingPixel(image))
            {
                continue;
            }

            // Only an image at fault is looked up, once a page however often it's shown.
            int Id() => (ids ??= new Dictionary<string, int>(StringComparer.Ordinal)).TryGetValue(url, out var known) ? known : ids[url] = readUrlId(url);

            if (!image.HasAlt)
            {
                missingAlt.Add(Id());
            }
            else if (image.Alt is { } alt && RuleText.Length(alt) > settings.ImageAltMaxLength)
            {
                longAlt.Add(Id());
            }

            if (string.IsNullOrWhiteSpace(image.Width) || string.IsNullOrWhiteSpace(image.Height))
            {
                missingSize.Add(Id());
            }
        }

        if (missingAlt.Hit(IssueCatalog.IMAGE_MISSING_ALT, page.UrlId) is { } withoutAlt)
        {
            yield return withoutAlt;
        }

        if (longAlt.Hit(IssueCatalog.IMAGE_ALT_TOO_LONG, page.UrlId) is { } tooLong)
        {
            yield return tooLong;
        }

        if (missingSize.Hit(IssueCatalog.IMAGE_MISSING_DIMENSIONS, page.UrlId) is { } withoutSize)
        {
            yield return withoutSize;
        }

        TargetSet broken = new(), large = new();
        foreach (var link in page.Links)
        {
            if (link.Kind != LinkKind.Image)
            {
                continue;
            }

            // An image that redirects is judged by where the redirects end.
            if (urls.IsBrokenThroughRedirects(link.ToId))
            {
                broken.Add(link.ToId);
            }
            else if (urls[urls.TraceRedirects(link.ToId).FinalId].SizeBytes > settings.LargeImageBytes)
            {
                large.Add(link.ToId);
            }
        }

        if (broken.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.IMAGE_BROKEN, page.UrlId, RuleText.BrokenTargets(urls, broken, "image", "images"), broken.First) { Targets = broken.Ids };
        }

        if (large.Count > 0)
        {
            var largest = large.Ids.Max(id => urls[urls.TraceRedirects(id).FinalId].SizeBytes ?? 0);
            var over = $"{RuleText.Count(large.Count, "image", "images")} over {RuleText.LimitSize(settings.LargeImageBytes)}";
            yield return new IssueHit(IssueCatalog.IMAGE_TOO_LARGE, page.UrlId,
                large.Count == 1 ? $"{over}: {RuleText.Size(largest)}" : $"{over}, the largest {RuleText.Size(largest)}", large.First) { Targets = large.Ids };
        }
    }

    /// <summary>
    /// The images of a page at fault with one check: each counted, as the detail says, and each the crawl has a URL for named
    /// once; one it has none for puts the page in the group of issues naming no image.
    /// </summary>
    private sealed class ImageCount
    {
        private readonly TargetSet _named = new();
        private int _count;
        private bool _unnamed;

        public void Add(int id)
        {
            _count++;
            if (id > 0)
            {
                _named.Add(id);
            }
            else
            {
                _unnamed = true;
            }
        }

        public IssueHit? Hit(IssueDefinition check, int pageId) => _count == 0
            ? null
            : new IssueHit(check, pageId, RuleText.Count(_count, "image", "images"), _named.First)
            {
                Targets = _unnamed ? [.. _named.Ids, IssueHit.NO_OTHER_URL] : _named.Ids
            };
    }

    /// <summary>1x1 analytics beacons aren't content, so they need neither alt text nor layout space.</summary>
    private static bool IsTrackingPixel(ImageFact image) => image.Width is "0" or "1" && image.Height is "0" or "1";
}
