using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Analysis;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// Functions the grid readers' SQL calls: a URL's resource type, judged as the checks judge it, for a crawl without a URL
/// summary, and regular expressions, which run without backtracking so that no pattern can hang a query.
/// </summary>
internal static class ReaderFunctions
{
    public const string RESOURCE_TYPE = "si_resource_type";

    public const string REGEX = "si_regex";

    /// <summary>Linear in the text matched, and the same for every culture.</summary>
    private const RegexOptions OPTIONS = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant;

    public static void Register(SqliteConnection connection)
    {
        // Judged as the URL summary judges it: by the content type of a successful response, otherwise by the extension.
        connection.CreateFunction<string, string?, int>(RESOURCE_TYPE,
            (url, contentType) => (int)(contentType is null ? ResourceTypes.OfUrl(url) : ResourceTypes.OfContentType(contentType)), isDeterministic: true);

        // One query's patterns, compiled once each: a connection serves one request.
        Dictionary<string, Regex> patterns = new(StringComparer.Ordinal);
        connection.CreateFunction<string, string?, bool?>(REGEX, (pattern, text) =>
        {
            if (text is null)
            {
                return null;
            }

            if (!patterns.TryGetValue(pattern, out var regex))
            {
                patterns[pattern] = regex = new Regex(pattern, OPTIONS);
            }

            return regex.IsMatch(text);
        }, isDeterministic: true);
    }

    /// <summary>Why a pattern can't be used, in words for the user, or null when it can.</summary>
    public static string? CheckPattern(string pattern)
    {
        try
        {
            _ = new Regex(pattern, OPTIONS).IsMatch(string.Empty);
            return null;
        }
        catch (NotSupportedException)
        {
            return "This pattern needs backtracking (a backreference, a lookaround or an atomic group), which isn't supported: rewrite it without.";
        }
        catch (ArgumentException exception)
        {
            return $"This isn't a valid regular expression: {exception.Message}";
        }
    }
}
