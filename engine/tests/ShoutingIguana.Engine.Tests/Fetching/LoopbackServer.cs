using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;

namespace ShoutingIguana.Engine.Tests.Fetching;

/// <summary>
/// Kestrel on two loopback ports, answering with a test's own handler. A different port counts as a different site,
/// so <see cref="OtherBaseUri"/> plays another host. Over https, <see cref="BaseUri"/>'s port answers plain http too, as a
/// site's two schemes do, and <see cref="HttpRequest.IsHttps"/> tells them apart.
/// </summary>
internal sealed class LoopbackServer : IAsyncDisposable
{
    /// <summary>The first byte of every TLS connection: a handshake record.</summary>
    private const byte TLS_HANDSHAKE = 0x16;

    private readonly WebApplication _app;

    private LoopbackServer(WebApplication app, Uri baseUri, Uri otherBaseUri, X509Certificate2? certificate)
    {
        _app = app;
        BaseUri = baseUri;
        OtherBaseUri = otherBaseUri;
        Certificate = certificate;
    }

    public Uri BaseUri { get; }

    public Uri OtherBaseUri { get; }

    /// <summary>Over https, the self-signed certificate for the crawler to trust (<c>CrawlOptions.TrustedCertificate</c>).</summary>
    public X509Certificate2? Certificate { get; }

    public static async Task<LoopbackServer> StartAsync(RequestDelegate handler, bool https = false)
    {
        var certificate = https ? CreateCertificate() : null;
        ListenOptions? site = null, other = null;
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Listen(IPAddress.Loopback, 0, listen =>
            {
                site = listen;
                if (certificate is not null)
                {
                    ServeBothSchemes(listen, certificate);
                }
            });
            kestrel.Listen(IPAddress.Loopback, 0, listen => other = listen);
        });

        var app = builder.Build();
        app.Run(handler);
        await app.StartAsync();

        var baseUri = new Uri($"{(https ? "https" : "http")}://127.0.0.1:{((IPEndPoint)site!.EndPoint).Port}/");
        return new LoopbackServer(app, baseUri, new Uri($"http://127.0.0.1:{((IPEndPoint)other!.EndPoint).Port}/"), certificate);
    }

    public Uri Url(string pathAndQuery) => new(BaseUri, pathAndQuery);

    public Uri OtherUrl(string pathAndQuery) => new(OtherBaseUri, pathAndQuery);

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        Certificate?.Dispose();
    }

    /// <summary>TLS for connections that start with a handshake; the rest go straight to the HTTP handler behind it.</summary>
    private static void ServeBothSchemes(ListenOptions listen, X509Certificate2 certificate)
    {
        ConnectionDelegate? plainHttp = null;
        listen.Protocols = HttpProtocols.Http1AndHttp2;
        listen.Use(next => async connection =>
        {
            var input = connection.Transport.Input;
            var read = await input.ReadAsync(connection.ConnectionClosed);
            var isTls = !read.Buffer.IsEmpty && read.Buffer.FirstSpan[0] == TLS_HANDSHAKE;
            input.AdvanceTo(read.Buffer.Start);
            await (isTls ? next : plainHttp!)(connection);
        });
        listen.UseHttps(certificate);
        listen.Use(next => plainHttp = next);
    }

    /// <summary>A throwaway certificate for 127.0.0.1, valid for a week either side of now.</summary>
    private static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Loopback", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow.AddDays(7));

        // Windows can't serve TLS with the ephemeral key CreateSelfSigned makes, so round-trip it through PKCS#12.
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12), null);
    }
}
