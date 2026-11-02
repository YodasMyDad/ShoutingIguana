using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Tests.Support;

/// <summary>Filters for the grid readers' tests, and the changes to a crawl file that take its URL summary away.</summary>
internal static class GridQueries
{
    public static UrlFilter Number(string column, FilterOperator op, double value) => new(column, op) { Value = FilterValue.Of(value) };

    public static UrlFilter Numbers(string column, FilterOperator op, params double[] values) => new(column, op) { Values = [.. values.Select(FilterValue.Of)] };

    public static UrlFilter Names(string column, FilterOperator op, params string[] values) => new(column, op) { Values = [.. values.Select(FilterValue.Of)] };

    public static UrlFilter Boolean(string column, bool value) => new(column, FilterOperator.Is) { Value = FilterValue.Of(value) };

    public static UrlFilter Text(string column, FilterOperator op, string value) => new(column, op) { Value = FilterValue.Of(value) };

    /// <summary>What a crawl continued after its analysis leaves: the crawl row stamped again.</summary>
    public static void Stamp(string path) => Change(path, "UPDATE crawl SET updated_at = updated_at + 1000");

    public static void Change(string path, string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }
}
