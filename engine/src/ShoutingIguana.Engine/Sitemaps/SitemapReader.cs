using System.Runtime.CompilerServices;
using System.Xml;
using ShoutingIguana.Engine.Sitemaps.Models;

namespace ShoutingIguana.Engine.Sitemaps;

/// <summary>
/// Streams sitemap entries without loading the file into memory: XML urlsets, sitemap indexes, and plain
/// text sitemaps. Only <c>loc</c> elements in the sitemap namespace count, so image and video extension
/// locations are ignored. DTDs are ignored and documents are size-capped, which blocks entity and
/// decompression bombs.
/// </summary>
internal static class SitemapReader
{
    private const long MAX_DOCUMENT_CHARS = 200_000_000;
    private const int MAX_TEXT_LINES = 100_000;

    public static async IAsyncEnumerable<SitemapEntry> ReadAsync(
        Stream stream,
        bool isText,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (isText)
        {
            using var lines = new StreamReader(stream);
            var count = 0;
            while (count < MAX_TEXT_LINES && await lines.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                var location = line.Trim();
                if (location.Length > 0)
                {
                    count++;
                    yield return new SitemapEntry(location, IsSitemap: false);
                }
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

        while (!xml.EOF)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (xml.NodeType == XmlNodeType.Element)
            {
                if (!rootSeen)
                {
                    rootSeen = true;
                    isIndex = xml.LocalName == "sitemapindex";
                }
                else if (xml.LocalName == "loc" && IsSitemapNamespace(xml.NamespaceURI))
                {
                    // ReadElementContentAsString already moves the reader past the element.
                    var location = (await xml.ReadElementContentAsStringAsync().ConfigureAwait(false)).Trim();
                    if (location.Length > 0)
                    {
                        yield return new SitemapEntry(location, isIndex);
                    }

                    continue;
                }
            }

            if (!await xml.ReadAsync().ConfigureAwait(false))
            {
                break;
            }
        }
    }

    private static bool IsSitemapNamespace(string namespaceUri) =>
        namespaceUri.Length == 0 ||
        namespaceUri.Contains("sitemaps.org", StringComparison.OrdinalIgnoreCase) ||
        namespaceUri.Contains("/schemas/sitemap/", StringComparison.OrdinalIgnoreCase);
}
