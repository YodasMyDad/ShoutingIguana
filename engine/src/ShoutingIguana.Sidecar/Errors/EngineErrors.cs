using Microsoft.Data.Sqlite;

namespace ShoutingIguana.Sidecar.Errors;

/// <summary>
/// The engine's exceptions that a user can cause, as the CLI treats them (<c>CommandErrors</c>): their messages are
/// written for users. Each call site turns them into the problem that fits what it was doing.
/// </summary>
internal static class EngineErrors
{
    /// <summary>What the CLI says for SQLite error 26, which is what opening anything but a database gives.</summary>
    public const string NOT_A_CRAWL = "That file isn't a Shouting Iguana crawl.";

    public static bool IsExpected(Exception exception) =>
        exception is InvalidOperationException or SqliteException or IOException or UnauthorizedAccessException or ArgumentException { ParamName: "path" };

    /// <summary>The message to show for an expected exception.</summary>
    public static string Message(Exception exception) => exception is SqliteException { SqliteErrorCode: 26 } ? NOT_A_CRAWL : exception.Message;

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
        SqliteException { SqliteErrorCode: 26 } => Problems.NotACrawl(NOT_A_CRAWL, exception.Message),
        InvalidOperationException => Problems.NotACrawl(exception.Message),
        FileNotFoundException or DirectoryNotFoundException => Problems.FileError("The crawl file isn't there any more.", exception.Message),
        _ => Problems.FileError(exception.Message)
    };
}
