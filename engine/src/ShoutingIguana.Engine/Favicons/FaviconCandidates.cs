using System.Globalization;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Favicons;

/// <summary>
/// Where a site's icon is looked for, best first: the icons the start page links (<c>&lt;link rel="icon"&gt;</c>), then
/// <c>/favicon.ico</c> at the page's origin, where browsers look when a page names none.
/// <list type="bullet">
/// <item>The app shows the icon in a tile of 16 to 32 px, at twice that on a dense screen, so the best is the smallest
/// icon the page says is <see cref="TARGET_PIXELS"/> px or more; failing that, the largest it says is smaller; then the
/// icons it gives no size for, in page order.</item>
/// <item>An SVG is never asked for (by its declared type or its file name): it can carry script, and isn't kept.</item>
/// <item>At most <see cref="MAX_FROM_PAGE"/> of the page's icons are tried, so a page listing dozens costs the site a
/// few requests, not dozens.</item>
/// </list>
/// </summary>
internal static class FaviconCandidates
{
    /// <summary>A 32 px tile at two device pixels a pixel.</summary>
    public const int TARGET_PIXELS = 64;

    /// <summary>The most of a page's own icons that are tried before <c>/favicon.ico</c>.</summary>
    public const int MAX_FROM_PAGE = 3;

    public const string FALLBACK_PATH = "/favicon.ico";

    /// <summary>The URLs to try, in order, each once.</summary>
    /// <param name="page">The page the icons are linked from.</param>
    public static IReadOnlyList<Uri> Rank(Uri page, IReadOnlyList<IconFact> icons)
    {
        var ranked = icons
            .Select((icon, index) => (icon, index, Uri: Uri.TryCreate(icon.Url, UriKind.Absolute, out var uri) && UrlNormalizer.IsHttp(uri) ? uri : null))
            .Where(candidate => candidate.Uri is not null && !IsSvg(candidate.icon, candidate.Uri))
            .Select(candidate => (Uri: candidate.Uri!, candidate.index, Size: DeclaredSize(candidate.icon.Sizes)))
            .OrderBy(candidate => candidate.Size switch { >= TARGET_PIXELS => 0, > 0 => 1, _ => 2 })
            .ThenBy(candidate => candidate.Size >= TARGET_PIXELS ? candidate.Size : -candidate.Size)
            .ThenBy(candidate => candidate.index)
            .Select(candidate => candidate.Uri)
            .DistinctBy(uri => uri.AbsoluteUri, StringComparer.Ordinal)
            .Take(MAX_FROM_PAGE)
            .ToList();

        var fallback = new Uri(page, FALLBACK_PATH);
        if (ranked.All(uri => uri.AbsoluteUri != fallback.AbsoluteUri))
        {
            ranked.Add(fallback);
        }

        return ranked;
    }

    /// <summary>
    /// The size a <c>sizes</c> attribute declares, as one number of pixels: the larger side of the size nearest above the
    /// target, or of the largest when none reaches it. 0 when it declares none ("any", nothing, or nothing readable).
    /// </summary>
    internal static int DeclaredSize(string? sizes)
    {
        if (sizes is null)
        {
            return 0;
        }

        var declared = sizes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Split('x', 'X'))
            .Where(parts => parts.Length == 2)
            .Select(parts => int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var width) &&
                             int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var height)
                ? Math.Max(width, height)
                : 0)
            .Where(size => size > 0)
            .ToList();
        return declared.Where(size => size >= TARGET_PIXELS).DefaultIfEmpty(declared.DefaultIfEmpty(0).Max()).Min();
    }

    private static bool IsSvg(IconFact icon, Uri uri) =>
        icon.Type?.Contains("svg", StringComparison.OrdinalIgnoreCase) == true ||
        uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath.EndsWith(".svgz", StringComparison.OrdinalIgnoreCase);
}
