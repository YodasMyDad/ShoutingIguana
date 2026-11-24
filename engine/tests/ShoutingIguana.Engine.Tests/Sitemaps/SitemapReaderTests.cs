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

        // The image's own location is no entry; the entry that carries it says so, as an image sitemap's entries all do.
        Assert.Equal([new SitemapEntry("https://example.com/a", false, "2026-01-01", HasExtension: true), new SitemapEntry("https://example.com/b", false)], entries);
    }

    /// <summary>
    /// The fourth pass's case (freebsdfoundation.org, servercore.com): All in One SEO declares a <c>sitemap.rss</c> in
    /// robots.txt beside its <c>sitemap.xml</c>, an RSS 2.0 feed of the newest pages, which Google takes as a sitemap. Read
    /// for <c>loc</c> alone it listed nothing, and sitemap-empty said so of a file with 50 pages in it. Each item's link is
    /// an entry; the channel's own link, its image's and an item's <c>atom:link</c> aren't. The entries are marked as an
    /// image sitemap's are, since a feed lists again what the page sitemap lists, and the date is given as a sitemap's is.
    /// </summary>
    [Fact]
    public async Task ReadsAnRssFeedAsTheSitemapItIs()
    {
        var entries = await ReadAsync("""
            <?xml version="1.0" encoding="UTF-8"?>
            <!-- This sitemap was dynamically generated on November 24, 2026 at 10:07 am by All in One SEO v4.9.8 - the original SEO plugin for WordPress. -->
            <?xml-stylesheet type="text/xsl" href="https://freebsdfoundation.org/default-sitemap.xsl"?>
            <rss version="2.0" xmlns:atom="http://www.w3.org/2005/Atom">
            	<channel>
            		<title>FreeBSD Foundation</title>
            		<link><![CDATA[https://freebsdfoundation.org]]></link>
            		<description><![CDATA[FreeBSD Foundation]]></description>
            		<lastBuildDate><![CDATA[Fri, 20 Nov 2026 17:01:41 +0000]]></lastBuildDate>
            		<atom:link href="https://freebsdfoundation.org/sitemap.rss" rel="self" type="application/rss+xml"/>
            		<item>
            			<guid><![CDATA[https://freebsdfoundation.org/blog/a-post/]]></guid>
            			<link><![CDATA[https://freebsdfoundation.org/blog/a-post/]]></link>
            			<title><![CDATA[A post]]></title>
            			<pubDate><![CDATA[Fri, 20 Nov 2026 17:01:41 +0000]]></pubDate>
            			<atom:link href="https://freebsdfoundation.org/blog/a-post/feed/" rel="replies"/>
            		</item>
            		<item>
            			<link>https://freebsdfoundation.org/news/another/</link>
            			<pubDate>not a date</pubDate>
            		</item>
            		<image><url>https://freebsdfoundation.org/logo.png</url><title>FreeBSD Foundation</title><link>https://freebsdfoundation.org/logo-page/</link></image>
            	</channel>
            </rss>
            """);

        Assert.Equal(
            [
                new SitemapEntry("https://freebsdfoundation.org/blog/a-post/", false, "2026-11-20T17:01:41+00:00", HasExtension: true),
                new SitemapEntry("https://freebsdfoundation.org/news/another/", false, null, HasExtension: true)
            ], entries);
    }

    /// <summary>An Atom feed, the other kind Google takes: each entry's link (its alternate, not its replies or itself) and when it was updated.</summary>
    [Fact]
    public async Task ReadsAnAtomFeedAsASitemap()
    {
        var entries = await ReadAsync("""
            <?xml version="1.0" encoding="utf-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <title>Example</title>
              <link href="https://example.com/"/>
              <link rel="self" href="https://example.com/feed.atom"/>
              <updated>2026-11-20T17:01:41Z</updated>
              <entry>
                <title>A post</title>
                <link rel="replies" href="https://example.com/a-post/comments"/>
                <link rel="alternate" type="text/html" href="https://example.com/a-post"/>
                <id>urn:uuid:1225c695-cfb8-4ebb-aaaa-80da344efa6a</id>
                <updated>2026-11-19T08:30:00+01:00</updated>
              </entry>
              <entry><link href="https://example.com/b-post"/></entry>
            </feed>
            """);

        Assert.Equal(
            [
                new SitemapEntry("https://example.com/a-post", false, "2026-11-19T08:30:00+01:00", HasExtension: true),
                new SitemapEntry("https://example.com/b-post", false, null, HasExtension: true)
            ], entries);
    }

    /// <summary>A Google News sitemap (theregister.com's, cnx-software.com's) and a video sitemap: every entry carries its extension's tag.</summary>
    [Fact]
    public async Task NotesTheEntriesOfANewsOrVideoSitemap()
    {
        var entries = await ReadAsync("""
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:news="http://www.google.com/schemas/sitemap-news/0.9"
                    xmlns:video="http://www.google.com/schemas/sitemap-video/1.1" xmlns:xhtml="http://www.w3.org/1999/xhtml">
              <url><news:news><news:title>A story</news:title></news:news><loc>https://example.com/story</loc></url>
              <url><loc>https://example.com/film</loc><video:video><video:title>A film</video:title></video:video></url>
              <url><loc>https://example.com/plain</loc><xhtml:link rel="alternate" hreflang="de" href="https://example.com/de/plain"/></url>
            </urlset>
            """);

        Assert.Equal([true, true, false], entries.Select(entry => entry.HasExtension));
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
