using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis;

/// <summary>
/// Tells files (images, video, audio, fonts, stylesheets, scripts) from pages. A fetched URL is judged by its content
/// type; one that wasn't fetched, as nofollow targets usually aren't, by the extension of its path. PDFs and anything
/// unknown count as pages, since they can be indexed.
/// </summary>
internal static class ResourceTypes
{
    private static readonly Dictionary<string, ResourceType> EXTENSIONS = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jpg"] = ResourceType.Image, ["jpeg"] = ResourceType.Image, ["png"] = ResourceType.Image, ["gif"] = ResourceType.Image,
        ["webp"] = ResourceType.Image, ["avif"] = ResourceType.Image, ["svg"] = ResourceType.Image, ["bmp"] = ResourceType.Image,
        ["ico"] = ResourceType.Image, ["tif"] = ResourceType.Image, ["tiff"] = ResourceType.Image, ["heic"] = ResourceType.Image,
        ["heif"] = ResourceType.Image, ["jxl"] = ResourceType.Image,
        ["mp4"] = ResourceType.Media, ["m4v"] = ResourceType.Media, ["webm"] = ResourceType.Media, ["mov"] = ResourceType.Media,
        ["avi"] = ResourceType.Media, ["mkv"] = ResourceType.Media, ["ogv"] = ResourceType.Media, ["mp3"] = ResourceType.Media,
        ["m4a"] = ResourceType.Media, ["wav"] = ResourceType.Media, ["ogg"] = ResourceType.Media, ["oga"] = ResourceType.Media,
        ["opus"] = ResourceType.Media, ["aac"] = ResourceType.Media, ["flac"] = ResourceType.Media,
        ["woff"] = ResourceType.Font, ["woff2"] = ResourceType.Font, ["ttf"] = ResourceType.Font, ["otf"] = ResourceType.Font,
        ["eot"] = ResourceType.Font,
        ["css"] = ResourceType.Stylesheet,
        ["js"] = ResourceType.Script, ["mjs"] = ResourceType.Script,
        ["pdf"] = ResourceType.Pdf
    };

    private static readonly (string Prefix, ResourceType Type)[] MEDIA_TYPE_PREFIXES =
        [("image/", ResourceType.Image), ("video/", ResourceType.Media), ("audio/", ResourceType.Media), ("font/", ResourceType.Font)];

    private static readonly Dictionary<string, ResourceType> MEDIA_TYPES = new(StringComparer.OrdinalIgnoreCase)
    {
        ["text/css"] = ResourceType.Stylesheet,
        ["text/javascript"] = ResourceType.Script, ["application/javascript"] = ResourceType.Script,
        ["application/x-javascript"] = ResourceType.Script, ["application/ecmascript"] = ResourceType.Script,
        ["application/font-woff"] = ResourceType.Font, ["application/x-font-woff"] = ResourceType.Font,
        ["application/x-font-ttf"] = ResourceType.Font, ["application/x-font-otf"] = ResourceType.Font,
        ["application/vnd.ms-fontobject"] = ResourceType.Font,
        ["text/html"] = ResourceType.Page, ["application/xhtml+xml"] = ResourceType.Page,
        ["application/pdf"] = ResourceType.Pdf
    };

    /// <param name="contentType">The response's Content-Type when the URL was fetched successfully, otherwise null.</param>
    public static bool IsResource(string url, string? contentType) =>
        Classify(url, contentType) is ResourceType.Image or ResourceType.Script or ResourceType.Stylesheet or ResourceType.Font or ResourceType.Media;

    /// <summary>What the URL is: by its content type when it was fetched successfully, otherwise by its extension.</summary>
    /// <param name="contentType">The response's Content-Type when the URL was fetched successfully, otherwise null.</param>
    public static ResourceType Classify(string url, string? contentType) =>
        contentType is null ? OfUrl(url) : OfContentType(contentType);

    /// <summary>What a successful response with this Content-Type is.</summary>
    public static ResourceType OfContentType(string contentType) => OfContentType(contentType.AsSpan());

    /// <inheritdoc cref="OfContentType(string)"/>
    public static ResourceType OfContentType(ReadOnlySpan<char> contentType)
    {
        var mediaType = contentType;
        var parameters = mediaType.IndexOf(';');
        mediaType = (parameters < 0 ? mediaType : mediaType[..parameters]).Trim();
        foreach (var (prefix, type) in MEDIA_TYPE_PREFIXES)
        {
            if (mediaType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return type;
            }
        }

        return MEDIA_TYPES.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(mediaType, out var known) ? known : ResourceType.Other;
    }

    /// <summary>
    /// What a URL without a successful response probably is, by the extension of its path's last segment; the query
    /// doesn't count (/download?file=a.jpg is a page).
    /// </summary>
    public static ResourceType OfUrl(string url) => OfUrl(url.AsSpan());

    /// <inheritdoc cref="OfUrl(string)"/>
    public static ResourceType OfUrl(ReadOnlySpan<char> url)
    {
        var path = url;
        var end = path.IndexOfAny('?', '#');
        if (end >= 0)
        {
            path = path[..end];
        }

        var segment = path[(path.LastIndexOf('/') + 1)..];
        var dot = segment.LastIndexOf('.');
        return dot >= 0 && EXTENSIONS.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(segment[(dot + 1)..], out var type)
            ? type
            : ResourceType.Page;
    }
}
