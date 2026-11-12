using System.Net;
using System.Text;
using ShoutingIguana.Sidecar.Tests.Support;
using ShoutingIguana.TestSite;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>
/// The site's icon as the UI asks for it (E6): the image a crawl kept, served as an image and nothing else, and nothing
/// at all for a crawl that has none. A crawl file is trusted no more than the site was, so whatever a file holds as its
/// icon, only an image is ever served, under the media type its own bytes are.
/// </summary>
public sealed class FaviconEndpointTests(GridSidecar grid) : IClassFixture<GridSidecar>
{
    private TestSidecar Sidecar => grid.Sidecar;

    [Fact]
    public async Task ACrawlsIconIsServedAsTheImageItIsAndTheLibrarySaysItHasOne()
    {
        using var response = await Sidecar.Client.GetAsync($"api/crawls/{grid.Id}/favicon");

        // The synthetic site serves a PNG at /favicon.ico, calling it an icon file: the bytes say what it is.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.ToString());
        Assert.Equal(SyntheticSite.Favicon, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(SyntheticSite.Favicon.Count, response.Content.Headers.ContentLength);

        // The headers that keep it an image: no guessing another type from its bytes, nothing run or loaded from it were it
        // ever opened as a document, and never kept by a cache.
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

        // Each crawl of the site kept it, analysed or not, and the library says so.
        Assert.True((bool)(await Sidecar.GetJsonAsync($"api/crawls/{grid.Id}"))["hasFavicon"]!);
        var listed = (await Sidecar.GetJsonAsync("api/crawls")).AsArray();
        Assert.All(new[] { grid.Id, grid.LimitedId, grid.FreshId }, id => Assert.True((bool)listed.Single(crawl => (string?)crawl!["id"] == id)!["hasFavicon"]!));
        using var fresh = await Sidecar.Client.GetAsync($"api/crawls/{grid.FreshId}/favicon");
        Assert.Equal((HttpStatusCode.OK, "image/png"), (fresh.StatusCode, fresh.Content.Headers.ContentType?.ToString()));
    }

    [Fact]
    public async Task ACrawlWithoutAnIconAnswersWithNothingAndAnUnknownCrawlWithAProblem()
    {
        // A crawl made before icons were kept has no table for one.
        var earlier = grid.Copy(path => Change(path, "DROP TABLE favicon", null));
        using var none = await Sidecar.Client.GetAsync($"api/crawls/{earlier}/favicon");
        Assert.Equal((HttpStatusCode.NoContent, 0), (none.StatusCode, (await none.Content.ReadAsByteArrayAsync()).Length));
        Assert.Equal("nosniff", Assert.Single(none.Headers.GetValues("X-Content-Type-Options")));
        Assert.False((bool)(await Sidecar.GetJsonAsync($"api/crawls/{earlier}"))["hasFavicon"]!);

        TestSidecar.AssertProblem(await Sidecar.SendAsync(HttpMethod.Get, "api/crawls/0000000000000000/favicon"), HttpStatusCode.NotFound, "crawl-not-found");
        using var unauthorised = new HttpClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await unauthorised.GetAsync(new Uri(Sidecar.Client.BaseAddress!, $"api/crawls/{grid.Id}/favicon"))).StatusCode);
    }

    /// <summary>A crafted file's "icon": a page, a script-bearing SVG, more bytes than an icon may be. None is served, whatever type the file gives it.</summary>
    [Theory]
    [InlineData("<html><script>alert(1)</script></html>", "text/html")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"/>", "image/svg+xml")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"/>", "image/png")]
    [InlineData("GIF89a is how this script begins; alert(1)", "image/gif")]
    public async Task WhatAFileHoldsThatIsNoImageIsNeverServed(string body, string contentType)
    {
        var crafted = grid.Copy(path => Change(path, $"UPDATE favicon SET content_type = '{contentType}', body = $body", Encoding.UTF8.GetBytes(body)));

        using var response = await Sidecar.Client.GetAsync($"api/crawls/{crafted}/favicon");

        Assert.Equal((HttpStatusCode.NoContent, 0), (response.StatusCode, (await response.Content.ReadAsByteArrayAsync()).Length));
        Assert.Null(response.Content.Headers.ContentType);
    }

    /// <summary>A file that calls a real image something else has it served as what it is.</summary>
    [Fact]
    public async Task AnImageAFileCallsAPageIsServedAsTheImage()
    {
        var mislabelled = grid.Copy(path => Change(path, "UPDATE favicon SET content_type = 'text/html; charset=utf-8'", null));

        using var response = await Sidecar.Client.GetAsync($"api/crawls/{mislabelled}/favicon");

        Assert.Equal((HttpStatusCode.OK, "image/png"), (response.StatusCode, response.Content.Headers.ContentType?.ToString()));
    }

    private static void Change(string path, string sql, byte[]? body)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (body is not null)
        {
            command.Parameters.AddWithValue("$body", body);
        }

        command.ExecuteNonQuery();
    }
}
