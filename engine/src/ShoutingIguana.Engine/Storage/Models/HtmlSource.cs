namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A page's HTML as the crawl stored it, or why there is none.</summary>
/// <param name="Bytes">The HTML's size once unpacked, before any cut.</param>
/// <param name="Truncated">It was cut at <see cref="UrlInspectorReader.MAX_SOURCE_BYTES"/>.</param>
public sealed record HtmlSource(string? Html, long? Bytes, bool Truncated, HtmlSourceMissing? Missing)
{
    public bool Stored => Html is not null;
}
