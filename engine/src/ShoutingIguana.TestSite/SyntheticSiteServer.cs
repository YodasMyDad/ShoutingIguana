using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.TestSite;

/// <summary>
/// Serves a <see cref="SyntheticSite"/> on two loopback ports. The second port plays the part of an
/// external site, because a different port counts as a different site.
/// </summary>
public sealed class SyntheticSiteServer : IAsyncDisposable
{
    /// <summary>
    /// The control route that switches a running site's revision: <c>POST /__si/revision/{n}</c> on the site's port, with
    /// <see cref="CONTROL_TOKEN_HEADER"/> set to <see cref="ControlToken"/>. It is for a site run in another process (the
    /// CLI's <c>testsite</c>), which prints the token with its ready line.
    /// </summary>
    public const string REVISION_ROUTE = "/__si/revision/";

    /// <summary>The request header that carries <see cref="ControlToken"/>.</summary>
    public const string CONTROL_TOKEN_HEADER = "X-SI-Token";

    /// <summary>The first byte of every TLS connection: a handshake record.</summary>
    private const byte TLS_HANDSHAKE = 0x16;

    private readonly WebApplication _app;
    private readonly SyntheticRateLimiter? _limiter;
    private readonly SyntheticShopifyStore? _store;
    private readonly SiteHolder _holder;

    private SyntheticSiteServer(WebApplication app, SiteHolder holder, Uri baseUri, Uri externalBaseUri, X509Certificate2? certificate,
        SyntheticRequestLog requests, SyntheticRateLimiter? limiter, SyntheticShopifyStore? store, string controlToken)
    {
        _app = app;
        _limiter = limiter;
        _store = store;
        _holder = holder;
        BaseUri = baseUri;
        ExternalBaseUri = externalBaseUri;
        Certificate = certificate;
        Requests = requests;
        ControlToken = controlToken;
    }

    /// <summary>The site as it is served now, at <see cref="Revision"/>.</summary>
    public SyntheticSite Site => _holder.Current;

    /// <summary>
    /// The revision the site serves (<see cref="SyntheticSiteOptions.Revision"/>). Setting it switches every response from
    /// the next request on, as a site's owner publishing a fix would; the URLs and ports stay the same.
    /// </summary>
    public int Revision
    {
        get => _holder.Current.Revision;
        set => _holder.Current = _holder.Current.AtRevision(value);
    }

    /// <summary>The secret <see cref="REVISION_ROUTE"/> requires, so nothing that merely finds the port can switch the site.</summary>
    public string ControlToken { get; }

    /// <summary>What the crawler sent the site, for tests of its options.</summary>
    public SyntheticRequestLog Requests { get; }

    public Uri BaseUri { get; }

    public Uri ExternalBaseUri { get; }

    /// <summary>The 429s the product pages' rate limit (<see cref="SyntheticSiteOptions.RateLimit"/>) has answered with.</summary>
    public long RateLimited => _limiter?.Limited ?? 0;

    /// <summary>As a Shopify store (<see cref="SyntheticSiteOptions.Shopify"/>): the requests that carried a signature to the store under its own name.</summary>
    public long ShopifySigned => _store?.Signed ?? 0;

    /// <summary>As a Shopify store: the product requests it refused with 429.</summary>
    public long ShopifyRefused => _store?.Refused ?? 0;

    /// <summary>
    /// As a Shopify store: the requests that carried a signature anywhere else, to the external site or to the store under
    /// another host's name (<c>localhost</c>). A crawler must never send one.
    /// </summary>
    public long ShopifySignedElsewhere => _store?.SignedElsewhere ?? 0;

    /// <summary>The self-signed certificate of an HTTPS site, for the crawler to trust. Null over plain HTTP.</summary>
    public X509Certificate2? Certificate { get; }

    /// <param name="port">0 lets the operating system pick a free port, so parallel test runs never collide.</param>
    public static async Task<SyntheticSiteServer> StartAsync(
        SyntheticSiteOptions options,
        int port = 0,
        int externalPort = 0,
        CancellationToken cancellationToken = default)
    {
        if (options.Shopify is not null && !options.Https)
        {
            throw new ArgumentException("A Shopify store is served over https: set Https too.", nameof(options));
        }

        var certificate = options.Https ? CreateCertificate() : null;
        ListenOptions? site = null, external = null;
        ConnectionDelegate? plainHttp = null;

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Listen(IPAddress.Loopback, port, listen =>
            {
                site = listen;
                if (certificate is null)
                {
                    return;
                }

                // Real HTTPS sites answer plain HTTP too (on port 80); here both share one port, told apart by the first
                // byte. Plain connections skip the TLS layer and go straight to the HTTP handler behind it.
                listen.Protocols = HttpProtocols.Http1AndHttp2;
                listen.Use(next => async connection =>
                {
                    var input = connection.Transport.Input;
                    var read = await input.ReadAsync(connection.ConnectionClosed).ConfigureAwait(false);
                    var isTls = !read.Buffer.IsEmpty && read.Buffer.FirstSpan[0] == TLS_HANDSHAKE;
                    input.AdvanceTo(read.Buffer.Start);
                    await (isTls ? next : plainHttp!)(connection).ConfigureAwait(false);
                });
                listen.UseHttps(certificate);
                listen.Use(next => plainHttp = next);
            });
            kestrel.Listen(IPAddress.Loopback, externalPort, listen => external = listen);
        });

        if (options.Compress)
        {
            builder.Services.AddResponseCompression(compression =>
            {
                compression.EnableForHttps = true;
                compression.Providers.Add<GzipCompressionProvider>();
                compression.MimeTypes = ["text/html", "application/xml", "text/css", "application/javascript", "text/plain"];
            });
            builder.Services.Configure<GzipCompressionProviderOptions>(gzip => gzip.Level = CompressionLevel.Fastest);
        }

        var app = builder.Build();
        if (options.Compress)
        {
            app.UseResponseCompression();
        }

        // The ports are only known once Kestrel has bound them, and the site renders absolute URLs, so it's created after
        // the start. Nothing can request a page before then: nobody knows the ports yet.
        var holder = new SiteHolder();
        var boundExternalPort = 0;
        var requests = new SyntheticRequestLog();
        var limiter = options.RateLimit is { } limit ? new SyntheticRateLimiter(limit, TimeProvider.System) : null;
        var store = options.Shopify is { } shopify ? new SyntheticShopifyStore(shopify, TimeProvider.System) : null;
        var controlToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        app.Run(context => RespondAsync(context, holder, boundExternalPort, requests, limiter, store, controlToken));
        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        var sitePort = ((IPEndPoint)site!.EndPoint).Port;
        boundExternalPort = ((IPEndPoint)external!.EndPoint).Port;
        var baseUri = new Uri($"{(certificate is null ? "http" : "https")}://127.0.0.1:{sitePort}/");
        var externalBaseUri = new Uri($"http://127.0.0.1:{boundExternalPort}/");
        holder.Current = new SyntheticSite(options, baseUri, externalBaseUri);
        return new SyntheticSiteServer(app, holder, baseUri, externalBaseUri, certificate, requests, limiter, store, controlToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
        Certificate?.Dispose();
    }

    private static async Task RespondAsync(HttpContext context, SiteHolder holder, int externalPort, SyntheticRequestLog requests,
        SyntheticRateLimiter? limiter, SyntheticShopifyStore? store, string controlToken)
    {
        var path = context.Request.Path.Value ?? "/";
        var site = holder.Current;
        if (context.Connection.LocalPort != externalPort && path.StartsWith(REVISION_ROUTE, StringComparison.Ordinal))
        {
            context.Response.StatusCode = SwitchRevision(context.Request, path[REVISION_ROUTE.Length..], holder, controlToken);
            return;
        }

        if (context.Connection.LocalPort == externalPort)
        {
            store?.SawExternal(context.Request);
            await ServeAsync(context, site.Handle(path, external: true)).ConfigureAwait(false);
            return;
        }

        requests.Started(context.Request);
        try
        {
            var sitePort = context.Connection.LocalPort;
            var response = store?.Refuse(context.Request, $"127.0.0.1:{sitePort}") ?? (limiter?.IsOverTheLimit(path) == true
                ? new SyntheticResponse(429, "text/html; charset=utf-8", "<html><body>Too many requests</body></html>", null, null,
                    new Dictionary<string, string> { ["Retry-After"] = limiter.RetryAfter })
                : store is not null && path == SyntheticShopify.AWAY_PATH
                    ? SyntheticShopifyStore.Away(sitePort)
                    : site.Handle(path, external: false, plainHttp: !context.Request.IsHttps));
            response = await AuthPageAsync(site, context.Request, path, response).ConfigureAwait(false);
            response = BrowserPage(site, context.Request, path, response);
            await ServeAsync(context, store?.Dress(response, path, sitePort) ?? response).ConfigureAwait(false);
        }
        finally
        {
            requests.Finished();
        }
    }

    /// <summary>The control route: the status to answer with, having switched the revision when the request may.</summary>
    private static int SwitchRevision(HttpRequest request, string revision, SiteHolder holder, string controlToken)
    {
        if (!HttpMethods.IsPost(request.Method))
        {
            return StatusCodes.Status405MethodNotAllowed;
        }

        var token = request.Headers[CONTROL_TOKEN_HEADER].ToString();
        if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(token), System.Text.Encoding.ASCII.GetBytes(controlToken)))
        {
            return StatusCodes.Status403Forbidden;
        }

        if (!int.TryParse(revision, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) ||
            number > SyntheticSite.LATEST_REVISION)
        {
            return StatusCodes.Status404NotFound;
        }

        holder.Current = holder.Current.AtRevision(number);
        return StatusCodes.Status204NoContent;
    }

    /// <summary>
    /// The auth plants. They are not linked from the site, so a crawl that doesn't start on one never asks. HTTP Basic
    /// and Digest answer 401 until the password is right. The cookie gate answers 403 until the session cookie is sent.
    /// The form sets that cookie on a correct POST and redirects to the member page, which is 403 without it.
    /// </summary>
    private static async Task<SyntheticResponse> AuthPageAsync(SyntheticSite site, HttpRequest request, string path, SyntheticResponse response)
    {
        if (path == SyntheticSite.HTTP_AUTH_PATH)
        {
            return BasicOk(request) ? site.SignedInDocument(path) : Challenge("Basic realm=\"Synthetic\"");
        }

        if (path == SyntheticSite.DIGEST_AUTH_PATH)
        {
            return DigestOk(request, path)
                ? site.SignedInDocument(path)
                : Challenge("Digest realm=\"Synthetic\", nonce=\"planted\", algorithm=MD5, qop=\"auth\"");
        }

        if (path == SyntheticSite.COOKIE_GATE_PATH || path == SyntheticSite.FORM_MEMBER_PATH)
        {
            return HasSession(request) ? site.SignedInDocument(path) : Locked();
        }

        if (path == SyntheticSite.FORM_LOGIN_PATH)
        {
            if (HttpMethods.IsPost(request.Method))
            {
                var form = await request.ReadFormAsync().ConfigureAwait(false);
                if (form["username"] == SyntheticSite.AUTH_USER && form["password"] == SyntheticSite.AUTH_PASSWORD)
                {
                    return new SyntheticResponse(302, "text/html", null, null, SyntheticSite.FORM_MEMBER_PATH,
                        new Dictionary<string, string> { ["Set-Cookie"] = SyntheticSite.AUTH_COOKIE + "; Path=/" });
                }
            }

            return site.SignInForm();
        }

        return response;
    }

    private static SyntheticResponse Challenge(string authenticate) =>
        new(401, "text/html", "<html><head><title>Sign in</title></head><body><p>Sign in.</p></body></html>", null, null,
            new Dictionary<string, string> { ["WWW-Authenticate"] = authenticate });

    private static SyntheticResponse Locked() =>
        new(403, "text/html", "<html><head><title>Members</title></head><body><p>Sign in first.</p></body></html>", null, null);

    private static bool HasSession(HttpRequest request) =>
        request.Headers.Cookie.ToString().Contains(SyntheticSite.AUTH_COOKIE, StringComparison.Ordinal);

    private static bool BasicOk(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var text = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..].Trim()));
            return text == SyntheticSite.AUTH_USER + ":" + SyntheticSite.AUTH_PASSWORD;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool DigestOk(HttpRequest request, string path)
    {
        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith("Digest ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in header["Digest ".Length..].Split(','))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            fields[part[..eq].Trim()] = part[(eq + 1)..].Trim().Trim('"');
        }

        if (fields.GetValueOrDefault("username") != SyntheticSite.AUTH_USER ||
            !fields.TryGetValue("response", out var given) ||
            !fields.TryGetValue("nonce", out var nonce))
        {
            return false;
        }

        var uri = fields.GetValueOrDefault("uri") ?? path;
        var ha1 = Md5($"{SyntheticSite.AUTH_USER}:Synthetic:{SyntheticSite.AUTH_PASSWORD}");
        var ha2 = Md5($"{request.Method}:{uri}");
        var expected = fields.TryGetValue("qop", out var qop) && qop.Length > 0
            ? Md5($"{ha1}:{nonce}:{fields.GetValueOrDefault("nc")}:{fields.GetValueOrDefault("cnonce")}:{qop}:{ha2}")
            : Md5($"{ha1}:{nonce}:{ha2}");
        return string.Equals(expected, given, StringComparison.OrdinalIgnoreCase);
    }

    private static string Md5(string text)
    {
        var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.ASCII.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// The planted browser-fallback page. Plain HTTP is the challenge <see cref="SyntheticSite.Handle"/> already
    /// built. A browser request (Chromium's <c>Sec-Fetch-Mode</c>) is the page. The page it links is not a plant: a
    /// crawl that never reads the first page in a browser never asks for it.
    /// </summary>
    private static SyntheticResponse BrowserPage(SyntheticSite site, HttpRequest request, string path, SyntheticResponse response)
    {
        if (path == SyntheticSite.BROWSER_FALLBACK_NEXT_PATH)
        {
            return site.BrowserFallbackNextDocument();
        }

        if (path is SyntheticSite.HUMAN_CHECK_PATH or SyntheticSite.HUMAN_CHECK_ASK_PATH or SyntheticSite.HUMAN_CHECK_MOVED_PATH or
            SyntheticSite.HUMAN_CHECK_NEXT_PATH or SyntheticSite.HUMAN_CHECK_LAST_PATH)
        {
            return HumanCheck(site, request, path);
        }

        if (path == SyntheticSite.BROWSER_FALLBACK_PATH &&
            (request.Headers.ContainsKey("Sec-Fetch-Mode") || request.Headers.ContainsKey("sec-ch-ua")))
        {
            return site.BrowserFallbackDocument();
        }

        return response;
    }

    /// <summary>
    /// The pages behind a human check (<see cref="SyntheticSite.HUMAN_CHECK_PATH"/>). The pass is a cookie made from the
    /// user agent that asks, so a pass handed to another client is refused, as Cloudflare's is.
    /// </summary>
    private static SyntheticResponse HumanCheck(SyntheticSite site, HttpRequest request, string path)
    {
        var clearance = SyntheticSite.Clearance(request.Headers.UserAgent.ToString());
        var ask = path is SyntheticSite.HUMAN_CHECK_ASK_PATH or SyntheticSite.HUMAN_CHECK_MOVED_PATH;
        var passed = path switch
        {
            SyntheticSite.HUMAN_CHECK_PATH => request.Cookies[SyntheticSite.HUMAN_CHECK_COOKIE] == clearance,
            SyntheticSite.HUMAN_CHECK_ASK_PATH or SyntheticSite.HUMAN_CHECK_MOVED_PATH => request.Cookies[SyntheticSite.HUMAN_CHECK_ASK_COOKIE] == clearance,
            _ => request.Cookies[SyntheticSite.HUMAN_CHECK_COOKIE] == clearance || request.Cookies[SyntheticSite.HUMAN_CHECK_ASK_COOKIE] == clearance
        };
        if (!passed)
        {
            return SyntheticSite.HumanCheckChallenge(ask, clearance);
        }

        // The first page with the pass is answered once with a redirect to itself, as a firewall does when it sets
        // its cookie: the browser asks again, and a crawl that reads one request at a time must not wait on its own first ask.
        if (path == SyntheticSite.HUMAN_CHECK_PATH && request.Cookies[SyntheticSite.HUMAN_CHECK_HOP_COOKIE] != "1")
        {
            return new SyntheticResponse(302, "text/html; charset=utf-8", null, null, SyntheticSite.HUMAN_CHECK_PATH,
                new Dictionary<string, string> { ["Set-Cookie"] = SyntheticSite.HUMAN_CHECK_HOP_COOKIE + "=1; Path=/" });
        }

        if (path == SyntheticSite.HUMAN_CHECK_MOVED_PATH)
        {
            // The page the check let through loads again, and this time the site sends the visitor on.
            var away = request.Query.ContainsKey("away");
            var target = away
                ? $"http://localhost:{request.Host.Port}{SyntheticSite.HUMAN_CHECK_LAST_PATH}"
                : SyntheticSite.HUMAN_CHECK_NEXT_PATH;
            return new SyntheticResponse(302, "text/html; charset=utf-8", null, null, target);
        }

        return path == SyntheticSite.HUMAN_CHECK_NEXT_PATH
            ? new SyntheticResponse(301, "text/html; charset=utf-8", null, null, SyntheticSite.HUMAN_CHECK_LAST_PATH)
            : site.HumanCheckDocument(path);
    }

    private static async Task ServeAsync(HttpContext context, SyntheticResponse response)
    {
        if (response.Abort)
        {
            context.Abort();
            return;
        }

        if (response.DelayMs > 0)
        {
            await Task.Delay(response.DelayMs, context.RequestAborted).ConfigureAwait(false);
        }

        context.Response.StatusCode = response.StatusCode;
        context.Response.ContentType = response.ContentType;
        if (response.Location is not null)
        {
            context.Response.Headers.Location = response.Location;
        }

        foreach (var (name, value) in response.Headers ?? new Dictionary<string, string>())
        {
            context.Response.Headers[name] = value;
        }

        if (response.Bytes is not null)
        {
            context.Response.ContentLength = response.Bytes.Length;
            await context.Response.Body.WriteAsync(response.Bytes, context.RequestAborted).ConfigureAwait(false);
        }
        else if (response.Text is not null)
        {
            await context.Response.WriteAsync(response.Text, context.RequestAborted).ConfigureAwait(false);
        }
    }

    /// <summary>The site the server answers with: swapped whole when its revision switches, so a request sees one revision.</summary>
    private sealed class SiteHolder
    {
        private SyntheticSite? _current;

        public SyntheticSite Current
        {
            get => Volatile.Read(ref _current)!;
            set => Volatile.Write(ref _current, value);
        }
    }

    /// <summary>A throwaway certificate for 127.0.0.1, valid for a week either side of now.</summary>
    private static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Synthetic Store", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow.AddDays(7));

        // Windows can't serve TLS with the ephemeral key CreateSelfSigned makes, so round-trip it through PKCS#12.
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12), null);
    }
}
