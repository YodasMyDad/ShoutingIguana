namespace ShoutingIguana.Engine.Favicons.Models;

/// <summary>What some bytes are, as their own first bytes say: an image of a kind the engine keeps as a site's icon.</summary>
/// <param name="ContentType">The media type the image is served as: one of <see cref="FaviconSniffer.CONTENT_TYPES"/>.</param>
/// <param name="Width">Its width in pixels; an icon file's largest image.</param>
/// <param name="Height">Its height in pixels.</param>
internal sealed record FaviconImage(string ContentType, int Width, int Height);
