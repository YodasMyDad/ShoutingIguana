using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// Settles the rechecks an analysis is the first to follow (the fix loop's "4 of 5 fixed"), in the analysis's own
/// transaction, from the issues it is storing: each URL a recheck set out to judge gets its outcome, and the recheck its
/// counts, the score after it and when it was settled. Once settled, a recheck is a record of what it found, and no later
/// analysis works it out again. One whose run was cut short (its row still says running, and nothing has the file open,
/// as the analysis's lock proves) is settled as interrupted, with what it fetched.
/// <list type="bullet">
/// <item><b>Fixed</b>: let go as no longer linked, or read again in a way the check can judge (<see cref="CheckJudging"/>),
/// and not flagged.</item>
/// <item><b>Still</b>: flagged now.</item>
/// <item><b>Not checked</b>: read again, but not in a way the check can judge (an error in place of a page, a block, a
/// redirect away), or the analysis skipped the check. Never fixed.</item>
/// <item><b>Not rechecked</b>: past the cap, or not reached before the run ended.</item>
/// <item><b>New</b>: a URL in its scope the check didn't flag before and flags now.</item>
/// </list>
/// The cookie check is settled by cookie rather than by page (<see cref="RecheckCookies"/>): a recheck's fresh session
/// cookie lands on another page than the crawl's did.
/// </summary>
internal static class RecheckSettlement
{
    /// <param name="skipped">The checks this analysis skipped.</param>
    /// <param name="score">The score this analysis gives, or null without one.</param>
    public static void Settle(SqliteConnection connection, SqliteTransaction transaction, IEnumerable<string> skipped, int? score)
    {
        if (Scalar(connection, transaction, "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'rechecks'") is null)
        {
            return;
        }

        var skippedChecks = skipped.ToHashSet(StringComparer.Ordinal);

        // How the site answered each URL, kept by this engine's rechecks: a file an earlier engine rechecked has no column for it.
        var hasAnswers = Scalar(connection, transaction, "SELECT 1 FROM pragma_table_info('recheck_urls') WHERE name = 'answer'") is not null;
        List<(int Id, string? CheckId, long StartedAt, RecheckStatus Status, string Kinds, int Judged)> unsettled = [];
        using (var command = Command(connection, transaction, "SELECT id, check_id, started_at, status, kinds, judged FROM rechecks WHERE settled_at IS NULL ORDER BY id"))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                unsettled.Add((reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetInt64(2), (RecheckStatus)reader.GetInt32(3),
                    reader.GetString(4), reader.GetInt32(5)));
            }
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var recheck in unsettled)
        {
            var status = recheck.Status == RecheckStatus.Running ? RecheckStatus.Interrupted : recheck.Status;
            var check = recheck.CheckId is null ? null : IssueCatalog.Find(recheck.CheckId);
            SetOutcomes(connection, transaction, recheck.Id, recheck.StartedAt, check, check is not null && skippedChecks.Contains(check.Id),
                fetchesUrls: recheck.Kinds.Split(',').Contains(nameof(RecheckKind.Urls)), readOnlyCompleted: status == RecheckStatus.Completed, hasAnswers);
            if (check == IssueCatalog.COOKIES_INSECURE && !skippedChecks.Contains(check.Id))
            {
                RecheckCookies.Settle(connection, transaction, recheck.Id, recheck.StartedAt);
            }

            using var counts = Command(connection, transaction, $"""
                UPDATE rechecks SET
                    status = $status,
                    fixed = (SELECT COUNT(*) FROM recheck_urls WHERE recheck_id = $id AND outcome = {(int)RecheckOutcome.Fixed}),
                    still = (SELECT COUNT(*) FROM recheck_urls WHERE recheck_id = $id AND outcome = {(int)RecheckOutcome.Still}),
                    not_checked = (SELECT COUNT(*) FROM recheck_urls WHERE recheck_id = $id AND outcome = {(int)RecheckOutcome.NotChecked}),
                    not_rechecked = (SELECT COUNT(*) FROM recheck_urls WHERE recheck_id = $id AND outcome = {(int)RecheckOutcome.NotRechecked})
                        + MAX(0, judged - (SELECT COUNT(*) FROM recheck_urls WHERE recheck_id = $id AND role = {(int)RecheckRole.Judged})),
                    new = (SELECT COUNT(*) FROM recheck_urls WHERE recheck_id = $id AND outcome = {(int)RecheckOutcome.New})
                        + (SELECT COUNT(*) FROM recheck_urls ru WHERE ru.recheck_id = $id AND ru.role <> {(int)RecheckRole.Judged} AND ru.flagged_before = 0
                           AND ru.outcome IS NULL AND EXISTS (SELECT 1 FROM issues i WHERE i.issue = $check AND i.url_id = ru.url_id)),
                    score_after = $score,
                    settled_at = $now
                WHERE id = $id
                """, ("$status", (int)status), ("$id", recheck.Id), ("$check", (object?)check?.Id ?? DBNull.Value), ("$score", (object?)score ?? DBNull.Value),
                ("$now", now));
            counts.ExecuteNonQuery();
        }
    }

    /// <summary>The outcome of each URL the recheck judged, as the analysis now finds it.</summary>
    /// <param name="fetchesUrls">The recheck read its URLs again; one that read only sitemaps, robots.txt or the site's versions didn't.</param>
    /// <param name="readOnlyCompleted">For a recheck that didn't read its URLs again, it read what it does read in full.</param>
    /// <param name="hasAnswers">
    /// The file keeps how the site answered each URL: for a recheck that didn't read its URLs again, one whose robots.txt
    /// or sitemap reads got no answer couldn't be judged, so it's not checked, never fixed.
    /// </param>
    private static void SetOutcomes(SqliteConnection connection, SqliteTransaction transaction, int id, long startedAt, IssueDefinition? check, bool skipped,
        bool fetchesUrls, bool readOnlyCompleted, bool hasAnswers)
    {
        if (check is null)
        {
            return;
        }

        var judging = CheckJudging.Of(check);
        var judges = string.Join(", ", Enum.GetValues<UrlReading>().Where(reading => CheckJudging.Judges(judging, reading)).Select(reading => (int)reading));
        var rechecked = fetchesUrls
            ? "COALESCE((SELECT r2.fetched_at FROM responses r2 WHERE r2.url_id = recheck_urls.url_id), 0) >= $startedAt"
            : readOnlyCompleted ? "1" : "0";
        // A URL it fetched that the site turned away, where the crawl had read it, kept the crawl's record (and the issues on
        // it): it was asked and refused, so it's not checked, never fixed.
        var refusals = $"{(int)RecheckAnswer.RateLimited}, {(int)RecheckAnswer.Blocked}, {(int)RecheckAnswer.RobotsTxt}, {(int)RecheckAnswer.NoAnswer}";
        var unanswered = !hasAnswers ? string.Empty
            : fetchesUrls ? $"WHEN recheck_urls.answer IN ({refusals}) AND NOT ({rechecked}) THEN {(int)RecheckOutcome.NotChecked}"
            : $"WHEN recheck_urls.answer IN ({refusals}) THEN {(int)RecheckOutcome.NotChecked}";
        var reading = $"(SELECT {CheckJudging.READING} FROM urls u LEFT JOIN responses r ON r.url_id = u.id WHERE u.id = recheck_urls.url_id)";

        // A duplicate no longer flagged though its own text is as it was: the others changed, and a page the recheck didn't
        // fetch may share it now. Only a full crawl can say it's fixed. A page judged had the text its cluster shares, so
        // one with none before was fetched before its text was stored (a share title, by an earlier engine): it can't be
        // said to have changed either.
        var unchanged = RecheckDuplicates.TextOf(check.Id) is { } text
            ? $"WHEN {reading} IN ({judges}) AND (text_before IS NULL OR text_before IS (SELECT {text} FROM responses r WHERE r.url_id = recheck_urls.url_id)) THEN {(int)RecheckOutcome.NotChecked}"
            : string.Empty;
        using var command = Command(connection, transaction, $"""
            UPDATE recheck_urls SET outcome = CASE
                WHEN $skipped = 1 THEN {(int)RecheckOutcome.NotChecked}
                WHEN {reading} = {(int)UrlReading.NotFound} THEN CASE WHEN flagged_before = 1 THEN {(int)RecheckOutcome.Fixed} END
                {unanswered}
                WHEN NOT ({rechecked}) THEN {(int)RecheckOutcome.NotRechecked}
                WHEN EXISTS (SELECT 1 FROM issues i WHERE i.issue = $check AND i.url_id = recheck_urls.url_id)
                    THEN CASE WHEN flagged_before = 1 THEN {(int)RecheckOutcome.Still} ELSE {(int)RecheckOutcome.New} END
                {unchanged}
                WHEN {reading} IN ({judges}) THEN CASE WHEN flagged_before = 1 THEN {(int)RecheckOutcome.Fixed} END
                ELSE {(int)RecheckOutcome.NotChecked}
            END
            WHERE recheck_id = $id AND role = {(int)RecheckRole.Judged}
            """, ("$skipped", skipped ? 1 : 0), ("$check", check.Id), ("$id", id), ("$startedAt", startedAt));
        command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = Command(connection, transaction, sql);
        return command.ExecuteScalar();
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
