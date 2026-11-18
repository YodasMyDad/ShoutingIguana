using System.Text.RegularExpressions;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// Image problems on a page: alt text, dimensions, broken and oversized files; and since batch 6 (docs/v2-new-checks.md),
/// images with no source, wide files without srcset, files in an older format, alt text that describes nothing, images
/// from other websites, and large inline images.
/// </summary>
/// <param name="scope">The crawl's scope, which says which images are the site's own; with none, every image is.</param>
/// <param name="startIsPrivate">
/// The crawl started at a private address: another private host's images (a test site's partner on the same machine)
/// can't be told from the site's own, so they aren't called another website's.
/// </param>
/// <param name="readUrl">A URL's text by its id, for an image on another host.</param>
internal sealed partial class ImageRules(AnalysisSettings settings, UrlTable urls, Func<string, int> readUrlId, CrawlScope? scope = null,
    bool startIsPrivate = false, Func<int, string?>? readUrl = null) : IPageRule
{
    private static readonly string[] IMAGE_EXTENSIONS = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".avif", ".svg", ".bmp"];

    /// <summary>Image CDNs a site serves its own images from, which are no other website's (a leading * is any subdomain).</summary>
    private static readonly string[] CDN_HOSTS =
    [
        "cdn.shopify.com", "*.cloudfront.net", "*.akamaized.net", "*.fastly.net", "res.cloudinary.com", "*.imgix.net", "i0.wp.com", "i1.wp.com",
        "i2.wp.com", "i3.wp.com", "*.wixstatic.com", "images.squarespace-cdn.com", "images.ctfassets.net", "*.bigcommerce.com", "*.googleusercontent.com"
    ];

    /// <summary>Second-level public suffixes under which a registrable domain takes three labels (example.co.uk).</summary>
    private static readonly HashSet<string> TWO_LEVEL_SUFFIXES = new(StringComparer.OrdinalIgnoreCase)
    {
        "co.uk", "org.uk", "ac.uk", "gov.uk", "me.uk", "ltd.uk", "plc.uk", "net.uk", "sch.uk", "com.au", "net.au", "org.au", "edu.au", "gov.au",
        "co.nz", "org.nz", "net.nz", "co.jp", "ne.jp", "or.jp", "com.br", "net.br", "co.za", "org.za", "com.cn", "net.cn", "org.cn", "co.in",
        "net.in", "org.in", "com.mx", "com.ar", "com.tr", "co.kr", "or.kr", "com.sg", "com.hk", "com.tw", "co.il", "com.my", "co.id", "com.ph",
        "com.vn", "com.pk", "com.ng", "co.ke", "com.eg", "com.sa", "com.ua", "com.co", "com.pe", "com.es", "com.pl", "co.th"
    };

    public IEnumerable<IssueHit> Check(AnalyzedPage page)
    {
        ImageCount missingAlt = new(), longAlt = new(), missingSize = new(), unhelpfulAlt = new();
        var missingSource = 0;
        List<long> inline = [];
        Dictionary<string, int>? ids = null;
        foreach (var image in page.Facts.Images)
        {
            if (image.Url is not { } url)
            {
                // An image with nothing to fetch: a data: URI is the page's own bytes; with no source at all it never shows.
                if (image.DataUriBytes is { } bytes && bytes > settings.InlineImageMaxBytes)
                {
                    inline.Add(bytes);
                }
                else if (!image.IsDataUri && !image.HasSrcset && !IsTrackingPixel(image))
                {
                    missingSource++;
                }

                continue;
            }

            if (IsTrackingPixel(image))
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
            else if (image.Alt is { } written && IsUnhelpfulAlt(written, url))
            {
                unhelpfulAlt.Add(Id());
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

        if (unhelpfulAlt.Hit(IssueCatalog.IMAGE_ALT_UNHELPFUL, page.UrlId) is { } unhelpful)
        {
            yield return unhelpful;
        }

        if (missingSource > 0)
        {
            yield return new IssueHit(IssueCatalog.IMAGE_MISSING_SRC, page.UrlId, RuleText.Count(missingSource, "image", "images"));
        }

        if (inline.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.IMAGE_INLINE_LARGE, page.UrlId, inline.Count == 1
                ? $"1 inline image: {RuleText.Size(inline[0])}"
                : $"{RuleText.Count(inline.Count, "inline image", "inline images")}, the largest {RuleText.Size(inline.Max())}");
        }

        TargetSet broken = new(), large = new(), legacy = new(), hotlinked = new();
        HashSet<string> hotlinkSites = new(StringComparer.OrdinalIgnoreCase);
        foreach (var link in page.Links)
        {
            if (link.Kind != LinkKind.Image)
            {
                continue;
            }

            // An image that redirects is judged by where the redirects end.
            var file = urls[urls.TraceRedirects(link.ToId).FinalId];
            if (urls.IsBrokenThroughRedirects(link.ToId))
            {
                broken.Add(link.ToId);
            }
            else
            {
                if (file.SizeBytes > settings.LargeImageBytes)
                {
                    large.Add(link.ToId);
                }

                if (file is { IsLegacyImage: true, SizeBytes: { } size } && size >= settings.LegacyImageMinBytes)
                {
                    legacy.Add(link.ToId);
                }
            }

            if (OtherSite(link.ToId) is { } site)
            {
                hotlinked.Add(link.ToId);
                hotlinkSites.Add(site);
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

        if (legacy.Count > 0)
        {
            var largest = legacy.Ids.Max(id => urls[urls.TraceRedirects(id).FinalId].SizeBytes ?? 0);
            yield return new IssueHit(IssueCatalog.IMAGE_LEGACY_FORMAT, page.UrlId, legacy.Count == 1
                ? $"1 image: {RuleText.Size(largest)}"
                : $"{RuleText.Count(legacy.Count, "image", "images")}, the largest {RuleText.Size(largest)}", legacy.First) { Targets = legacy.Ids };
        }

        if (WideWithoutSrcset(page) is { Count: > 0 } wide)
        {
            var widest = wide.Ids.Max(id => urls[urls.TraceRedirects(id).FinalId].PixelWidth ?? 0);
            yield return new IssueHit(IssueCatalog.IMAGE_NO_SRCSET, page.UrlId, wide.Count == 1
                ? $"1 image, {widest:N0} px wide"
                : $"{RuleText.Count(wide.Count, "image", "images")}, the widest {widest:N0} px", wide.First) { Targets = wide.Ids };
        }

        if (hotlinked.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.IMAGE_HOTLINKED, page.UrlId,
                $"{RuleText.Count(hotlinked.Count, "image", "images")} from {RuleText.Count(hotlinkSites.Count, "site", "sites")}", hotlinked.First) { Targets = hotlinked.Ids };
        }
    }

    /// <summary>
    /// The page's images whose file is at least the limit wide with no srcset and no picture's sources. Each &lt;img&gt;
    /// with a URL is the image link of the same place, as the evidence pairs them; with the counts apart (a page past the
    /// link cap), each such image's URL is looked up instead.
    /// </summary>
    private TargetSet WideWithoutSrcset(AnalyzedPage page)
    {
        TargetSet wide = new();
        var images = page.Facts.Images.Where(image => image.Url is not null).ToList();
        var links = page.Links.Where(link => link.Kind == LinkKind.Image).Select(link => link.ToId).ToList();
        for (var index = 0; index < images.Count; index++)
        {
            var image = images[index];
            if (image.HasSrcset || image.InPicture || IsTrackingPixel(image))
            {
                continue;
            }

            var id = links.Count == images.Count ? links[index] : readUrlId(image.Url!);
            if (id > 0 && urls[urls.TraceRedirects(id).FinalId].PixelWidth >= settings.ResponsiveImageMinWidth)
            {
                wide.Add(id);
            }
        }

        return wide;
    }

    /// <summary>An image on another website's host: its registrable domain, or null for the site's own, a CDN's, or one it can't tell.</summary>
    private string? OtherSite(int id)
    {
        var status = urls[id];
        if (scope is null || readUrl is null || status.IsInternal || startIsPrivate && status.IsPrivateHost ||
            !Uri.TryCreate(readUrl(id), UriKind.Absolute, out var uri) || scope.IsInternal(uri))
        {
            return null;
        }

        var host = uri.IdnHost;
        if (CDN_HOSTS.Any(cdn => cdn.StartsWith("*.", StringComparison.Ordinal)
                ? host.EndsWith(cdn[1..], StringComparison.OrdinalIgnoreCase) || host.Equals(cdn[2..], StringComparison.OrdinalIgnoreCase)
                : host.Equals(cdn, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var site = RegistrableDomain(host);
        return string.Equals(site, RegistrableDomain(scope.StartOrigin.IdnHost), StringComparison.OrdinalIgnoreCase) ? null : site;
    }

    /// <summary>A host's registrable domain: its last two labels, or three under a second-level suffix such as co.uk.</summary>
    internal static string RegistrableDomain(string host)
    {
        var labels = host.TrimEnd('.').Split('.');
        if (labels.Length <= 2 || Uri.CheckHostName(host) is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            return host;
        }

        var last = $"{labels[^2]}.{labels[^1]}";
        return TWO_LEVEL_SUFFIXES.Contains(last) ? $"{labels[^3]}.{last}" : last;
    }

    /// <summary>
    /// Alt text that describes nothing: one or two characters (not in a script where that's a word, as Chinese), none a
    /// letter or digit, an image file name, a camera's or upload's name, or the image file's own name (whole, or as a slug).
    /// </summary>
    internal static bool IsUnhelpfulAlt(string alt, string url)
    {
        var text = alt.Trim();
        if (text.Length == 0)
        {
            return false;
        }

        if (!text.Any(char.IsLetterOrDigit))
        {
            return true;
        }

        if (RuleText.Length(text) <= 2 && !text.Any(IsIdeographic))
        {
            return true;
        }

        if (IMAGE_EXTENSIONS.Any(extension => text.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) || CameraName().IsMatch(text))
        {
            return true;
        }

        // The file's own name: whole, or as a slug ("evolution-of-web-design"). A plain word that names its file is often the
        // right alt, as a brand's name is for its logo (avon.png, "Avon").
        var file = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath)) : string.Empty;
        var isSlug = !text.Contains(' ', StringComparison.Ordinal) && text.IndexOfAny(['-', '_']) >= 0;
        return file.Length > 0 && (string.Equals(text, file, StringComparison.OrdinalIgnoreCase) ||
                                   isSlug && string.Equals(text, Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsIdeographic(char character) =>
        character is >= '\u3040' and <= '\u30FF' or >= '\u3400' and <= '\u9FFF' or >= '\uAC00' and <= '\uD7AF' or >= '\uF900' and <= '\uFAFF';

    /// <summary>A camera's or upload's file name: IMG_1234, DSC01234, PXL_20230101…, image-3, photo_12.</summary>
    [GeneratedRegex(@"^(?:(?:IMG|DSC|DSCN|DSCF|DCIM|PXL|MVIMG|VID|WP|GOPR)[_-]?\d[\w-]*|(?:image|img|photo|pic|picture|screenshot|untitled)[ _-]?\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CameraName();

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

    private static bool IsTrackingPixel(ImageFact image) => image.Width is "0" or "1" && image.Height is "0" or "1";
}
