using Microsoft.Data.Sqlite;

namespace ShoutingIguana.Cli.Commands;

/// <summary>
/// Turns the errors a user can cause (a file that isn't a crawl, a crawl that's still running, a path that can't be
/// written) into a one-line message and exit code 2. Anything else is a bug and keeps its stack trace.
/// </summary>
internal static class CommandErrors
{
    public static int Run(Func<int> action)
    {
        try
        {
            return action();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Report(exception);
        }
    }

    public static async Task<int> RunAsync(Func<Task<int>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Report(exception);
        }
    }

    // The path APIs reject a path the user typed (empty, invalid characters) with ArgumentException for "path".
    private static bool IsExpected(Exception exception) =>
        exception is InvalidOperationException or SqliteException or IOException or UnauthorizedAccessException or ArgumentException { ParamName: "path" };

    private static int Report(Exception exception)
    {
        Console.Error.WriteLine(exception is SqliteException { SqliteErrorCode: 26 }
            ? "That file isn't a Shouting Iguana crawl."
            : exception.Message);
        return 2;
    }
}
