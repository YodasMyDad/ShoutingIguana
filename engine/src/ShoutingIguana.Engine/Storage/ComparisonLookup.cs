using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The point lookups a comparison makes in one crawl file, each an index seek on a statement prepared once: a URL by its
/// text (<c>urls.url</c> is UNIQUE) with what the crawl made of it, a URL's text by its id, and one check's issue on a URL.
/// </summary>
internal sealed class ComparisonLookup(GridReadContext context) : IDisposable
{
    private SqliteCommand? _find;
    private SqliteCommand? _text;
    private SqliteCommand? _issue;

    /// <summary>The URL with this text, or null when the crawl doesn't have it.</summary>
    public FoundUrl? Find(string url)
    {
        _find ??= context.Command($"""
            SELECT u.id, {CheckJudging.READING}, r.outcome, r.status_code, r.error, r.block_reason, u.state, u.skip_reason
            FROM urls u LEFT JOIN responses r ON r.url_id = u.id
            WHERE u.url = $url
            """, [new("$url", string.Empty)]);
        _find.Parameters["$url"].Value = url;
        context.Token.ThrowIfCancellationRequested();
        using var reader = _find.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new FoundUrl(
            reader.GetInt32(0),
            (UrlReading)reader.GetInt32(1),
            reader.IsDBNull(2) ? null : (FetchOutcome)reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            (UrlState)reader.GetInt32(6),
            (SkipReason)reader.GetInt32(7));
    }

    /// <summary>The text of the URL with this id; null for an id the crawl doesn't have.</summary>
    public string? TextOf(int urlId)
    {
        _text ??= context.Command("SELECT url FROM urls WHERE id = $id", [new("$id", 0)]);
        _text.Parameters["$id"].Value = urlId;
        context.Token.ThrowIfCancellationRequested();
        return _text.ExecuteScalar() as string;
    }

    /// <summary>Whether the check flagged the URL, and the issue's detail when it did.</summary>
    public (bool Flagged, string? Detail) IssueOn(string checkId, int urlId)
    {
        _issue ??= context.Command("SELECT detail FROM issues WHERE issue = $check AND url_id = $id", [new("$check", string.Empty), new("$id", 0)]);
        _issue.Parameters["$check"].Value = checkId;
        _issue.Parameters["$id"].Value = urlId;
        context.Token.ThrowIfCancellationRequested();
        using var reader = _issue.ExecuteReader();
        return reader.Read() ? (true, reader.IsDBNull(0) ? null : reader.GetString(0)) : (false, null);
    }

    public void Dispose()
    {
        _find?.Dispose();
        _text?.Dispose();
        _issue?.Dispose();
    }
}
