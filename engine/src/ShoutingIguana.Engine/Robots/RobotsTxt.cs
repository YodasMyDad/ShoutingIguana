using ShoutingIguana.Engine.Robots.Models;

namespace ShoutingIguana.Engine.Robots;

/// <summary>
/// The robots.txt rules that apply to this crawler. Matching follows RFC 9309: the most specific
/// (longest) matching rule wins, allow wins a tie, and <c>/robots.txt</c> itself is always allowed.
/// </summary>
public sealed class RobotsTxt
{
    private readonly IReadOnlyList<RobotsRule> _rules;

    internal RobotsTxt(IReadOnlyList<RobotsRule> rules, IReadOnlyList<string> sitemaps, double? crawlDelaySeconds)
    {
        _rules = rules;
        Sitemaps = sitemaps;
        CrawlDelaySeconds = crawlDelaySeconds;
    }

    public static RobotsTxt AllowAll { get; } = new([], [], null);

    public static RobotsTxt DisallowAll { get; } = new([new RobotsRule("/", false, false, false, 1)], [], null);

    public IReadOnlyList<string> Sitemaps { get; }

    public double? CrawlDelaySeconds { get; }

    /// <param name="pathAndQuery">Path and query of a URL normalised by <c>UrlNormalizer</c>.</param>
    public bool IsAllowed(string pathAndQuery)
    {
        if (_rules.Count == 0 || pathAndQuery == "/robots.txt")
        {
            return true;
        }

        RobotsRule? best = null;
        foreach (var rule in _rules)
        {
            if (!Matches(rule, pathAndQuery))
            {
                continue;
            }

            if (best is null ||
                rule.Specificity > best.Specificity ||
                (rule.Specificity == best.Specificity && rule.Allow))
            {
                best = rule;
            }
        }

        return best?.Allow ?? true;
    }

    private static bool Matches(RobotsRule rule, string path)
    {
        if (!rule.HasWildcard)
        {
            return rule.IsAnchored
                ? path.Equals(rule.Pattern, StringComparison.Ordinal)
                : path.StartsWith(rule.Pattern, StringComparison.Ordinal);
        }

        return WildcardMatch(rule.Pattern, path, rule.IsAnchored);
    }

    /// <summary>Glob match where <c>*</c> spans any run of characters. Unanchored patterns match a prefix.</summary>
    private static bool WildcardMatch(string pattern, string path, bool anchored)
    {
        int patternIndex = 0, pathIndex = 0, starIndex = -1, starPathIndex = 0;

        while (pathIndex < path.Length)
        {
            if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex++;
                starPathIndex = pathIndex;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == path[pathIndex])
            {
                patternIndex++;
                pathIndex++;
            }
            else if (patternIndex == pattern.Length && !anchored)
            {
                return true;
            }
            else if (starIndex >= 0)
            {
                patternIndex = starIndex + 1;
                pathIndex = ++starPathIndex;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }
}
