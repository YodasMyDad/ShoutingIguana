using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
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
    private readonly WebApplication _app;

    private SyntheticSiteServer(WebApplication app, SyntheticSite site, Uri baseUri, Uri externalBaseUri)
    {
        _app = app;
        Site = site;
        BaseUri = baseUri;
        ExternalBaseUri = externalBaseUri;
    }

    public SyntheticSite Site { get; }

    public Uri BaseUri { get; }

    public Uri ExternalBaseUri { get; }

    public static async Task<SyntheticSiteServer> StartAsync(
        SyntheticSiteOptions options,
        int port = 0,
        int externalPort = 0,
        CancellationToken cancellationToken = default)
    {
        port = port == 0 ? FindFreePort() : port;
        externalPort = externalPort == 0 ? FindFreePort() : externalPort;
        var baseUri = new Uri($"http://127.0.0.1:{port}/");
        var externalBaseUri = new Uri($"http://127.0.0.1:{externalPort}/");
        var site = new SyntheticSite(options, baseUri, externalBaseUri);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Listen(IPAddress.Loopback, port);
            kestrel.Listen(IPAddress.Loopback, externalPort);
        });

        if (options.Compress)
        {
            builder.Services.AddResponseCompression(compression =>
            {
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

        app.Run(context => RespondAsync(context, site, externalPort));
        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        return new SyntheticSiteServer(app, site, baseUri, externalBaseUri);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
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

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
