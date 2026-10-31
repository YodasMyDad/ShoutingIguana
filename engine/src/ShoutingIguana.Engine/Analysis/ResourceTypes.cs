namespace ShoutingIguana.Engine.Analysis;

/// <summary>
/// Tells files (images, video, audio, fonts, stylesheets, scripts) from pages. A fetched URL is judged by its content
/// type; one that wasn't fetched, as nofollow targets usually aren't, by the extension of its path. PDFs and anything
/// unknown count as pages, since they can be indexed.
/// </summary>
internal static class ResourceTypes
{
    private static readonly HashSet<string> EXTENSIONS = new(StringComparer.OrdinalIgnoreCase)
    {
        "jpg", "jpeg", "png", "gif", "webp", "avif", "svg", "bmp", "ico", "tif", "tiff", "heic", "heif", "jxl",
        "mp4", "m4v", "webm", "mov", "avi", "mkv", "ogv", "mp3", "m4a", "wav", "ogg", "oga", "opus", "aac", "flac",
        "woff", "woff2", "ttf", "otf", "eot", "css", "js", "mjs"
    };

    private static readonly string[] MEDIA_TYPE_PREFIXES = ["image/", "video/", "audio/", "font/"];

    private static readonly HashSet<string> MEDIA_TYPES = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/css", "text/javascript", "application/javascript", "application/x-javascript", "application/ecmascript",
        "application/font-woff", "application/x-font-woff", "application/x-font-ttf", "application/x-font-otf",
        "application/vnd.ms-fontobject"
    };

    /// <param name="contentType">The response's Content-Type when the URL was fetched successfully, otherwise null.</param>
    public static bool IsResource(string url, string? contentType) =>
        contentType is null ? HasResourceExtension(url) : IsResourceMediaType(contentType);

    private static bool IsResourceMediaType(string contentType)
    {
        var mediaType = contentType.AsSpan();
        var parameters = mediaType.IndexOf(';');
        mediaType = (parameters < 0 ? mediaType : mediaType[..parameters]).Trim();
        foreach (var prefix in MEDIA_TYPE_PREFIXES)
        {
            if (mediaType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return MEDIA_TYPES.GetAlternateLookup<ReadOnlySpan<char>>().Contains(mediaType);
    }

    /// <summary>The extension of the path's last segment; the query doesn't count (/download?file=a.jpg is a page).</summary>
    private static bool HasResourceExtension(string url)
    {
        var path = url.AsSpan();
        var end = path.IndexOfAny('?', '#');
        if (end >= 0)
        {
            path = path[..end];
        }

        var segment = path[(path.LastIndexOf('/') + 1)..];
        var dot = segment.LastIndexOf('.');
        return dot >= 0 && EXTENSIONS.GetAlternateLookup<ReadOnlySpan<char>>().Contains(segment[(dot + 1)..]);
    }
}
