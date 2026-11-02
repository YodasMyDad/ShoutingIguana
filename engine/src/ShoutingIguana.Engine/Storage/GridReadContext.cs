using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Configuration;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// One grid read: a reader's connection to a crawl file and a snapshot, held for that one page only, so a grid never keeps
/// a crawl or an analysis waiting. It knows whether the URL summary describes the crawl, and caches what it counts while
/// the crawl stands still.
/// </summary>
internal sealed class GridReadContext : IDisposable
{
    /// <summary>
    /// Each read opens its own connection, whose page cache starts empty; mapping the file reads its pages without a copy,
    /// which a scan of a million rows feels (a third quicker). SQLite caps the map at 2 GB; the rest is read as usual.
    /// </summary>
    private const long MMAP_BYTES = 2L * 1024 * 1024 * 1024;

    /// <summary>A sort of a million rows keeps its heap in memory rather than in a temporary file.</summary>
    private const int CACHE_KIB = 65_536;

    private readonly ReadCache? _cache;
    private CrawlOptions? _options;

    private GridReadContext(SqliteConnection connection, SqliteTransaction transaction, string path, ReadCache? cache)
    {
        Connection = connection;
        Transaction = transaction;
        Path = path;
        _cache = cache;
    }

    public SqliteConnection Connection { get; }

    public SqliteTransaction Transaction { get; }

    public string Path { get; }

    public SummaryState Summary { get; private set; } = SummaryState.Present;

    public GridMode Mode => Summary.Available ? GridMode.Summary : GridMode.Crawl;

    public AnalysisInfo? Analysis { get; private set; }

    public CrawlStatus Status { get; private set; }

    public string StartUrl { get; private set; } = string.Empty;

    /// <summary>The crawl has the analysis's indexability, where it judged a URL differently from the crawl.</summary>
    public bool HasEffectiveIndexability { get; private set; }

    /// <summary>The crawl's indexability reason in crawl SQL: the analysis's where it judged a URL differently.</summary>
    public string Reason => HasEffectiveIndexability ? "COALESCE(e.indexability_reason, r.indexability_reason)" : "r.indexability_reason";

    /// <summary>What counts are kept under; null while the crawl runs, as its counts change from one read to the next.</summary>
    public GridStamp? Stamp { get; private set; }

    /// <exception cref="InvalidOperationException">The file isn't a crawl, or was written with another schema.</exception>
    public static GridReadContext Open(string path, ReadCache? cache)
    {
        var connection = CrawlDatabase.OpenReader(path);
        SqliteTransaction? transaction = null;
        try
        {
            CrawlDatabase.Execute(connection, $"PRAGMA mmap_size = {MMAP_BYTES}; PRAGMA cache_size = -{CACHE_KIB};");
            ReaderFunctions.Register(connection);
            transaction = connection.BeginTransaction(deferred: true);
            var context = new GridReadContext(connection, transaction, path, cache);
            context.ReadState();
            return context;
        }
        catch
        {
            transaction?.Dispose();
            connection.Dispose();
            throw;
        }
    }

    /// <summary>A count, kept while the crawl stands still: per file, crawl stamp, analysis and mode.</summary>
    public long Count(string key, Func<long> count) => Cached(key, count);

    public T Cached<T>(string key, Func<T> read) where T : notnull =>
        Stamp is { } stamp && _cache is { } cache ? cache.GetOrAdd($"{stamp}|{key}", read) : read();

    /// <summary>As <see cref="Cached{T}"/>, for something large (an id set), of which only a few are kept.</summary>
    public T CachedLarge<T>(string key, Func<T> read) where T : notnull =>
        Stamp is { } stamp && _cache is { } cache ? cache.GetOrAddLarge($"{stamp}|{key}", read) : read();

    public SqliteCommand Command(string sql, IEnumerable<KeyValuePair<string, object>> parameters)
    {
        var command = Connection.CreateCommand();
        command.Transaction = Transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            if (!command.Parameters.Contains(name))
            {
                command.Parameters.AddWithValue(name, value);
            }
        }

        return command;
    }

    public long Scalar(string sql, IEnumerable<KeyValuePair<string, object>> parameters)
    {
        using var command = Command(sql, parameters);
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
    }

    /// <summary>The options the crawl ran with, read once; an option its engine didn't have takes its default.</summary>
    public CrawlOptions Options => _options ??= ReadOptions();

    /// <summary>The name the crawl's robots meta tags and headers could address the crawler by.</summary>
    public string RobotsToken => UserAgents.Resolve(Options.UserAgent, Options.CustomUserAgent).RobotsToken;

    private CrawlOptions ReadOptions()
    {
        using var command = Command("SELECT options_json FROM crawl WHERE id = 1", []);
        try
        {
            return command.ExecuteScalar() is string json
                ? StoredJson.Deserialize(json, EngineJsonContext.Default.CrawlOptions, new CrawlOptions { StartUrl = StartUrl }) ?? new CrawlOptions { StartUrl = StartUrl }
                : new CrawlOptions { StartUrl = StartUrl };
        }
        catch (JsonException)
        {
            // Options edited by hand: the engine's defaults are the best guess at how it ran.
            return new CrawlOptions { StartUrl = StartUrl };
        }
    }

    public void Dispose()
    {
        Transaction.Dispose();
        Connection.Dispose();
    }

    /// <summary>
    /// The crawl row, the last analysis, and whether the summary describes the crawl, from the snapshot. Why not, when it
    /// doesn't, is the first that applies: the crawl is running, was never analysed, was analysed by another engine, or
    /// was continued since.
    /// </summary>
    private void ReadState()
    {
        using (var command = Command("SELECT status, updated_at, start_url FROM crawl WHERE id = 1", []))
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read())
            {
                throw new InvalidOperationException("This file is not a crawl database.");
            }

            Status = (CrawlStatus)reader.GetInt32(0);
            var updatedAt = reader.GetInt64(1);
            StartUrl = reader.GetString(2);
            Analysis = IssueReader.ReadAnalysis(Connection, Transaction);
            if (!UrlSummaryReader.IsAvailable(Connection, Transaction))
            {
                Summary = new SummaryState(false, UnavailableReason());
            }

            HasEffectiveIndexability = IssueReader.HasTable(Connection, Transaction, "effective_indexability");
            Stamp = Status == CrawlStatus.Running ? null : new GridStamp(Path, updatedAt, Analysis?.AnalyzedAt.ToUnixTimeMilliseconds(), Mode);
        }
    }

    private SummaryUnavailableReason UnavailableReason()
    {
        // A file that says it's running is interrupted unless someone holds its lock.
        if (Status == CrawlStatus.Running && CrawlDatabase.IsInUse(Path))
        {
            return SummaryUnavailableReason.Crawling;
        }

        if (Analysis is null)
        {
            return SummaryUnavailableReason.NotAnalyzed;
        }

        return Analysis.HasUrlSummary && IssueReader.HasTable(Connection, Transaction, "url_summary")
            ? SummaryUnavailableReason.CrawlChanged
            : SummaryUnavailableReason.EngineChanged;
    }
}
