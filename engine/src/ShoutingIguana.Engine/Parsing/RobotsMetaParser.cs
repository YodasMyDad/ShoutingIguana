using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Parsing;

/// <summary>
/// Merges robots meta tags and X-Robots-Tag headers. Directives addressed to all robots, to the crawler's
/// own token, or to Googlebot apply. Googlebot is always included because the point of an audit is what
/// Google will do. Header values prefixed with another crawler's name (<c>bingbot: noindex</c>) are skipped.
/// </summary>
internal static class RobotsMetaParser
{
    private const string GOOGLEBOT = "googlebot";

    private static readonly HashSet<string> VALUED_DIRECTIVES =
        new(["max-snippet", "max-image-preview", "max-video-preview", "unavailable_after"], StringComparer.Ordinal);

    public static bool AppliesToMetaName(string metaName, string robotsToken) =>
        metaName == "robots" || metaName == GOOGLEBOT || metaName.Equals(robotsToken, StringComparison.OrdinalIgnoreCase);

    public static RobotsDirectives Parse(IEnumerable<string> metaContents, IEnumerable<string> headerValues, string robotsToken)
    {
        List<string> directives = [];

        foreach (var content in metaContents)
        {
            AddDirectives(directives, content);
        }

        foreach (var value in headerValues)
        {
            var remainder = value.Trim();
            var colon = remainder.IndexOf(':');
            if (colon > 0)
            {
                var prefix = remainder[..colon].Trim().ToLowerInvariant();
                var isCrawlerName = !VALUED_DIRECTIVES.Contains(prefix) && !prefix.Contains(',') && !prefix.Contains(' ');
                if (isCrawlerName)
                {
                    if (prefix != GOOGLEBOT && !prefix.Equals(robotsToken, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    remainder = remainder[(colon + 1)..];
                }
            }

            AddDirectives(directives, remainder);
        }

        if (directives.Count == 0)
        {
            return RobotsDirectives.None;
        }

        var none = directives.Contains("none");
        return new RobotsDirectives(none || directives.Contains("noindex"), none || directives.Contains("nofollow"), directives);
    }

    private static void AddDirectives(List<string> directives, string content)
    {
        foreach (var part in content.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var directive = part.ToLowerInvariant();
            if (!directives.Contains(directive))
            {
                directives.Add(directive);
            }
        }
    }
}
