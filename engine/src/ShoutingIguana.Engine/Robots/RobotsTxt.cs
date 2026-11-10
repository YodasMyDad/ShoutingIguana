using ShoutingIguana.Engine.Robots.Models;

namespace ShoutingIguana.Engine.Robots;

/// <summary>
/// The robots.txt rules that apply to this crawler. Matching follows RFC 9309: the most specific
/// (longest) matching rule wins, allow wins a tie, and <c>/robots.txt</c> itself is always allowed.
/// </summary>
public sealed class RobotsTxt
{
    /// <summary>
    /// Start of the error stored for a URL refused because its site's robots.txt couldn't be read, which sets it apart
    /// from a URL a rule disallows (stored without an error).
    /// </summary>
    public const string UNREACHABLE_ERROR = "robots.txt unreachable";

    private readonly IReadOnlyList<RobotsRule> _rules;

    internal RobotsTxt(IReadOnlyList<RobotsRule> rules, IReadOnlyList<string> sitemaps, double? crawlDelaySeconds, string? unreachableReason = null,
        RobotsNoAnswer? noAnswer = null)
    {
        _rules = rules;
        Sitemaps = sitemaps;
        CrawlDelaySeconds = crawlDelaySeconds;
        UnreachableReason = unreachableReason;
        NoAnswer = noAnswer;
    }

    public static RobotsTxt AllowAll { get; } = new([], [], null);

    public static RobotsTxt DisallowAll { get; } = new([new RobotsRule("/", false, false, false, 1)], [], null);

    public IReadOnlyList<string> Sitemaps { get; }

    public double? CrawlDelaySeconds { get; }

    /// <summary>Why robots.txt couldn't be read (<c>HTTP 503</c>, <c>Timed out</c>...) when that left a full disallow.</summary>
    public string? UnreachableReason { get; }

    /// <summary>
    /// The origin gave no answer at all, not even an error, so robots.txt sets no rules: how it failed. Only an origin other
    /// than the start URL's gets this; there, no answer means a full disallow (see <see cref="UnreachableReason"/>).
    /// </summary>
    internal RobotsNoAnswer? NoAnswer { get; }

    /// <summary>RFC 9309: a robots.txt that can't be read (5xx, 429, no answer) means nothing may be crawled.</summary>
    internal static RobotsTxt Unreachable(string reason) => new(DisallowAll._rules, [], null, reason);

    /// <summary>No answer at all on an origin other than the start URL's: no verdict, so everything is allowed.</summary>
    internal static RobotsTxt Unanswered(RobotsNoAnswer noAnswer) => new([], [], null, null, noAnswer);

    /// <param name="pathAndQuery">Path and query of a URL normalised by <c>UrlNormalizer</c>.</param>
    public bool IsAllowed(string pathAndQuery) => Match(pathAndQuery)?.Allow ?? true;

    /// <summary>
    /// The rule that decides a URL: the most specific one that matches, an allow winning a tie. Null when none matches, which
    /// allows it. What the crawl obeys and what the robots.txt screen shows for a blocked URL are this one answer.
    /// </summary>
    /// <param name="pathAndQuery">Path and query of a URL normalised by <c>UrlNormalizer</c>.</param>
    internal RobotsRule? Match(string pathAndQuery)
    {
        if (_rules.Count == 0 || pathAndQuery == "/robots.txt")
        {
            return null;
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

        return best;
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
