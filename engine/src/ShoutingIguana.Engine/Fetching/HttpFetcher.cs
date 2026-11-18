using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// Plain HTTP fetching for every URL. Redirects are not followed (each hop is recorded as its own URL),
/// compression is decoded here so the transfer size is known, and only bodies that are needed are read:
/// HTML for pages being crawled, a short sample of error pages for block detection, text for robots.txt,
/// and a byte count (nothing kept) for images sent without a Content-Length.
/// </summary>
internal sealed class HttpFetcher : IDisposable
{
    private const int INITIAL_BUFFER_BYTES = 64 * 1024;
    private const int BLOCK_SAMPLE_BYTES = 64 * 1024;
    private const int MEASURE_BUFFER_BYTES = 64 * 1024;
    private const long MAX_MEASURED_IMAGE_BYTES = 10 * 1024 * 1024;
    private const int MAX_HEADERS = 200;
    private const string ACCEPT = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8";

    private readonly HttpClient _client;
    private readonly CrawlOptions _options;
    private readonly CrawlScope _scope;
    private readonly bool _startsOnHttps;
    private readonly TimeProvider _timeProvider;
    private readonly string _userAgent;
    private readonly TimeSpan _timeout;

    /// <summary>The proxy's address has a user name and password, which a SOCKS5 proxy's refusal is told against.</summary>
    private readonly bool _proxyHasLogin;

    public HttpFetcher(CrawlOptions options, ResolvedUserAgent userAgent, CrawlScope scope, TimeProvider timeProvider)
    {
        _options = options;
        _scope = scope;
        _startsOnHttps = Uri.TryCreate(options.StartUrl, UriKind.Absolute, out var start) && start.Scheme == Uri.UriSchemeHttps;
        _timeProvider = timeProvider;
        _userAgent = userAgent.Header;
        _timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
        Guard = new PrivateNetworkGuard(options.StartUrl);
        var proxy = ProxyFactory.Create(options.ProxyUrl);
        _proxyHasLogin = proxy?.Credentials is not null;

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = options.UseCookies,
            CookieContainer = new CookieContainer(),
            ConnectTimeout = TimeSpan.FromSeconds(Math.Min(10, options.RequestTimeoutSeconds)),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            MaxConnectionsPerServer = 64,
            EnableMultipleHttp2Connections = true,
            UseProxy = options.ProxyUrl is not null,
            Proxy = proxy
        };

        // Connecting by itself, the crawl sees the addresses a name resolves to; through a proxy it would see the proxy's.
        if (options.ProxyUrl is null)
        {
            handler.ConnectCallback = ConnectAsync;
        }

        if (options.TrustedCertificate is { } trusted)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                errors == SslPolicyErrors.None || certificate?.GetCertHashString() == trusted.GetCertHashString();
        }

        _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>What keeps a crawl of a public site off private network addresses.</summary>
    public PrivateNetworkGuard Guard { get; }

    public async Task<FetchResponse> FetchAsync(Uri uri, FetchBody body, CancellationToken cancellationToken, int? maxBodyBytes = null)
    {
        if (await Guard.RefusesAsync(uri).ConfigureAwait(false))
        {
            return PrivateNetworkGuard.NotChecked(uri);
        }

        var stopwatch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        try
        {
            using var response = await SendAsync(uri, timeout.Token).ConfigureAwait(false);
            var timeToFirstByte = (int)stopwatch.ElapsedMilliseconds;
            var statusCode = (int)response.StatusCode;
            var headers = CollectHeaders(response);
            var rawContentType = ContentTypeHeader.Read(response.Content.Headers);
            var contentType = ContentTypeHeader.MediaTypeOf(rawContentType);
            var isHtml = contentType is "text/html" or "application/xhtml+xml";
            var isSuccess = statusCode is >= 200 and < 300;
            var contentLength = response.Content.Headers.ContentLength;

            var readLimit = body switch
            {
                FetchBody.Html when isHtml && isSuccess => maxBodyBytes ?? _options.MaxHtmlBytes,
                FetchBody.Text when isSuccess => maxBodyBytes ?? _options.MaxHtmlBytes,
                _ when isHtml && statusCode is 401 or 403 or 405 or 429 or 503 => BLOCK_SAMPLE_BYTES,
                _ => 0
            };

            string? text = null;
            string? byteOrderMark = null;
            long? bodyBytes = null;
            long? transferBytes = null;
            var truncated = false;
            (int Width, int Height)? pixels = null;
            if (readLimit > 0)
            {
                using var buffer = await ReadBodyAsync(response, readLimit, timeout.Token).ConfigureAwait(false);
                text = CharsetDecoder.Decode(buffer.Span, ContentTypeHeader.CharsetOf(rawContentType));
                byteOrderMark = CharsetDecoder.ByteOrderMarkOf(buffer.Span);
                bodyBytes = buffer.Length;
                transferBytes = buffer.TransferBytes;
                truncated = buffer.Truncated;
            }
            else if (isSuccess && contentType?.StartsWith("image/", StringComparison.Ordinal) == true)
            {
                // Its first bytes say the image's size in pixels. Image CDNs often send chunked responses without a length;
                // then counting the bytes is the only way to know its size in bytes, which the large-image check needs, so
                // it's read to the end. Its body is the image as sent.
                var measure = contentLength is null;
                (var measured, pixels) = await ReadImageAsync(response, measure, timeout.Token, cancellationToken).ConfigureAwait(false);
                if (measure)
                {
                    transferBytes = measured;
                    bodyBytes = transferBytes;
                }
            }

            var blockReason = BlockDetector.Detect(statusCode, headers, isSuccess ? null : text);
            return new FetchResponse
            {
                RequestUri = uri,
                Outcome = Classify(statusCode, blockReason),
                StatusCode = statusCode,
                Headers = headers,
                ContentType = contentType,
                IsHtml = isHtml,
                ContentLength = contentLength,
                TransferBytes = transferBytes,
                BodyBytes = bodyBytes,
                PixelWidth = pixels?.Width,
                PixelHeight = pixels?.Height,
                Body = isSuccess && blockReason is null ? text : null,
                ByteOrderMark = isSuccess && blockReason is null ? byteOrderMark : null,
                BodyTruncated = truncated,
                Location = ResolveLocation(uri, response),
                RetryAfter = GetRetryAfter(response),
                HttpVersion = response.Version.ToString(),
                TimeToFirstByteMs = timeToFirstByte,
                TotalMs = (int)stopwatch.ElapsedMilliseconds,
                BlockReason = blockReason
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed(uri, FetchOutcome.Timeout, $"No response within {_timeout.TotalSeconds:0} seconds", true, stopwatch);
        }
        catch (HttpRequestException exception) when (exception.InnerException is PrivateNetworkAddressException)
        {
            return PrivateNetworkGuard.NotChecked(uri);
        }
        catch (HttpRequestException exception) when (SocksRefusals.Of(exception) is { } socks)
        {
            // A SOCKS5 proxy said no: in its words, and only asked again where that could help (not a login refused).
            return Failed(uri, FetchOutcome.NetworkError, SocksRefusals.Describe(socks.Refusal, socks.Message, _proxyHasLogin), SocksRefusals.IsTransient(socks.Refusal),
                stopwatch);
        }
        catch (HttpRequestException exception)
        {
            var transient = exception.HttpRequestError is not (HttpRequestError.NameResolutionError or HttpRequestError.SecureConnectionError);
            return Failed(uri, FetchOutcome.NetworkError, Describe(exception), transient, stopwatch);
        }
        catch (IOException exception)
        {
            return Failed(uri, FetchOutcome.NetworkError, exception.Message, true, stopwatch);
        }
        catch (InvalidDataException exception)
        {
            return Failed(uri, FetchOutcome.NetworkError, $"Could not decompress the response: {exception.Message}", false, stopwatch);
        }
    }

    /// <summary>Sends a GET and returns once headers arrive. The caller owns the response.</summary>
    /// <exception cref="HttpRequestException">It failed, or <see cref="Guard"/> refused the address: nothing was asked.</exception>
    public async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (await Guard.RefusesAsync(uri).ConfigureAwait(false))
        {
            throw new HttpRequestException(PrivateNetworkGuard.ERROR, new PrivateNetworkAddressException(uri.IdnHost));
        }

        return await _client.SendAsync(CreateRequest(uri), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Connects as the handler would by itself (the name's addresses in turn), save those <see cref="Guard"/> refuses.
    /// </summary>
    private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var addresses = await Guard.AddressesToConnectToAsync(context.DnsEndPoint.Host, cancellationToken).ConfigureAwait(false);
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Opens the response body with any Content-Encoding removed.</summary>
    public static async Task<Stream> OpenBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var raw = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return Decompress(raw, response.Content.Headers.ContentEncoding);
    }

    public void Dispose() => _client.Dispose();

    private HttpRequestMessage CreateRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri)
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };

        var headers = request.Headers;
        headers.TryAddWithoutValidation("User-Agent", _userAgent);
        headers.TryAddWithoutValidation("Accept", ACCEPT);
        headers.TryAddWithoutValidation("Accept-Language", _options.AcceptLanguage);
        headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");
        headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");

        // Extra headers often carry a firewall allowlist secret, so they only go to the site being crawled, never to
        // other sites: not to their links, their robots.txt or a redirect that leads there. Nor over plain http when
        // the crawl started on https, where anyone on the way could read them.
        if (_scope.IsInternal(uri) && (uri.Scheme == Uri.UriSchemeHttps || !_startsOnHttps))
        {
            foreach (var (name, value) in _options.ExtraHeaders)
            {
                headers.Remove(name);
                headers.TryAddWithoutValidation(name, value);
            }
        }

        return request;
    }

    private static async Task<PooledBuffer> ReadBodyAsync(HttpResponseMessage response, int limit, CancellationToken cancellationToken)
    {
        var raw = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var counting = new CountingStream(raw);
        await using var stream = Decompress(counting, response.Content.Headers.ContentEncoding);

        var buffer = ArrayPool<byte>.Shared.Rent(Math.Min(INITIAL_BUFFER_BYTES, limit + 1));
        var length = 0;
        try
        {
            while (length <= limit)
            {
                if (length == buffer.Length)
                {
                    var larger = ArrayPool<byte>.Shared.Rent(Math.Min(buffer.Length * 2, limit + 1));
                    buffer.AsSpan(0, length).CopyTo(larger);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = larger;
                }

                var read = await stream.ReadAsync(buffer.AsMemory(length, Math.Min(buffer.Length, limit + 1) - length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                length += read;
            }

            var truncated = length > limit;
            return new PooledBuffer(buffer, truncated ? limit : length, truncated, counting.BytesRead);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    /// <summary>
    /// Reads an image's size in pixels from the first bytes of its body as sent (<see cref="ImageHeader"/>), at most
    /// <see cref="ImageHeader.MAX_JPEG_BYTES"/> of them, and stops there unless asked to <paramref name="measure"/>: then it
    /// counts the body's bytes off the wire to the end without keeping them, up to <see cref="MAX_MEASURED_IMAGE_BYTES"/>.
    /// The count is null when the transfer fails part-way (a partial count would understate the size, and the response
    /// itself is fine); the pixels are what the bytes read gave, if anything.
    /// </summary>
    private static async Task<(long? Measured, (int Width, int Height)? Pixels)> ReadImageAsync(HttpResponseMessage response, bool measure,
        CancellationToken timeoutToken, CancellationToken cancellationToken)
    {
        var head = ArrayPool<byte>.Shared.Rent(ImageHeader.MAX_JPEG_BYTES);
        var scratch = measure ? ArrayPool<byte>.Shared.Rent(MEASURE_BUFFER_BYTES) : null;
        (int Width, int Height)? pixels = null;
        try
        {
            await using var raw = await response.Content.ReadAsStreamAsync(timeoutToken).ConfigureAwait(false);
            long total = 0;
            var filled = 0;
            var reading = true;
            while (total < MAX_MEASURED_IMAGE_BYTES)
            {
                int read;
                if (reading)
                {
                    var most = filled < 2 ? ImageHeader.MAX_OTHER_BYTES : ImageHeader.MostToRead(head.AsSpan(0, filled));
                    read = await raw.ReadAsync(head.AsMemory(filled, Math.Min(most - filled, MEASURE_BUFFER_BYTES)), timeoutToken).ConfigureAwait(false);
                    filled += read;
                    int width = 0, height = 0;
                    var result = read == 0 ? ImageHeader.Result.Unknown : ImageHeader.TryRead(head.AsSpan(0, filled), out width, out height);
                    if (result == ImageHeader.Result.Found)
                    {
                        pixels = (width, height);
                    }

                    reading = result == ImageHeader.Result.NeedMore && filled < ImageHeader.MostToRead(head.AsSpan(0, filled));
                }
                else if (measure)
                {
                    read = await raw.ReadAsync(scratch, timeoutToken).ConfigureAwait(false);
                }
                else
                {
                    return (null, pixels);
                }

                if (read == 0)
                {
                    break;
                }

                total += read;
            }

            return (measure ? Math.Min(total, MAX_MEASURED_IMAGE_BYTES) : null, pixels);
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException ||
                                          (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return (null, pixels);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(head);
            if (scratch is not null)
            {
                ArrayPool<byte>.Shared.Return(scratch);
            }
        }
    }

    private static Stream Decompress(Stream raw, ICollection<string> contentEncoding) =>
        contentEncoding.LastOrDefault()?.Trim().ToLowerInvariant() switch
        {
            "gzip" or "x-gzip" => new GZipStream(raw, CompressionMode.Decompress),
            "br" => new BrotliStream(raw, CompressionMode.Decompress),
            "deflate" => new ZLibStream(raw, CompressionMode.Decompress),
            _ => raw
        };

    private static List<ResponseHeader> CollectHeaders(HttpResponseMessage response)
    {
        List<ResponseHeader> headers = [];
        foreach (var (name, values) in response.Headers.NonValidated.Concat(response.Content.Headers.NonValidated))
        {
            var lowerName = name.ToLowerInvariant();
            foreach (var value in values)
            {
                if (headers.Count == MAX_HEADERS)
                {
                    return headers;
                }

                headers.Add(new ResponseHeader(lowerName, value));
            }
        }

        return headers;
    }

    private static Uri? ResolveLocation(Uri requestUri, HttpResponseMessage response)
    {
        var location = response.Headers.Location;
        if (location is null)
        {
            return null;
        }

        var absolute = location.IsAbsoluteUri ? location : new Uri(requestUri, location);
        return UrlNormalizer.IsHttp(absolute) ? absolute : null;
    }

    /// <summary>The response's Retry-After as a wait from now, for the fetches beside this one (sitemaps, the icon) too.</summary>
    internal TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var wait = date - _timeProvider.GetUtcNow();
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return null;
    }

    private static FetchOutcome Classify(int statusCode, string? blockReason) =>
        blockReason is not null
            ? FetchOutcome.Blocked
            : statusCode switch
            {
                >= 200 and < 300 => FetchOutcome.Success,
                >= 300 and < 400 => FetchOutcome.Redirect,
                >= 400 and < 500 => FetchOutcome.ClientError,
                _ => FetchOutcome.ServerError
            };

    private static FetchResponse Failed(Uri uri, FetchOutcome outcome, string error, bool transient, Stopwatch stopwatch) => new()
    {
        RequestUri = uri,
        Outcome = outcome,
        Error = error,
        IsTransientFailure = transient,
        TotalMs = (int)stopwatch.ElapsedMilliseconds
    };

    private static string Describe(HttpRequestException exception) => exception.HttpRequestError switch
    {
        HttpRequestError.NameResolutionError => "DNS lookup failed",
        HttpRequestError.ConnectionError => $"Could not connect: {exception.InnerException?.Message ?? exception.Message}",
        HttpRequestError.SecureConnectionError => $"TLS handshake failed: {exception.InnerException?.Message ?? exception.Message}",
        _ => exception.Message
    };
}
