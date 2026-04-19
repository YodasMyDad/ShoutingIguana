using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ShoutingIguana.Data;

/// <summary>
/// Applies SQLite performance pragmas to every connection as it is opened.
/// WAL mode is persisted in the database header so it only sticks on the first
/// successful open; the remaining pragmas are per-connection and must be re-applied.
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    private const string PragmaBatch =
        "PRAGMA journal_mode=WAL;" +
        "PRAGMA busy_timeout=5000;" +
        "PRAGMA synchronous=NORMAL;" +
        "PRAGMA cache_size=-32000;" +
        "PRAGMA temp_store=MEMORY;" +
        "PRAGMA foreign_keys=ON;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyPragmas(connection);
    }

    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ApplyPragmas(connection);
        return Task.CompletedTask;
    }

    private static void ApplyPragmas(DbConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = PragmaBatch;
        cmd.ExecuteNonQuery();
    }
}
