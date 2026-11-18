using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The cookie check's recheck (<c>cookies-insecure</c>), settled by cookie, not by page. A session cookie lands on
/// whichever page a fresh visitor asks first: the crawl's cookie jar had it after its first page, so later pages weren't
/// sent it; a recheck starts with an empty jar, so it lands on a page it asks, and a page that had it may not set it
/// again. So:
/// <list type="bullet">
/// <item>A URL flagged now and not before is not new when every cookie it is flagged for the crawl had already flagged on
/// a page: judged, it's not checked; otherwise it doesn't count as new.</item>
/// <item>A page fixed by the check's lights is fixed only when it set every cookie it was flagged for again, protected now.
/// One that didn't set them again says nothing of them: not checked. A page that's gone stays fixed.</item>
/// </list>
/// The alternatives were worse: the crawl's cookies aren't kept to send again (nor would sending them show a cookie
/// fixed), and "check the site again" would lose a recheck that can show a cookie protected.
/// </summary>
internal static class RecheckCookies
{
    /// <summary>Runs after the recheck's outcomes are set, before its counts, in the analysis's transaction.</summary>
    public static void Settle(SqliteConnection connection, SqliteTransaction transaction, int id, long startedAt)
    {
        var check = IssueCatalog.COOKIES_INSECURE.Id;

        // The cookies the crawl had flagged before the recheck: on the URLs it set out with, and on those it didn't fetch again.
        HashSet<string> flaggedBefore = new(StringComparer.Ordinal);
        foreach (var detail in Texts(connection, transaction, "SELECT detail_before FROM recheck_urls WHERE recheck_id = $id AND flagged_before = 1",
                     ("$id", id)))
        {
            flaggedBefore.UnionWith(Names(detail));
        }

        foreach (var detail in Texts(connection, transaction, """
                     SELECT i.detail FROM issues i LEFT JOIN responses r ON r.url_id = i.url_id
                     WHERE i.issue = $check AND COALESCE(r.fetched_at, 0) < $startedAt
                     """, ("$check", check), ("$startedAt", startedAt)))
        {
            flaggedBefore.UnionWith(Names(detail));
        }

        List<(int UrlId, bool Judged, bool WasFlagged, RecheckOutcome? Outcome, string? Before, string? Now, bool ReadAsPage)> rows = [];
        using (var command = Command(connection, transaction, $"""
                   SELECT ru.url_id, ru.role = {(int)RecheckRole.Judged}, ru.flagged_before, ru.outcome, ru.detail_before,
                          (SELECT i.detail FROM issues i WHERE i.issue = $check AND i.url_id = ru.url_id),
                          EXISTS (SELECT 1 FROM responses r WHERE r.url_id = ru.url_id AND r.outcome = {(int)FetchOutcome.Success})
                   FROM recheck_urls ru WHERE ru.recheck_id = $id
                   """, ("$check", check), ("$id", id)))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                rows.Add((reader.GetInt32(0), reader.GetBoolean(1), reader.GetBoolean(2), reader.IsDBNull(3) ? null : (RecheckOutcome)reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetBoolean(6)));
            }
        }

        foreach (var row in rows)
        {
            var notNew = row is { WasFlagged: false, Now: { } now } && Names(now).All(flaggedBefore.Contains);
            var notShown = row is { Judged: true, WasFlagged: true, Outcome: RecheckOutcome.Fixed, ReadAsPage: true, Before: { } before } &&
                           !Names(before).All(SetNow(connection, transaction, row.UrlId).Contains);
            if (notNew || notShown)
            {
                using var update = Command(connection, transaction, "UPDATE recheck_urls SET outcome = $outcome WHERE recheck_id = $id AND url_id = $url",
                    ("$outcome", (int)RecheckOutcome.NotChecked), ("$id", id), ("$url", row.UrlId));
                update.ExecuteNonQuery();
            }
        }
    }

    /// <summary>The cookies an issue's detail names: "sessionid: no HttpOnly; theme: no Secure".</summary>
    private static IEnumerable<string> Names(string detail) =>
        detail.Split("; ", StringSplitOptions.RemoveEmptyEntries).Select(fault => fault.Split(": ", 2)[0]).Where(name => name.Length > 0);

    /// <summary>The cookies a URL's response sets now, as the check names them.</summary>
    private static HashSet<string> SetNow(SqliteConnection connection, SqliteTransaction transaction, int urlId)
    {
        using var command = Command(connection, transaction, "SELECT data FROM response_details WHERE url_id = $url", ("$url", urlId));
        if (command.ExecuteScalar() is not byte[] data)
        {
            return [];
        }

        return BlobCodec.DecodeDetails(data).Headers
            .Where(header => header.Name == "set-cookie")
            .Select(header => header.Value.Split(';')[0].Split('=', 2)[0].Trim())
            .Where(name => name.Length > 0)
            .Select(name => RuleText.Cut(name, 40))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static List<string> Texts(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
    {
        List<string> texts = [];
        using var command = Command(connection, transaction, sql, parameters);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
            {
                texts.Add(reader.GetString(0));
            }
        }

        return texts;
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command;
    }
}
