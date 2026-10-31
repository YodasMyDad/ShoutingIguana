using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Parsing;

/// <summary>
/// Merges robots meta tags and X-Robots-Tag headers. Directives addressed to all robots, to the crawler's
/// own token, or to Googlebot apply. Googlebot is always included because the point of an audit is what
/// Google will do. A header value can address several crawlers (<c>bingbot: noindex, googlebot: nofollow</c>): each
/// crawler's name starts a run of directives for it, and runs for other crawlers are skipped.
/// </summary>
internal static class RobotsMetaParser
{
    private const string GOOGLEBOT = "googlebot";

    private static readonly HashSet<string> VALUED_DIRECTIVES =
        new(["max-snippet", "max-image-preview", "max-video-preview", "unavailable_after"], StringComparer.Ordinal);

    /// <summary>Directive names Google and Bing document, for telling "noindex nofollow" (two directives) from free text.</summary>
    private static readonly HashSet<string> KNOWN_DIRECTIVES = new(
    [
        "all", "none", "index", "noindex", "follow", "nofollow", "noarchive", "nocache", "nosnippet", "noimageindex",
        "notranslate", "indexifembedded", "noodp", "noydir", .. VALUED_DIRECTIVES
    ], StringComparer.Ordinal);

    public static bool AppliesToMetaName(string metaName, string robotsToken) =>
        metaName == "robots" || metaName == GOOGLEBOT || metaName.Equals(robotsToken, StringComparison.OrdinalIgnoreCase);

    public static RobotsDirectives Parse(IEnumerable<string> metaContents, IEnumerable<string> headerValues, string robotsToken)
    {
        List<string> directives = [];

        foreach (var content in metaContents)
        {
            foreach (var part in content.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                AddDirectives(directives, part);
            }
        }

        foreach (var value in headerValues)
        {
            var applies = true;
            foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var directive = part;
                var colon = part.IndexOf(':');
                if (colon > 0)
                {
                    var prefix = part[..colon].Trim().ToLowerInvariant();
                    if (!VALUED_DIRECTIVES.Contains(prefix) && !prefix.Contains(' '))
                    {
                        applies = prefix == GOOGLEBOT || prefix.Equals(robotsToken, StringComparison.OrdinalIgnoreCase);
                        directive = part[(colon + 1)..].Trim();
                    }
                }

                if (applies && directive.Length > 0)
                {
                    AddDirectives(directives, directive);
                }
            }
        }

        if (directives.Count == 0)
        {
            return RobotsDirectives.None;
        }

        var none = directives.Contains("none");
        return new RobotsDirectives(none || directives.Contains("noindex"), none || directives.Contains("nofollow"), directives);
    }

    /// <summary>
    /// Adds one comma-separated part. Directives separated only by spaces ("noindex nofollow") count separately when
    /// every word is a known directive; anything else stays as written.
    /// </summary>
    private static void AddDirectives(List<string> directives, string part)
    {
        var lower = part.ToLowerInvariant();
        var words = lower.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var spaced = words.Length > 1 && words.All(word => KNOWN_DIRECTIVES.Contains(word.Split(':')[0]));
        foreach (var directive in spaced ? words : [lower])
        {
            if (!directives.Contains(directive))
            {
                directives.Add(directive);
            }
        }
    }
}
