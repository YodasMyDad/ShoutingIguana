namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>The site's icon as the crawl fetched it, for the writer to keep: bytes already told to be an image the engine keeps.</summary>
/// <param name="Url">Where the bytes came from, after any redirects.</param>
/// <param name="ContentType">What the bytes are, by their own signature.</param>
internal sealed record FaviconRecord(string Url, string ContentType, int Width, int Height, byte[] Body, long FetchedAt);
