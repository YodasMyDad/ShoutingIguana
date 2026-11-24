using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Time.Testing;
using ShoutingIguana.Engine.Configuration;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Tests.Fetching;

/// <summary>The fetcher against a real local server, so headers, compression and timing are what HttpClient sees.</summary>
public sealed class HttpFetcherTests : IAsyncLifetime
{
    private const string SECRET_HEADER = "X-Allowlist-Token";
    private const string PAGE = "<html><body>€</body></html>";
    private const int LARGE_PAGE_BYTES = 200 * 1024;
    private const int IMAGE_BYTES = 300_000;
    private const int MEASURE_CAP_BYTES = 10 * 1024 * 1024;

    private static readonly DateTimeOffset NOW = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly Encoding WINDOWS_1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;
    private static readonly string COMPRESSIBLE_PAGE = $"<html><body>{string.Concat(Enumerable.Repeat("<p>€ 10 – “quoted”</p>", 2_000))}</body></html>";
    private static readonly byte[] COMPRESSIBLE_BYTES = WINDOWS_1252.GetBytes(COMPRESSIBLE_PAGE);
    private static readonly byte[] GZIPPED = Compress(COMPRESSIBLE_BYTES, "gzip");
    private static readonly byte[] BROTLI = Compress(COMPRESSIBLE_BYTES, "br");

    /// <summary>HTTP's "deflate" is the zlib format (RFC 9110), not raw deflate.</summary>
    private static readonly byte[] DEFLATED = Compress(COMPRESSIBLE_BYTES, "deflate");

    private LoopbackServer _server = null!;

    public async Task InitializeAsync() => _server = await LoopbackServer.StartAsync(RespondAsync);

    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Theory]
    [InlineData("text/html;charset=windows-1252;", "text/html")]
    [InlineData("text/html;;charset=windows-1252", "text/html")]
    [InlineData("text/html; charset=\"windows-1252", "text/html")]
    [InlineData("text/html charset=windows-1252", "text/html")]
    [InlineData("TEXT/HTML; Charset=\"Windows-1252\"", "text/html")]
    [InlineData("application/xhtml+xml; charset=windows-1252", "application/xhtml+xml")]
    public async Task SloppyContentTypesAreStillHtmlWithTheirCharset(string contentType, string mediaType)
    {
        using var fetcher = Fetcher();

        var response = await fetcher.FetchAsync(_server.Url($"/page?type={Uri.EscapeDataString(contentType)}"), FetchBody.Html, CancellationToken.None);

        Assert.Equal(FetchOutcome.Success, response.Outcome);
        Assert.Equal(mediaType, response.ContentType);
        Assert.True(response.IsHtml);
        Assert.Equal(PAGE, response.Body);
    }

    /// <summary>
    /// A page whose bytes begin with a byte-order mark has declared its encoding. The decoded body no longer has the
    /// mark, so the response says which it was; a page without one says nothing.
    /// </summary>
    [Theory]
    [InlineData("utf-8", "utf-8")]
    [InlineData("utf-16", "utf-16le")]
    [InlineData("utf-16BE", "utf-16be")]
    public async Task AByteOrderMarkIsKeptWithTheResponse(string encoding, string expected)
    {
        using var fetcher = Fetcher();

        var marked = await fetcher.FetchAsync(_server.Url($"/marked?encoding={encoding}"), FetchBody.Html, CancellationToken.None);
        var plain = await fetcher.FetchAsync(_server.Url("/page?type=text/html"), FetchBody.Html, CancellationToken.None);

        Assert.Equal((FetchOutcome.Success, PAGE, expected), (marked.Outcome, marked.Body, marked.ByteOrderMark));
        Assert.Equal(FetchOutcome.Success, plain.Outcome);
        Assert.Null(plain.ByteOrderMark);
    }

    [Theory]
    [InlineData("text/html; charset=")]
    [InlineData("text/html;")]
    [InlineData("text/html; charset=\"\"")]
    public async Task AnEmptyCharsetStillReadsTheHtml(string contentType)
    {
        using var fetcher = Fetcher();

        var response = await fetcher.FetchAsync(_server.Url($"/page?type={Uri.EscapeDataString(contentType)}"), FetchBody.Html, CancellationToken.None);

        // No charset anywhere, so the windows-1252 byte decodes as invalid UTF-8.
        Assert.True(response.IsHtml);
        Assert.Equal("<html><body>�</body></html>", response.Body);
    }

    [Theory]
    [InlineData("gzip")]
    [InlineData("br")]
    [InlineData("deflate")]
    public async Task DecompressesThenDecodesTheCharset(string encoding)
    {
        using var fetcher = Fetcher();

        var response = await fetcher.FetchAsync(_server.Url($"/compressed?encoding={encoding}"), FetchBody.Html, CancellationToken.None);

        var compressed = Compressed(encoding);
        Assert.Equal(COMPRESSIBLE_PAGE, response.Body);
        Assert.Equal(COMPRESSIBLE_BYTES.Length, response.BodyBytes);
        Assert.Equal(compressed.Length, response.TransferBytes);
        Assert.Equal(compressed.Length, response.ContentLength);
    }

    [Fact]
    public async Task StopsReadingAtTheHtmlLimit()
    {
        using var fetcher = Fetcher(Options() with { MaxHtmlBytes = 64 * 1024 });
        using var unlimited = Fetcher();

        var limited = await fetcher.FetchAsync(_server.Url("/large"), FetchBody.Html, CancellationToken.None);
        var capped = await fetcher.FetchAsync(_server.Url("/large"), FetchBody.Html, CancellationToken.None, maxBodyBytes: 1_000);
        var whole = await unlimited.FetchAsync(_server.Url("/large"), FetchBody.Html, CancellationToken.None);

        Assert.True(limited.BodyTruncated);
        Assert.Equal(64 * 1024, limited.BodyBytes);
        Assert.Equal(64 * 1024, limited.Body!.Length);
        Assert.Equal(1_000, capped.BodyBytes);
        Assert.False(whole.BodyTruncated);
        Assert.Equal(LARGE_PAGE_BYTES, whole.BodyBytes);
    }

    [Theory]
    [InlineData(LARGE_PAGE_BYTES, false)]
    [InlineData(LARGE_PAGE_BYTES - 1, true)]
    public async Task ABodyExactlyAtTheLimitIsWhole(int limit, bool truncated)
    {
        using var fetcher = Fetcher();

        var response = await fetcher.FetchAsync(_server.Url("/large"), FetchBody.Html, CancellationToken.None, maxBodyBytes: limit);

        Assert.Equal(truncated, response.BodyTruncated);
        Assert.Equal(limit, response.BodyBytes);
        Assert.Equal(limit, response.Body!.Length);
    }

    [Fact]
    public async Task SamplesBlockPagesWithoutKeepingThem()
    {
        using var fetcher = Fetcher();

        var challenge = await fetcher.FetchAsync(_server.Url("/challenge"), FetchBody.Html, CancellationToken.None);
        var forbidden = await fetcher.FetchAsync(_server.Url("/forbidden-json"), FetchBody.Html, CancellationToken.None);

        Assert.Equal(FetchOutcome.Blocked, challenge.Outcome);
        Assert.Equal("Cloudflare", challenge.BlockReason);
        Assert.Null(challenge.Body);
        Assert.Equal(64 * 1024, challenge.BodyBytes);

        // Only HTML error pages are sampled.
        Assert.Equal(FetchOutcome.ClientError, forbidden.Outcome);
        Assert.Null(forbidden.BodyBytes);
    }

    [Fact]
    public async Task TimesOutAResponseThatNeverComes()
    {
        using var fetcher = Fetcher(Options() with { RequestTimeoutSeconds = 1 });

        var response = await fetcher.FetchAsync(_server.Url("/hang"), FetchBody.Html, CancellationToken.None);

        Assert.Equal(FetchOutcome.Timeout, response.Outcome);
        Assert.True(response.IsTransientFailure);
        Assert.Null(response.StatusCode);
        Assert.Equal("No response within 1 seconds", response.Error);
    }

    /// <summary>A connection that can't be made gives up after 10 seconds of a 20-second request, and was said to have waited 20.</summary>
    [Fact]
    public void ATimeoutNamesTheLimitThatWasHit()
    {
        var request = TimeSpan.FromSeconds(20);
        var connect = TimeSpan.FromSeconds(10);
        Assert.Equal("No response within 10 seconds", HttpFetcher.TimeoutError(requestTimeUp: false, request, connect));
        Assert.Equal("No response within 20 seconds", HttpFetcher.TimeoutError(requestTimeUp: true, request, connect));
    }

    /// <summary>
    /// A script or stylesheet is checked, not parsed, but its size is what Google's 2 MB limit is about, and that is
    /// its size once decompressed. The bytes sent are counted too, when no header says how many there are.
    /// </summary>
    [Fact]
    public async Task MeasuresAScriptAsItIsOnceDecompressed()
    {
        using var fetcher = Fetcher(Options());

        var script = await fetcher.FetchAsync(_server.Url("/script"), FetchBody.None, CancellationToken.None);

        Assert.Equal(FetchOutcome.Success, script.Outcome);
        Assert.Null(script.Body);
        Assert.Null(script.ContentLength);
        Assert.Equal(COMPRESSIBLE_BYTES.Length, script.BodyBytes);
        Assert.Equal(GZIPPED.Length, script.TransferBytes);
        Assert.True(script.BodyBytes > script.TransferBytes * 2);

        var styles = await fetcher.FetchAsync(_server.Url("/styles"), FetchBody.None, CancellationToken.None);
        Assert.Equal("body { color: black; }p { margin: 0; }".Length, styles.BodyBytes);
        Assert.Equal(styles.BodyBytes, styles.TransferBytes);
    }

    [Fact]
    public async Task ReadsRetryAfterInSeconds()
    {
        using var fetcher = Fetcher();

        var response = await fetcher.FetchAsync(_server.Url("/throttled?retry-after=120"), FetchBody.Html, CancellationToken.None);

        Assert.Equal(503, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(120), response.RetryAfter);
    }

    [Fact]
    public async Task ReadsRetryAfterAsAnHttpDate()
    {
        using var fetcher = Fetcher(time: new FakeTimeProvider(NOW));
        var later = Uri.EscapeDataString((NOW + TimeSpan.FromSeconds(90)).ToString("R", CultureInfo.InvariantCulture));
        var earlier = Uri.EscapeDataString((NOW - TimeSpan.FromSeconds(90)).ToString("R", CultureInfo.InvariantCulture));

        var future = await fetcher.FetchAsync(_server.Url($"/throttled?retry-after={later}"), FetchBody.Html, CancellationToken.None);
        var past = await fetcher.FetchAsync(_server.Url($"/throttled?retry-after={earlier}"), FetchBody.Html, CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(90), future.RetryAfter);
        Assert.Equal(TimeSpan.Zero, past.RetryAfter);
    }

    [Fact]
    public async Task ExtraHeadersOnlyGoToTheCrawledSite()
    {
        using var fetcher = Fetcher(Options() with { ExtraHeaders = new Dictionary<string, string> { [SECRET_HEADER] = "s3cret" } });

        var own = await fetcher.FetchAsync(_server.Url("/echo"), FetchBody.Text, CancellationToken.None);
        var other = await fetcher.FetchAsync(_server.OtherUrl("/echo"), FetchBody.Text, CancellationToken.None);
        using var otherDirect = await fetcher.SendAsync(_server.OtherUrl("/echo"), CancellationToken.None);

        Assert.Equal("s3cret", own.Body);
        Assert.Equal("(none)", other.Body);
        Assert.Equal("(none)", await otherDirect.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ExtraHeadersStayOffPlainHttpWhenTheCrawlStartedOnHttps()
    {
        var secure = new UriBuilder(_server.BaseUri) { Scheme = Uri.UriSchemeHttps }.Uri.AbsoluteUri;
        var secret = new Dictionary<string, string> { [SECRET_HEADER] = "s3cret" };
        using var fetcher = Fetcher(Options() with { StartUrl = secure, ExtraHeaders = secret });

        // An http:// link on an https site goes out unencrypted, so the secret stays behind.
        var downgraded = await fetcher.FetchAsync(_server.Url("/echo"), FetchBody.Text, CancellationToken.None);

        Assert.Equal("(none)", downgraded.Body);
    }

    [Fact]
    public async Task ExtraHeadersGoToTheSiteOverHttps()
    {
        await using var secure = await LoopbackServer.StartAsync(RespondAsync, https: true);
        var options = new CrawlOptions
        {
            StartUrl = secure.BaseUri.AbsoluteUri,
            ExtraHeaders = new Dictionary<string, string> { [SECRET_HEADER] = "s3cret" },
            TrustedCertificate = secure.Certificate
        };
        using var fetcher = new HttpFetcher(options, UserAgents.Resolve(options.UserAgent, null),
            new CrawlScope(secure.BaseUri, includeSubdomains: false, stayInStartFolder: false), TimeProvider.System);

        var response = await fetcher.FetchAsync(secure.Url("/echo"), FetchBody.Text, CancellationToken.None);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("s3cret", response.Body);
    }

    [Fact]
    public async Task ASignatureGoesToItsHostOverHttpsWithShopifysAgent()
    {
        await using var secure = await LoopbackServer.StartAsync(RespondAsync, https: true);
        using var fetcher = SignedFetcher(secure, TimeProvider.System);

        var response = await fetcher.FetchAsync(secure.Url("/signature"), FetchBody.Text, CancellationToken.None);
        using var direct = await fetcher.SendAsync(secure.Url("/signature"), CancellationToken.None);

        var expected = $"{FakeSignatures.Input()}|{FakeSignatures.SIGNATURE}|\"https://shopify.com\"";
        Assert.Equal(expected, response.Body);
        Assert.True(response.Signed);
        Assert.Equal(expected, await direct.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ASignatureNeverGoesOverPlainHttpOrToAnotherSite()
    {
        await using var secure = await LoopbackServer.StartAsync(RespondAsync, https: true);
        using var fetcher = SignedFetcher(secure, TimeProvider.System);

        var plain = await fetcher.FetchAsync(new UriBuilder(secure.Url("/signature")) { Scheme = Uri.UriSchemeHttp }.Uri, FetchBody.Text, CancellationToken.None);
        var other = await fetcher.FetchAsync(secure.OtherUrl("/signature"), FetchBody.Text, CancellationToken.None);

        Assert.Equal("(none)", plain.Body);
        Assert.False(plain.Signed);
        Assert.Equal("(none)", other.Body);
        Assert.False(other.Signed);
    }

    [Fact]
    public async Task ASignatureStopsBeingSentWhenItExpires()
    {
        await using var secure = await LoopbackServer.StartAsync(RespondAsync, https: true);
        var clock = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(FakeSignatures.CREATED));
        using var fetcher = SignedFetcher(secure, clock, expires: FakeSignatures.CREATED + 60);

        var before = await fetcher.FetchAsync(secure.Url("/signature"), FetchBody.Text, CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(1));
        var after = await fetcher.FetchAsync(secure.Url("/signature"), FetchBody.Text, CancellationToken.None);

        Assert.True(before.Signed);
        Assert.Equal("(none)", after.Body);
        Assert.False(after.Signed);
    }

    [Fact]
    public async Task MeasuresImagesSentWithoutALength()
    {
        using var fetcher = Fetcher();

        var chunked = await fetcher.FetchAsync(_server.Url($"/image?bytes={IMAGE_BYTES}&chunked=1"), FetchBody.None, CancellationToken.None);
        var huge = await fetcher.FetchAsync(_server.Url($"/image?bytes={MEASURE_CAP_BYTES + 2_000_000}&chunked=1"), FetchBody.None, CancellationToken.None);
        var sized = await fetcher.FetchAsync(_server.Url($"/image?bytes={IMAGE_BYTES}"), FetchBody.None, CancellationToken.None);
        var styles = await fetcher.FetchAsync(_server.Url("/styles"), FetchBody.None, CancellationToken.None);

        Assert.Null(chunked.ContentLength);
        Assert.Equal(IMAGE_BYTES, chunked.TransferBytes);
        Assert.Null(chunked.Body);
        Assert.Equal(MEASURE_CAP_BYTES, huge.TransferBytes);

        // With a Content-Length, an image is not read. A stylesheet is, to count it as it is once decompressed.
        Assert.Equal(IMAGE_BYTES, sized.ContentLength);
        Assert.Null(sized.TransferBytes);
        Assert.Null(styles.ContentLength);
        Assert.Equal(38, styles.TransferBytes);
    }

    private HttpFetcher Fetcher(CrawlOptions? options = null, TimeProvider? time = null)
    {
        options ??= Options();
        return new HttpFetcher(options, UserAgents.Resolve(options.UserAgent, null),
            new CrawlScope(_server.BaseUri, includeSubdomains: false, stayInStartFolder: false), time ?? TimeProvider.System);
    }

    private CrawlOptions Options() => new() { StartUrl = _server.BaseUri.AbsoluteUri };

    /// <summary>A fetcher whose crawl has a signature for the https server's own host and port.</summary>
    private static HttpFetcher SignedFetcher(LoopbackServer secure, TimeProvider clock, long expires = FakeSignatures.LASTS_UNTIL)
    {
        var options = new CrawlOptions
        {
            StartUrl = secure.BaseUri.AbsoluteUri,
            Signatures = FakeSignatures.For(secure.BaseUri.Authority, expires),
            TrustedCertificate = secure.Certificate
        };
        return new HttpFetcher(options, UserAgents.Resolve(options.UserAgent, null),
            new CrawlScope(secure.BaseUri, includeSubdomains: false, stayInStartFolder: false), clock);
    }

    private async Task RespondAsync(HttpContext context)
    {
        var request = context.Request;
        var response = context.Response;
        switch (request.Path.Value)
        {
            case "/page":
                response.Headers.ContentType = request.Query["type"].ToString();
                await response.Body.WriteAsync(WINDOWS_1252.GetBytes(PAGE));
                break;
            case "/marked":
                // The page in the encoding asked for, after that encoding's byte-order mark, with no charset in the header.
                var marking = Encoding.GetEncoding(request.Query["encoding"].ToString());
                response.Headers.ContentType = "text/html";
                await response.Body.WriteAsync((byte[])[.. marking.GetPreamble(), .. marking.GetBytes(PAGE)]);
                break;
            case "/compressed":
                var compressed = Compressed(request.Query["encoding"].ToString());
                response.Headers.ContentType = "text/html; charset=windows-1252";
                response.Headers.ContentEncoding = request.Query["encoding"].ToString();
                response.ContentLength = compressed.Length;
                await response.Body.WriteAsync(compressed);
                break;
            case "/large":
                response.ContentType = "text/html";
                await response.Body.WriteAsync(Encoding.ASCII.GetBytes(new string('a', LARGE_PAGE_BYTES)));
                break;
            case "/challenge":
                response.StatusCode = 403;
                response.ContentType = "text/html";
                response.Headers.Server = "cloudflare";
                await response.WriteAsync($"<title>Just a moment...</title>{new string(' ', LARGE_PAGE_BYTES)}");
                break;
            case "/forbidden-json":
                response.StatusCode = 403;
                response.ContentType = "application/json";
                await response.WriteAsync("""{"error":"forbidden"}""");
                break;
            case "/hang":
                await Task.Delay(Timeout.Infinite, context.RequestAborted);
                break;
            case "/throttled":
                response.StatusCode = 503;
                response.Headers.RetryAfter = request.Query["retry-after"].ToString();
                break;
            case "/signature":
                response.ContentType = "text/plain";
                await response.WriteAsync(request.Headers.ContainsKey("Signature")
                    ? $"{request.Headers["Signature-Input"]}|{request.Headers["Signature"]}|{request.Headers["Signature-Agent"]}"
                    : "(none)");
                break;
            case "/echo":
                response.ContentType = "text/plain";
                await response.WriteAsync(request.Headers.TryGetValue(SECRET_HEADER, out var secret) ? secret.ToString() : "(none)");
                break;
            case "/image":
                await WriteImageAsync(response, int.Parse(request.Query["bytes"]!, CultureInfo.InvariantCulture), request.Query.ContainsKey("chunked"));
                break;
            case "/script":
                // A script as a CDN sends one: compressed, and in chunks, so no header says how long it is.
                response.Headers.ContentType = "application/javascript";
                response.Headers.ContentEncoding = "gzip";
                await response.Body.WriteAsync(GZIPPED.AsMemory(0, GZIPPED.Length / 2));
                await response.Body.FlushAsync();
                await response.Body.WriteAsync(GZIPPED.AsMemory(GZIPPED.Length / 2));
                break;
            case "/styles":
                response.ContentType = "text/css";
                await response.WriteAsync("body { color: black; }");
                await response.Body.FlushAsync();
                await response.WriteAsync("p { margin: 0; }");
                break;
            default:
                response.StatusCode = 404;
                break;
        }
    }

    private static async Task WriteImageAsync(HttpResponse response, int bytes, bool chunked)
    {
        response.ContentType = "image/png";
        if (!chunked)
        {
            response.ContentLength = bytes;
        }

        var chunk = new byte[64 * 1024];
        for (var written = 0; written < bytes; written += chunk.Length)
        {
            await response.Body.WriteAsync(chunk.AsMemory(0, Math.Min(chunk.Length, bytes - written)));
        }
    }

    private static byte[] Compressed(string encoding) => encoding switch
    {
        "gzip" => GZIPPED,
        "br" => BROTLI,
        _ => DEFLATED
    };

    private static byte[] Compress(byte[] bytes, string encoding)
    {
        using var output = new MemoryStream();
        using (Stream compressor = encoding switch
               {
                   "gzip" => new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true),
                   "br" => new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true),
                   _ => new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true)
               })
        {
            compressor.Write(bytes);
        }

        return output.ToArray();
    }
}
