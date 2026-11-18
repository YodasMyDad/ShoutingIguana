using System.Globalization;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>Which of a check's sheet's columns hold anything on the last analysis (<see cref="IssueSheetColumns"/>).</summary>
public static class IssueSheetReader
{
    public static IssueSheetColumns Read(string databasePath, string checkId, ReadCache? cache = null, CancellationToken cancellationToken = default) =>
        GridReadContext.Read(databasePath, cache, cancellationToken, context => context.Cached($"sheet-columns|{checkId}", () =>
        {
            bool Any(string where)
            {
                using var command = context.Command($"SELECT EXISTS (SELECT 1 FROM issues i {where})", [new("$check", checkId)]);
                return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
            }

            const string RESPONSE = "JOIN responses r ON r.url_id = i.url_id WHERE i.issue = $check";
            return new IssueSheetColumns(
                Any($"{RESPONSE} AND r.title IS NOT NULL AND r.title <> ''"),
                Any("WHERE i.issue = $check AND i.detail IS NOT NULL AND i.detail <> ''"),
                Any("WHERE i.issue = $check AND i.related_url_id IS NOT NULL"),
                !context.Summary.Available || Any("JOIN url_summary s ON s.url_id = i.url_id WHERE i.issue = $check AND s.final_url_id IS NOT NULL AND s.final_url_id <> i.url_id"),
                Any($"{RESPONSE} AND r.canonical_url_id IS NOT NULL AND r.canonical_url_id <> i.url_id"));
        }));
}
