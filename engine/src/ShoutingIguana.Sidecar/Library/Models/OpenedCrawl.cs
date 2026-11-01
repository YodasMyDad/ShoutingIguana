namespace ShoutingIguana.Sidecar.Library.Models;

/// <summary>A crawl file opened from elsewhere.</summary>
/// <param name="StartUrl">As it was when opened, so the library can still name the site if the file goes missing.</param>
internal sealed record OpenedCrawl(string Path, string? StartUrl);
