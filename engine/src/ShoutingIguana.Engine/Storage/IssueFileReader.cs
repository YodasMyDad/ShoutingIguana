using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The files the checks of the site's own robots.txt and sitemaps are about (<c>issue_files</c>), so a view can name the file
/// where the issue's row would name the home page it's reported on. An analysis before <see cref="CrawlSchema.ISSUE_FILES_SINCE"/>
/// stored none: its checks have no files, and views fall back to the page.
/// </summary>
public static class IssueFileReader
{
    /// <summary>The files of the last analysis's file checks, by check id; null for a crawl never analysed.</summary>
    public static AnalysisFiles? Read(string databasePath, ReadCache? cache = null, CancellationToken cancellationToken = default) =>
        GridReadContext.Read(databasePath, cache, cancellationToken, context => context.Analysis is { } analysis
            ? context.Cached("issue-files", () => new AnalysisFiles(analysis.AnalyzedAt, Read(context)))
            : null);

    /// <summary>The files of the analysis <paramref name="context"/> reads, by check id: empty for one that stored none.</summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<IssueFile>> Read(GridReadContext context)
    {
        Dictionary<string, List<IssueFile>> files = new(StringComparer.Ordinal);
        if (context.Analysis?.AnalysisVersion < CrawlSchema.ISSUE_FILES_SINCE || !IssueReader.HasTable(context.Connection, context.Transaction, "issue_files"))
        {
            return new Dictionary<string, IReadOnlyList<IssueFile>>(StringComparer.Ordinal);
        }

        using var command = context.Command("""
            SELECT f.issue, f.file, (SELECT s.id FROM sitemaps s WHERE s.url = f.file)
            FROM issue_files f ORDER BY f.issue, f.url_id, f.position
            """, []);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var check = reader.GetString(0);
            if (!files.TryGetValue(check, out var list))
            {
                files[check] = list = [];
            }

            list.Add(new IssueFile(reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt64(2)));
        }

        return files.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<IssueFile>)entry.Value, StringComparer.Ordinal);
    }
}
