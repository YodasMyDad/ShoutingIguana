using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// Functions the grid readers' SQL calls, registered on each read's connection:
/// <list type="bullet">
/// <item>a URL's resource type, judged as the checks judge it, for a crawl without a URL summary;</item>
/// <item>regular expressions, which run without backtracking, each match stopped after <see cref="MATCH_TIMEOUT"/> and a
/// read's matches after <see cref="GridReadContext.RegexBudget"/> in all, so no pattern can hang a read;</item>
/// <item>the read's sets of URL ids (<see cref="Models.UrlIdSet"/>), tested and filled row by row.</item>
/// </list>
/// </summary>
internal static class ReaderFunctions
{
    public const string RESOURCE_TYPE = "si_resource_type";

    public const string REGEX = "si_regex";

    /// <summary><c>si_ids(slot, id)</c>: whether the id is in the read's set in that slot (<see cref="GridReadContext.Use"/>); null for no id.</summary>
    public const string IN_SET = "si_ids";

    /// <summary><c>si_add(slot, id)</c>: adds the id to the set being read in that slot, and gives 1.</summary>
    public const string ADD_TO_SET = "si_add";

    /// <summary><c>si_keep(key, id)</c>: keeps a row's sort key and URL id, for a page selected in memory (<see cref="GridReadContext.Kept"/>), and gives 1.</summary>
    public const string KEEP = "si_keep";

    /// <summary>
    /// <c>si_tally(value)</c>: counts one more row with this value (<see cref="GridReadContext.Tallies"/>), and gives 1.
    /// <c>si_tally(slot, value)</c> counts it in one of several tallies (<see cref="GridReadContext.TalliesIn"/>), for a
    /// pass that counts its rows several ways at once; a null value isn't counted, and gives null.
    /// </summary>
    public const string TALLY = "si_tally";

    /// <summary>
    /// <c>si_link(id, to_id, from_id, kind, position, flags)</c>: one link read from <c>links</c>, its id kept and the link
    /// counted, whichever the read set up (<see cref="GridReadContext.ReadLink"/>), and gives 1.
    /// </summary>
    public const string LINK = "si_link";

    /// <summary><c>si_link_id(id)</c>: one link's id kept, where a read keeps the ids and counts nothing (<see cref="GridReadContext.LinkIds"/>), and gives 1.</summary>
    public const string LINK_ID = "si_link_id";

    /// <summary>
    /// <c>si_links(to_id, kind, position, flags, source_nofollow, links)</c>: one row of the link summary, its links counted
    /// by target and shape (<see cref="GridReadContext.LinkTally"/>), and gives 1.
    /// </summary>
    public const string LINKS = "si_links";

    /// <summary>
    /// <c>si_search_url(id, url, is_internal)</c>: one URL's text kept for searching (<see cref="GridReadContext.SearchText"/>),
    /// and gives 1.
    /// </summary>
    public const string SEARCH_URL = "si_search_url";

    /// <summary><c>si_search_title(url_id, title)</c>: one URL's title kept for searching, and gives 1.</summary>
    public const string SEARCH_TITLE = "si_search_title";

    /// <summary>What the user is told of a pattern whose matches took too long on the crawl's text.</summary>
    public const string TOO_COMPLEX = "This pattern is too complex to run on this crawl. Simplify it.";

    /// <summary>Linear in the text matched, and the same for every culture.</summary>
    private const RegexOptions OPTIONS = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant;

    /// <summary>
    /// The longest one match may take. A pattern without backtracking is linear in the text, but nested counted repeats
    /// (<c>((.{0,9}){0,9}){0,9}</c>) build an automaton that can take minutes on one URL: such a pattern is refused.
    /// </summary>
    public static readonly TimeSpan MATCH_TIMEOUT = TimeSpan.FromMilliseconds(100);

    public static void Register(SqliteConnection connection, GridReadContext context)
    {
        // A call for each row, through SQLite's own interface: Microsoft.Data.Sqlite's wrapper costs three times as much a
        // call, which a scan of a million rows feels (160 ms against 60), and makes two strings of each. Nothing may throw
        // back into SQLite: a failure is the statement's error.
        var handle = connection.Handle!;

        // Judged as the URL summary judges it: by the content type of a successful response, otherwise by the extension.
        raw.sqlite3_create_function(handle, RESOURCE_TYPE, 2, raw.SQLITE_UTF8 | raw.SQLITE_DETERMINISTIC, context, static (call, state, arguments) =>
        {
            try
            {
                var read = (GridReadContext)state;
                var type = raw.sqlite3_value_type(arguments[1]) == raw.SQLITE_NULL
                    ? read.TypeOfUrl(raw.sqlite3_value_blob(arguments[0]))
                    : read.TypeOfContent(raw.sqlite3_value_blob(arguments[1]));
                raw.sqlite3_result_int(call, (int)type);
            }
            catch (Exception exception)
            {
                raw.sqlite3_result_error(call, exception.Message);
            }
        });

        raw.sqlite3_create_function(handle, REGEX, 2, raw.SQLITE_UTF8 | raw.SQLITE_DETERMINISTIC, context, static (call, state, arguments) =>
        {
            try
            {
                if (raw.sqlite3_value_type(arguments[1]) == raw.SQLITE_NULL)
                {
                    raw.sqlite3_result_null(call);
                    return;
                }

                // A text's bytes as stored, UTF-8, which the pattern's are too.
                var read = (GridReadContext)state;
                raw.sqlite3_result_int(call, read.Matches(raw.sqlite3_value_blob(arguments[0]), raw.sqlite3_value_blob(arguments[1])) ? 1 : 0);
            }
            catch (Exception exception)
            {
                raw.sqlite3_result_error(call, exception.Message);
            }
        });

        raw.sqlite3_create_function(handle, TALLY, 1, raw.SQLITE_UTF8, context, static (call, state, arguments) =>
        {
            try
            {
                ((GridReadContext)state).Tally(raw.sqlite3_value_int64(arguments[0]));
                raw.sqlite3_result_int(call, 1);
            }
            catch (Exception exception)
            {
                raw.sqlite3_result_error(call, exception.Message);
            }
        });

        raw.sqlite3_create_function(handle, TALLY, 2, raw.SQLITE_UTF8, context, static (call, state, arguments) =>
        {
            try
            {
                if (raw.sqlite3_value_type(arguments[1]) == raw.SQLITE_NULL)
                {
                    raw.sqlite3_result_null(call);
                    return;
                }

                ((GridReadContext)state).Tally((int)raw.sqlite3_value_int64(arguments[0]), raw.sqlite3_value_int64(arguments[1]));
                raw.sqlite3_result_int(call, 1);
            }
            catch (Exception exception)
            {
                raw.sqlite3_result_error(call, exception.Message);
            }
        });

        raw.sqlite3_create_function(handle, KEEP, 2, raw.SQLITE_UTF8, context, static (call, state, arguments) =>
        {
            try
            {
                ((GridReadContext)state).Keep(raw.sqlite3_value_type(arguments[0]) == raw.SQLITE_NULL ? null : raw.sqlite3_value_int64(arguments[0]),
                    raw.sqlite3_value_int64(arguments[1]));
                raw.sqlite3_result_int(call, 1);
            }
            catch (Exception exception)
            {
                raw.sqlite3_result_error(call, exception.Message);
            }
        });

        raw.sqlite3_create_function(handle, LINK, 6, raw.SQLITE_UTF8, context, static (call, state, arguments) =>
        {
            try
            {
                ((GridReadContext)state).ReadLink(raw.sqlite3_value_int64(arguments[0]), raw.sqlite3_value_int64(arguments[1]), raw.sqlite3_value_int64(arguments[2]),
                    raw.sqlite3_value_int64(arguments[3]), raw.sqlite3_value_int64(arguments[4]), raw.sqlite3_value_int64(arguments[5]));
                raw.sqlite3_result_int(call, 1);
            }
            catch (Exception exception)
            {
                raw.sqlite3_result_error(call, exception.Message);
            }
        });

        raw.sqlite3_create_function(handle, LINK_ID, 1, raw.SQLITE_UTF8, context, static (call, state, arguments) =>
        {
            try
            {
                ((GridReadContext)state).LinkIds?.Add(raw.sqlite3_value_int64(arguments[0]));
                raw.sqlite3_result_int(call, 1);
            }
            catch (Exception exception)
            {
                raw.sqlite3_result_error(call, exception.Message);
            }
        });

        raw.sqlite3_create_function(handle, LINKS, 6, raw.SQLITE_UTF8, context, static (call, state, arguments) =>
        {
            try
            {
                ((GridReadContext)state).LinkTally?.Add(raw.sqlite3_value_int64(arguments[0]), raw.sqlite3_value_int64(arguments[1]), raw.sqlite3_value_int64(arguments[2]),
                    raw.sqlite3_value_int64(arguments[3]), raw.sqlite3_value_int64(arguments[4]) != 0, raw.sqlite3_value_int64(arguments[5]));
                raw.sqlite3_result_int(call, 1);
            }
            catch (Exception exception)
            {
                raw.sqlite3_result_error(call, exception.Message);
            }
        });

        raw.sqlite3_create_function(handle, SEARCH_URL, 3, raw.SQLITE_UTF8, context, static (call, state, arguments) =>
        {
            try
            {
                ((GridReadContext)state).SearchText?.AddUrl(raw.sqlite3_value_int64(arguments[0]), raw.sqlite3_value_blob(arguments[1]),
                    raw.sqlite3_value_int64(arguments[2]) != 0);
                raw.sqlite3_result_int(call, 1);
            }
            catch (Exception exception)
            {
                raw.sqlite3_result_error(call, exception.Message);
            }
        });

        raw.sqlite3_create_function(handle, SEARCH_TITLE, 2, raw.SQLITE_UTF8, context, static (call, state, arguments) =>
        {
            try
            {
                ((GridReadContext)state).SearchText?.AddTitle(raw.sqlite3_value_int64(arguments[0]), raw.sqlite3_value_blob(arguments[1]));
                raw.sqlite3_result_int(call, 1);
            }
            catch (Exception exception)
            {
                raw.sqlite3_result_error(call, exception.Message);
            }
        });

        raw.sqlite3_create_function(handle, IN_SET, 2, raw.SQLITE_UTF8 | raw.SQLITE_DETERMINISTIC, context, static (call, state, arguments) =>
        {
            try
            {
                if (raw.sqlite3_value_type(arguments[1]) == raw.SQLITE_NULL)
                {
                    raw.sqlite3_result_null(call);
                    return;
                }

                var read = (GridReadContext)state;
                raw.sqlite3_result_int(call, read.HasId((int)raw.sqlite3_value_int64(arguments[0]), raw.sqlite3_value_int64(arguments[1])) ? 1 : 0);
            }
            catch (Exception exception)
            {
                raw.sqlite3_result_error(call, exception.Message);
            }
        });

        raw.sqlite3_create_function(handle, ADD_TO_SET, 2, raw.SQLITE_UTF8, context, static (call, state, arguments) =>
        {
            try
            {
                ((GridReadContext)state).AddId((int)raw.sqlite3_value_int64(arguments[0]), raw.sqlite3_value_int64(arguments[1]));
                raw.sqlite3_result_int(call, 1);
            }
            catch (Exception exception)
            {
                raw.sqlite3_result_error(call, exception.Message);
            }
        });
    }

    /// <summary>A pattern ready to match, each match stopped after <see cref="MATCH_TIMEOUT"/>.</summary>
    public static Regex Compile(string pattern) => new(pattern, OPTIONS, MATCH_TIMEOUT);

    /// <summary>Why a pattern can't be used, in words for the user, or null when it can.</summary>
    public static string? CheckPattern(string pattern)
    {
        try
        {
            _ = Compile(pattern).IsMatch(string.Empty);
            return null;
        }
        catch (NotSupportedException exception) when (exception.Message.Contains("automata", StringComparison.OrdinalIgnoreCase))
        {
            // Too large to build: its repeats, multiplied out, make an automaton over the engine's limit.
            return "This pattern is too complex. Simplify it: large repeat counts, such as {5000}, make it so.";
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
