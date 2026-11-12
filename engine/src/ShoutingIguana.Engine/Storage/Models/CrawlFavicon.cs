namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// The site's icon a crawl kept (<see cref="FaviconReader"/>): an image a browser draws without running anything, told
/// by its own bytes as it's read from the file.
/// </summary>
/// <param name="Url">Where the crawl fetched it from, after any redirects.</param>
/// <param name="ContentType">What the bytes are: <c>image/png</c>, <c>image/x-icon</c>, <c>image/gif</c>, <c>image/jpeg</c> or <c>image/webp</c>.</param>
/// <param name="Width">Its width in pixels; an icon file's largest image.</param>
/// <param name="Height">Its height in pixels.</param>
/// <param name="Bytes">The image, as the site sent it.</param>
/// <param name="FetchedAt">When the crawl fetched it.</param>
public sealed record CrawlFavicon(string Url, string ContentType, int Width, int Height, byte[] Bytes, DateTimeOffset FetchedAt);
