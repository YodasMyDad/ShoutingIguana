using System.IO.Pipelines;
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

        Assert.Equal([new SitemapEntry("https://example.com/a", false, "2026-01-01"), new SitemapEntry("https://example.com/b", false)], entries);
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
    public async Task ReadsTextSitemapsWithWindowsLineEndings()
    {
        var entries = await ReadAsync("https://example.com/a\r\nhttps://example.com/b", isText: true);

        Assert.Equal(["https://example.com/a", "https://example.com/b"], entries.Select(entry => entry.Location));
    }

    [Theory]
    [InlineData("\uFEFF<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">")]
    [InlineData("\n   <urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">")]
    public async Task XmlSentAsTextIsReadAsXml(string start)
    {
        var entries = await ReadAsync($"{start}\n<url><loc>https://example.com/a</loc></url>\n<url><loc>https://example.com/b</loc></url>\n</urlset>", isText: true);

        Assert.Equal(["https://example.com/a", "https://example.com/b"], entries.Select(entry => entry.Location));
    }

    [Fact]
    public async Task TextLinesLongerThanAnyUrlAreSkipped()
    {
        var longUrl = "https://example.com/" + new string('a', 2_000);

        var entries = await ReadAsync($"{new string('x', 200_000)}\n{longUrl}\nhttps://example.com/b\n", isText: true);

        Assert.Equal([longUrl, "https://example.com/b"], entries.Select(entry => entry.Location));
    }

    [Fact]
    public async Task MalformedXmlThrows() =>
        await Assert.ThrowsAsync<XmlException>(() => ReadAsync("<urlset><url><loc>https://example.com/a</loc></urlset>"));

    /// <summary>A sitemap that sends one URL and then nothing more, never closing: cancelling ends the read.</summary>
    /// <remarks>
    /// XML is read by XmlReader, which reads its stream without a token, so only <see cref="CancellableReadStream"/> lets
    /// that case end. Text is read with the token on every read already, so that case guards the text reader itself: it
    /// passes without the wrap, and fails once the text reader's own reads lose the token too.
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AStalledSitemapEndsWhenCancelled(bool isText)
    {
        var pipe = new Pipe();
        var start = isText ? "https://example.com/a\n" : "<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"><url><loc>https://example.com/a</loc></url>";
        await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(start));
        using var cancel = new CancellationTokenSource();
        var first = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var reading = Task.Run(async () =>
        {
            await foreach (var entry in SitemapReader.ReadAsync(pipe.Reader.AsStream(), isText, cancel.Token))
            {
                first.TrySetResult(entry.Location);
            }
        });

        Assert.Equal("https://example.com/a", await first.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading.WaitAsync(TimeSpan.FromSeconds(5)));
    }

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
