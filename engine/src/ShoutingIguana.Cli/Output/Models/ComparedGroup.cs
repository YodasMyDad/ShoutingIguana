using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Cli.Output.Models;

/// <summary>A cause of a check now, with how it moved since the other crawl (null when the comparison is counts only).</summary>
internal sealed record ComparedGroup(string? Detail, string? RelatedUrl, long Urls, GroupChange? Change);
