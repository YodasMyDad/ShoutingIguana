namespace ShoutingIguana.Engine.Fetching.Models;

/// <summary>
/// A script or stylesheet a rendered page asked for, kept for the crawl's later pages. The body is as the browser
/// reads it, already decompressed, so the headers leave out the ones that describe how it was sent.
/// </summary>
internal sealed record CachedResponse(int Status, IReadOnlyList<KeyValuePair<string, string>> Headers, byte[] Body);
