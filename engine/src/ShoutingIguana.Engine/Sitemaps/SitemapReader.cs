using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using ShoutingIguana.Engine.Sitemaps.Models;

namespace ShoutingIguana.Engine.Sitemaps;

/// <summary>
/// Streams sitemap entries without loading the file into memory: XML urlsets, sitemap indexes, and plain
/// text sitemaps. Only <c>loc</c> elements in the sitemap namespace count, so image and video extension
/// locations are ignored. DTDs are ignored and documents are size-capped, which blocks entity and
/// decompression bombs. A file announced as text that starts with markup is read as XML, since servers often
/// send sitemap.xml as text/plain.
/// </summary>
internal static class SitemapReader
{
    private const long MAX_DOCUMENT_CHARS = 200_000_000;
    private const int MAX_TEXT_LINES = 100_000;

    /// <summary>The sitemaps protocol caps a URL at 2,048 characters; longer text lines are skipped, not buffered.</summary>
    private const int MAX_TEXT_LINE_CHARS = 4_096;

    /// <summary>How far to look for the first character when deciding whether "text" is really XML.</summary>
    private const int SNIFF_BYTES = 4_096;

    public static async IAsyncEnumerable<SitemapEntry> ReadAsync(
        Stream stream,
        bool isText,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // XmlReader reads without a token, so a sitemap that stalls part-way would otherwise outlast the timeout and a stop.
        stream = new CancellableReadStream(stream, cancellationToken);
        if (isText)
        {
            var pipe = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
            isText = !await StartsWithMarkupAsync(pipe, cancellationToken).ConfigureAwait(false);
            stream = pipe.AsStream();
        }

        if (isText)
        {
            await foreach (var line in ReadTextLinesAsync(stream, cancellationToken).ConfigureAwait(false))
            {
                yield return new SitemapEntry(line, IsSitemap: false);
            }

            yield break;
        }

        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            IgnoreProcessingInstructions = true,
            MaxCharactersInDocument = MAX_DOCUMENT_CHARS
        };

        using var xml = XmlReader.Create(stream, settings);
        var rootSeen = false;
        var isIndex = false;

        // An entry's <loc> and <lastmod> can come in either order: it's given when its <url> or <sitemap> ends (or the
        // next one starts, or the file does), at the depth its <loc> was found.
        string? location = null;
        string? lastmod = null;
        var hasExtension = false;
        var entryDepth = -1;

        while (!xml.EOF)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (location is not null && xml.Depth <= entryDepth && xml.NodeType is XmlNodeType.EndElement or XmlNodeType.Element)
            {
                yield return new SitemapEntry(location, isIndex, lastmod, hasExtension);
                (location, lastmod, hasExtension, entryDepth) = (null, null, false, -1);
            }

            if (xml.NodeType == XmlNodeType.Element)
            {
                if (!rootSeen)
                {
                    rootSeen = true;
                    isIndex = xml.LocalName == "sitemapindex";
                }
                else if (xml.LocalName is "loc" or "lastmod" && IsSitemapNamespace(xml.NamespaceURI))
                {
                    var name = xml.LocalName;
                    var depth = xml.Depth - 1;

                    // ReadElementContentAsString already moves the reader past the element.
                    var text = (await xml.ReadElementContentAsStringAsync().ConfigureAwait(false)).Trim();
                    if (name == "loc" && text.Length > 0)
                    {
                        if (location is not null)
                        {
                            // A second <loc> in one entry, or one with no <url> around it: each is an entry of its own.
                            yield return new SitemapEntry(location, isIndex, lastmod, hasExtension);
                            (lastmod, hasExtension) = (null, false);
                        }

                        (location, entryDepth) = (text, depth);
                    }
                    else if (name == "lastmod" && text.Length > 0)
                    {
                        lastmod = text;
                    }

                    continue;
                }
                else if (IsExtensionNamespace(xml.NamespaceURI))
                {
                    hasExtension = true;
                }
            }

            if (!await xml.ReadAsync().ConfigureAwait(false))
            {
                break;
            }
        }

        if (location is not null)
        {
            yield return new SitemapEntry(location, isIndex, lastmod, hasExtension);
        }
    }

    /// <summary>Non-empty, trimmed lines, up to <see cref="MAX_TEXT_LINES"/>; a line longer than any URL is dropped.</summary>
    private static async IAsyncEnumerable<string> ReadTextLinesAsync(Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream);
        var buffer = new char[16 * 1024];
        var line = new StringBuilder();
        var isTooLong = false;
        var lines = 0;
        long total = 0;

        while (lines < MAX_TEXT_LINES && total < MAX_DOCUMENT_CHARS)
        {
            var read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            var atEnd = read == 0;
            total += read;
            for (var i = 0; i <= read && lines < MAX_TEXT_LINES; i++)
            {
                var endOfLine = i == read ? atEnd : buffer[i] is '\n' or '\r';
                if (!endOfLine)
                {
                    if (i < read && line.Length < MAX_TEXT_LINE_CHARS)
                    {
                        line.Append(buffer[i]);
                    }
                    else if (i < read)
                    {
                        isTooLong = true;
                    }

                    continue;
                }

                var location = isTooLong ? string.Empty : line.ToString().Trim();
                line.Clear();
                isTooLong = false;
                if (location.Length > 0)
                {
                    lines++;
                    yield return location;
                }
            }

            if (atEnd)
            {
                yield break;
            }
        }
    }

    /// <summary>Whether the first byte that isn't whitespace or part of a UTF-8 byte order mark is '&lt;'. Nothing is consumed.</summary>
    private static async Task<bool> StartsWithMarkupAsync(PipeReader pipe, CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await pipe.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = result.Buffer;
            var first = FirstSignificantByte(buffer);
            if (first is not null || result.IsCompleted || buffer.Length >= SNIFF_BYTES)
            {
                pipe.AdvanceTo(buffer.Start);
                return first == (byte)'<';
            }

            // Examined everything without consuming any of it, so the next read waits for more.
            pipe.AdvanceTo(buffer.Start, buffer.End);
        }
    }

    private static byte? FirstSignificantByte(ReadOnlySequence<byte> buffer)
    {
        foreach (var segment in buffer)
        {
            foreach (var value in segment.Span)
            {
                if (value is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or 0xEF or 0xBB or 0xBF))
                {
                    return value;
                }
            }
        }

        return null;
    }

    /// <summary>Google's image, video and news sitemap extensions: <c>http://www.google.com/schemas/sitemap-image/1.1</c> and its kin.</summary>
    private static bool IsExtensionNamespace(string namespaceUri) =>
        namespaceUri.Contains("/schemas/sitemap-image/", StringComparison.OrdinalIgnoreCase) ||
        namespaceUri.Contains("/schemas/sitemap-video/", StringComparison.OrdinalIgnoreCase) ||
        namespaceUri.Contains("/schemas/sitemap-news/", StringComparison.OrdinalIgnoreCase);

    private static bool IsSitemapNamespace(string namespaceUri) =>
        namespaceUri.Length == 0 ||
        namespaceUri.Contains("sitemaps.org", StringComparison.OrdinalIgnoreCase) ||
        namespaceUri.Contains("/schemas/sitemap/", StringComparison.OrdinalIgnoreCase);
}
