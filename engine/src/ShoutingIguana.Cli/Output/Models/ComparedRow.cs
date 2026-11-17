using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Cli.Output.Models;

/// <summary>A row a check flags now, with how it moved since the other crawl (null when the comparison is counts only).</summary>
internal sealed record ComparedRow(int UrlId, string Url, string? Detail, RowChange? Change);
