using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Security;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// Plain HTTP fetching for every URL. Redirects are not followed (each hop is recorded as its own URL),
/// compression is decoded here so the transfer size is known, and only bodies that are needed are read:
/// HTML for pages being crawled, a short sample of error pages for block detection, text for robots.txt.
/// </summary>
internal sealed class HttpFetcher : IDisposable
{
    private const int INITIAL_BUFFER_BYTES = 64 * 1024;
    private const int BLOCK_SAMPLE_BYTES = 64 * 1024;
    private const int MAX_HEADERS = 200;
    private const string ACCEPT = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8";

    private readonly HttpClient _client;
    private readonly CrawlOptions _options;
    private readonly string _userAgent;
    private readonly TimeSpan _timeout;

    public HttpFetcher(CrawlOptions options, ResolvedUserAgent userAgent)
    {
        _options = options;
        _userAgent = userAgent.Header;
        _timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);

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
            Proxy = CreateProxy(options.ProxyUrl)
        };

        if (options.TrustedCertificate is { } trusted)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                errors == SslPolicyErrors.None || certificate?.GetCertHashString() == trusted.GetCertHashString();
        }

        _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<FetchResponse> FetchAsync(Uri uri, FetchBody body, CancellationToken cancellationToken, int? maxBodyBytes = null)
    {
        var stopwatch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        try
        {
            using var response = await SendAsync(uri, timeout.Token).ConfigureAwait(false);
            var timeToFirstByte = (int)stopwatch.ElapsedMilliseconds;
            var statusCode = (int)response.StatusCode;
            var headers = CollectHeaders(response);
            var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            var isHtml = contentType is "text/html" or "application/xhtml+xml";
            var isSuccess = statusCode is >= 200 and < 300;

            var readLimit = body switch
            {
                FetchBody.Html when isHtml && isSuccess => maxBodyBytes ?? _options.MaxHtmlBytes,
                FetchBody.Text when isSuccess => maxBodyBytes ?? _options.MaxHtmlBytes,
                _ when isHtml && statusCode is 401 or 403 or 405 or 429 or 503 => BLOCK_SAMPLE_BYTES,
                _ => 0
            };

            string? text = null;
            long? bodyBytes = null;
            long? transferBytes = null;
            var truncated = false;
            if (readLimit > 0)
            {
                using var buffer = await ReadBodyAsync(response, readLimit, timeout.Token).ConfigureAwait(false);
                text = CharsetDecoder.Decode(buffer.Span, response.Content.Headers.ContentType?.CharSet);
                bodyBytes = buffer.Length;
                transferBytes = buffer.TransferBytes;
                truncated = buffer.Truncated;
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
                ContentLength = response.Content.Headers.ContentLength,
                TransferBytes = transferBytes,
                BodyBytes = bodyBytes,
                Body = isSuccess && blockReason is null ? text : null,
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
    public Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken cancellationToken) =>
        _client.SendAsync(CreateRequest(uri), HttpCompletionOption.ResponseHeadersRead, cancellationToken);

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

        foreach (var (name, value) in _options.ExtraHeaders)
        {
            headers.Remove(name);
            headers.TryAddWithoutValidation(name, value);
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

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
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

    private static WebProxy? CreateProxy(string? proxyUrl)
    {
        if (proxyUrl is null)
        {
            return null;
        }

        var uri = new Uri(proxyUrl);
        var proxy = new WebProxy(new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty }.Uri);
        if (uri.UserInfo.Length > 0)
        {
            var parts = uri.UserInfo.Split(':', 2);
            proxy.Credentials = new NetworkCredential(
                Uri.UnescapeDataString(parts[0]),
                parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty);
        }

        return proxy;
    }
}
