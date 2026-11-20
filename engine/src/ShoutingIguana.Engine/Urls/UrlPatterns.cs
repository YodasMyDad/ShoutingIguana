using System.Text.RegularExpressions;

namespace ShoutingIguana.Engine.Urls;

/// <summary>
/// Regular expressions matched against a whole URL: which URLs are crawled, and which of them are rendered. A pattern
/// is compiled so it can't be made to take forever; one that still can is treated as <see cref="Matches"/> is told,
/// and never stalls the crawl.
/// </summary>
internal sealed class UrlPatterns
{
    public const int MAX_COUNT = 40;
    public const int MAX_LENGTH = 300;

    private readonly Regex[] _patterns;

    private UrlPatterns(Regex[] patterns) => _patterns = patterns;

    public static UrlPatterns None { get; } = new([]);

    public bool Any => _patterns.Length > 0;

    /// <summary>Compiles <paramref name="patterns"/>, or says why one of them can't be used. An empty list compiles.</summary>
    public static bool TryCompile(IReadOnlyList<string>? patterns, out UrlPatterns compiled, out string? error)
    {
        compiled = None;
        error = null;
        if (patterns is null || patterns.Count == 0)
        {
            return true;
        }

        if (patterns.Count > MAX_COUNT)
        {
            error = $"At most {MAX_COUNT} patterns.";
            return false;
        }

        var compiledPatterns = new Regex[patterns.Count];
        for (var index = 0; index < patterns.Count; index++)
        {
            var pattern = patterns[index];
            if (string.IsNullOrWhiteSpace(pattern) || pattern.Length > MAX_LENGTH)
            {
                error = $"'{Shown(pattern)}' is empty or longer than {MAX_LENGTH} characters.";
                return false;
            }

            try
            {
                compiledPatterns[index] = Compile(pattern);
            }
            catch (ArgumentException)
            {
                error = $"'{Shown(pattern)}' is not a regular expression.";
                return false;
            }
        }

        compiled = new UrlPatterns(compiledPatterns);
        return true;
    }

    /// <param name="whenUnsure">What a pattern that takes too long counts as. An exclude list says true, so the URL is left out.</param>
    public bool Matches(string url, bool whenUnsure)
    {
        foreach (var pattern in _patterns)
        {
            try
            {
                if (pattern.IsMatch(url))
                {
                    return true;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                return whenUnsure;
            }
        }

        return false;
    }

    /// <summary>
    /// Non-backtracking where the pattern allows it, so a pattern can't be made to take forever. A pattern that needs
    /// a backreference falls back to a match that gives up after a moment.
    /// </summary>
    private static Regex Compile(string pattern)
    {
        const RegexOptions COMMON = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        try
        {
            return new Regex(pattern, COMMON | RegexOptions.NonBacktracking);
        }
        catch (NotSupportedException)
        {
            return new Regex(pattern, COMMON, TimeSpan.FromMilliseconds(50));
        }
    }

    private static string Shown(string pattern) => pattern.Length <= 40 ? pattern : pattern[..40] + "…";
}
