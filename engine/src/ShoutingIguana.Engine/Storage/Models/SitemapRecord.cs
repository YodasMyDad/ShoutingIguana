namespace ShoutingIguana.Engine.Storage.Models;

internal sealed record SitemapRecord(int Id, int? StatusCode, bool IsIndex, int UrlCount, string? Error);
