using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Storage;

namespace ShoutingIguana.Sidecar.Errors;

/// <summary>
/// The engine's exceptions that a user can cause, as the CLI treats them (<c>CommandErrors</c>): their messages are
/// written for users. Each call site turns them into the problem that fits what it was doing.
/// </summary>
internal static class EngineErrors
{
    /// <summary>What the CLI says for SQLite error 26, which is what opening anything but a database gives.</summary>
    public const string NOT_A_CRAWL = "That file isn't a Shouting Iguana crawl.";

    public const string LOCKED = "Another program has this crawl locked. Try again once it has finished.";

    private const int SQLITE_BUSY = 5;
    private const int SQLITE_LOCKED = 6;
    private const int SQLITE_CORRUPT = 11;
    private const int SQLITE_NOTADB = 26;

    public static bool IsExpected(Exception exception) =>
        exception is InvalidOperationException or SqliteException or IOException or UnauthorizedAccessException or ArgumentException { ParamName: "path" };

    /// <summary>The message to show for an expected exception.</summary>
    public static string Message(Exception exception) => exception is SqliteException { SqliteErrorCode: SQLITE_NOTADB } ? NOT_A_CRAWL : exception.Message;

    /// <summary>
    /// The file isn't a crawl this engine reads: not a database at all, not a crawl, or one with another schema. That
    /// lasts until the file changes. (Readers throw <see cref="InvalidOperationException"/> for nothing else.)
    /// </summary>
    public static bool IsNotACrawl(Exception exception) =>
        exception is SqliteException { SqliteErrorCode: SQLITE_NOTADB } or (InvalidOperationException and not CrawlFileInUseException);

    /// <summary>
    /// A file that couldn't be read for a reason that passes, such as a lock another program holds, rather than one that
    /// lasts until the file changes: it isn't a crawl, it is damaged, or reading it took too long.
    /// </summary>
    public static bool IsPassing(Exception exception) =>
        !IsNotACrawl(exception) && exception is not (OperationCanceledException or SqliteException { SqliteErrorCode: SQLITE_CORRUPT });

    /// <summary>Why the library can't show a file that may be a crawl, in words for the UI.</summary>
    public static string UnreadableMessage(Exception exception) => exception switch
    {
        OperationCanceledException => "This file took too long to read, so it may be damaged or not be a crawl. Open it again to retry.",
        SqliteException { SqliteErrorCode: SQLITE_CORRUPT } => "This file is damaged, so it can't be read as a crawl.",
        SqliteException { SqliteErrorCode: SQLITE_BUSY or SQLITE_LOCKED } => LOCKED,
        _ => exception.Message
    };

    /// <summary>
    /// Reads from a crawl file. Readers never take the writers' lock, so what can go wrong is the file: it isn't a crawl
    /// (or is one from another schema), or it can't be read.
    /// </summary>
    public static T Read<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            throw ForRead(exception);
        }
    }

    public static ProblemException ForRead(Exception exception) => exception switch
    {
        SqliteException { SqliteErrorCode: SQLITE_NOTADB } => Problems.NotACrawl(NOT_A_CRAWL, exception.Message),
        SqliteException { SqliteErrorCode: SQLITE_BUSY or SQLITE_LOCKED } => Problems.CrawlBusy(LOCKED),
        CrawlFileInUseException => Problems.CrawlBusy(exception.Message),
        InvalidOperationException => Problems.NotACrawl(exception.Message),
        FileNotFoundException or DirectoryNotFoundException => Problems.FileError("The crawl file isn't there any more.", exception.Message),
        _ => Problems.FileError(exception.Message)
    };
}
