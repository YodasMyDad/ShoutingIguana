using System.Text.RegularExpressions;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The URLs a text typed or pasted into "Go to URL…" could be in a crawl, in the order to look for them: the text as a URL,
/// a path on the site, or a URL without its scheme; then, as the engine treats www and the bare domain, and http and https,
/// as one site (<see cref="CrawlScope"/>), the same on the site's other versions. The site lives where the start URL's
/// redirects end, which is often another of its versions: a crawl started at example.com that redirects to
/// https://www.example.com/ has its pages there.
/// </summary>
internal static partial class UrlLookupCandidates
{
    /// <summary>The URL the text stands for and the URLs to look for, it first; null when the text isn't a URL.</summary>
    /// <param name="home">Where the start URL's redirects end, normalised: the site's own origin.</param>
    /// <param name="start">The start URL, normalised; null for a crawl file whose start URL isn't one.</param>
    public static (string Requested, List<string> Candidates)? Of(string text, string? home, string? start)
    {
        var origins = Origins(home, start);
        var candidate = text.Trim();
        if (candidate.StartsWith('/') && !candidate.StartsWith("//", StringComparison.Ordinal))
        {
            // A path: on each of the site's origins, where it lives first.
            List<string> onSite = [.. origins.Select(origin => Uri.TryCreate(origin, candidate, out var resolved) ? Normalized(resolved) : null).OfType<string>()];
            if (onSite.Count == 0)
            {
                return UrlNormalizer.TryNormalize(candidate, out var asIs) ? (asIs, [asIs]) : null;
            }

            return (onSite[0], [.. onSite.Distinct(StringComparer.Ordinal)]);
        }

        // Without a scheme, the site's: "//example.com/a", "www.example.com/a", "example.com:8080/a".
        var scheme = origins.FirstOrDefault()?.Scheme ?? Uri.UriSchemeHttps;
        if (candidate.StartsWith("//", StringComparison.Ordinal))
        {
            candidate = scheme + ":" + candidate;
        }
        else if (!Scheme().IsMatch(candidate))
        {
            candidate = scheme + Uri.SchemeDelimiter + candidate;
        }

        if (!UrlNormalizer.TryNormalize(candidate, out var requested))
        {
            return null;
        }

        // On the site: the same path and query on the origin the site lives on, then on each of its other versions.
        var asked = new Uri(requested);
        List<string> candidates = [requested];
        if (origins.Any(origin => CrawlScope.SiteHost(UrlNormalizer.GetHost(origin)) == CrawlScope.SiteHost(UrlNormalizer.GetHost(asked))))
        {
            candidates.AddRange(origins.Select(origin => new Uri(origin, asked.PathAndQuery)).Select(Normalized));
        }

        return (requested, [.. candidates.Distinct(StringComparer.Ordinal)]);
    }

    /// <summary>The site's origins, where it lives first: the home page's, the start URL's, then each one's other versions.</summary>
    private static List<Uri> Origins(string? home, string? start)
    {
        List<Uri> roots = [.. new[] { home, start }.OfType<string>().Select(url => new Uri(new Uri(url).GetLeftPart(UriPartial.Authority) + "/"))];
        return
        [
            .. roots.Concat(roots.SelectMany(root => SiteVersions.Of(root)).Select(url => new Uri(url)))
                .DistinctBy(origin => origin.GetLeftPart(UriPartial.Authority), StringComparer.Ordinal)
        ];
    }

    private static string Normalized(Uri uri) => UrlNormalizer.Normalize(uri);

    /// <summary>A scheme before the colon, rather than a host before its port: "mailto:", "https:", but not "example.com:8080".</summary>
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9+.-]*:(?![0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex Scheme();
}
