using System.Text;
using System.Xml;
using ShoutingIguana.Engine.Sitemaps;
using ShoutingIguana.Engine.Sitemaps.Models;

namespace ShoutingIguana.Engine.Tests.Sitemaps;

public sealed class SitemapReaderTests
{
    [Fact]
    public async Task ReadsUrlsetAndIgnoresImageLocations()
    {
        var entries = await ReadAsync("""
            <?xml version="1.0" encoding="UTF-8"?>
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:image="http://www.google.com/schemas/sitemap-image/1.1">
              <url><loc> https://example.com/a </loc><lastmod>2026-01-01</lastmod>
                <image:image><image:loc>https://example.com/a.jpg</image:loc></image:image></url>
              <url><loc>https://example.com/b</loc></url>
            </urlset>
            """);

        Assert.Equal([new SitemapEntry("https://example.com/a", false), new SitemapEntry("https://example.com/b", false)], entries);
    }

    [Fact]
    public async Task ReadsSitemapIndex()
    {
        var entries = await ReadAsync("""
            <sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
              <sitemap><loc>https://example.com/pages.xml</loc></sitemap>
              <sitemap><loc>https://example.com/posts.xml</loc></sitemap>
            </sitemapindex>
            """);

        Assert.All(entries, entry => Assert.True(entry.IsSitemap));
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public async Task ReadsTextSitemaps()
    {
        var entries = await ReadAsync("https://example.com/a\n\nhttps://example.com/b\n", isText: true);

        Assert.Equal(["https://example.com/a", "https://example.com/b"], entries.Select(entry => entry.Location));
    }

    [Fact]
    public async Task MalformedXmlThrows() =>
        await Assert.ThrowsAsync<XmlException>(() => ReadAsync("<urlset><url><loc>https://example.com/a</loc></urlset>"));

    private static async Task<List<SitemapEntry>> ReadAsync(string content, bool isText = false)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        List<SitemapEntry> entries = [];
        await foreach (var entry in SitemapReader.ReadAsync(stream, isText, CancellationToken.None))
        {
            entries.Add(entry);
        }

        return entries;
    }
}
