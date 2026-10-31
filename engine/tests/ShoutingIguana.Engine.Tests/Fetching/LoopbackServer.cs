using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ShoutingIguana.Engine.Tests.Fetching;

/// <summary>
/// Kestrel on two loopback ports, answering with a test's own handler. A different port counts as a different site,
/// so <see cref="OtherBaseUri"/> plays another host.
/// </summary>
internal sealed class LoopbackServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private LoopbackServer(WebApplication app, Uri baseUri, Uri otherBaseUri)
    {
        _app = app;
        BaseUri = baseUri;
        OtherBaseUri = otherBaseUri;
    }

    public Uri BaseUri { get; }

    public Uri OtherBaseUri { get; }

    public static async Task<LoopbackServer> StartAsync(RequestDelegate handler)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Listen(IPAddress.Loopback, 0);
            kestrel.Listen(IPAddress.Loopback, 0);
        });

        var app = builder.Build();
        app.Run(handler);
        await app.StartAsync();

        var addresses = app.Urls.Select(address => new Uri(address)).OrderBy(address => address.Port).ToList();
        return new LoopbackServer(app, addresses[0], addresses[1]);
    }

    public Uri Url(string pathAndQuery) => new(BaseUri, pathAndQuery);

    public Uri OtherUrl(string pathAndQuery) => new(OtherBaseUri, pathAndQuery);

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
