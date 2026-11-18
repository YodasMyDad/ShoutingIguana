namespace ShoutingIguana.Engine.Parsing.Models;

/// <param name="HasAlt">The alt attribute exists (an empty alt is valid for decorative images).</param>
public sealed record ImageFact(string? Url, string? Alt, bool HasAlt, string? Width, string? Height, bool HasSrcset, string? Loading)
{
    /// <summary>The image is a <c>data:</c> URI in its src, so it has no URL to fetch (collection 2; false for a page parsed before).</summary>
    public bool IsDataUri { get; init; }

    /// <summary>A <c>data:</c> image's size once decoded, in bytes; null for any other.</summary>
    public long? DataUriBytes { get; init; }

    /// <summary>The image is in a <c>&lt;picture&gt;</c> with a <c>&lt;source srcset&gt;</c>, which offers the browser other files.</summary>
    public bool InPicture { get; init; }

    /// <summary>
    /// An image without a URL's class attribute, whitespace collapsed and cut to 100 characters, which tells it apart where
    /// it has no file to name and no alt text (collection 6). Null with none, for an image with a URL, and for a page parsed before.
    /// </summary>
    public string? Class { get; init; }

    /// <summary>
    /// Where on the page an image without a URL is, as a link's place is judged (collection 6). Null for an image with a URL,
    /// and for a page parsed before.
    /// </summary>
    public LinkPosition? Position { get; init; }
}
