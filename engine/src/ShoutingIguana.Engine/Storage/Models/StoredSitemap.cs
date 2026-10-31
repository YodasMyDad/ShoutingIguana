namespace ShoutingIguana.Engine.Storage.Models;

/// <param name="Error">Why the sitemap could not be read, when it could not.</param>
internal sealed record StoredSitemap(string Url, int? StatusCode, bool IsIndex, int UrlCount, string? Error);
