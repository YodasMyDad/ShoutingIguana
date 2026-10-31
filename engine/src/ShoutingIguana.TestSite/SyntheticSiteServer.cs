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
    /// <summary>The first byte of every TLS connection: a handshake record.</summary>
    private const byte TLS_HANDSHAKE = 0x16;

    private readonly WebApplication _app;

    private SyntheticSiteServer(WebApplication app, SyntheticSite site, Uri baseUri, Uri externalBaseUri, X509Certificate2? certificate)
    {
        _app = app;
        Site = site;
        BaseUri = baseUri;
        ExternalBaseUri = externalBaseUri;
        Certificate = certificate;
    }

    public SyntheticSite Site { get; }

    public Uri BaseUri { get; }

    public Uri ExternalBaseUri { get; }

    /// <summary>The self-signed certificate of an HTTPS site, for the crawler to trust. Null over plain HTTP.</summary>
    public X509Certificate2? Certificate { get; }

    /// <param name="port">0 lets the operating system pick a free port, so parallel test runs never collide.</param>
    public static async Task<SyntheticSiteServer> StartAsync(
        SyntheticSiteOptions options,
        int port = 0,
        int externalPort = 0,
        CancellationToken cancellationToken = default)
    {
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
        SyntheticSite? syntheticSite = null;
        var boundExternalPort = 0;
        app.Run(context => RespondAsync(context, syntheticSite!, boundExternalPort));
        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        var sitePort = ((IPEndPoint)site!.EndPoint).Port;
        boundExternalPort = ((IPEndPoint)external!.EndPoint).Port;
        var baseUri = new Uri($"{(certificate is null ? "http" : "https")}://127.0.0.1:{sitePort}/");
        var externalBaseUri = new Uri($"http://127.0.0.1:{boundExternalPort}/");
        syntheticSite = new SyntheticSite(options, baseUri, externalBaseUri);
        return new SyntheticSiteServer(app, syntheticSite, baseUri, externalBaseUri, certificate);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
        Certificate?.Dispose();
    }

    private static async Task RespondAsync(HttpContext context, SyntheticSite site, int externalPort)
    {
        var response = site.Handle(context.Request.Path.Value ?? "/", context.Connection.LocalPort == externalPort);
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
