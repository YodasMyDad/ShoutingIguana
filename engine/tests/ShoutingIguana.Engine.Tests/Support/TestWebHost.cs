using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Support;

/// <summary>
/// A loopback web server for crawl tests that can hold back the response for any path until the test releases it, so
/// a test decides what the crawler sees first. A second port plays an external site, as for the synthetic site.
/// </summary>
internal sealed class TestWebHost : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _holds = new(StringComparer.Ordinal);
    private WebApplication _app = null!;

    private TestWebHost(Uri baseUri, Uri externalBaseUri)
    {
        BaseUri = baseUri;
        ExternalBaseUri = externalBaseUri;
    }

    public Uri BaseUri { get; }

    public Uri ExternalBaseUri { get; }

    /// <param name="createHandler">Given both base URIs, returns the response for a path and whether it came to the external port.</param>
    public static async Task<TestWebHost> StartAsync(Func<Uri, Uri, Func<string, bool, SyntheticResponse>> createHandler)
    {
        var port = FindFreePort();
        var externalPort = FindFreePort();
        var host = new TestWebHost(new Uri($"http://127.0.0.1:{port}/"), new Uri($"http://127.0.0.1:{externalPort}/"));
        var handler = createHandler(host.BaseUri, host.ExternalBaseUri);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Listen(IPAddress.Loopback, port);
            kestrel.Listen(IPAddress.Loopback, externalPort);
        });

        host._app = builder.Build();
        host._app.Run(context => host.RespondAsync(context, handler, externalPort));
        await host._app.StartAsync();
        return host;
    }

    /// <summary>Every request for the path waits until <see cref="Release"/>.</summary>
    public void Hold(string path) => _holds[path] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release(string path)
    {
        if (_holds.TryRemove(path, out var hold))
        {
            hold.TrySetResult();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var path in _holds.Keys)
        {
            Release(path);
        }

        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private async Task RespondAsync(HttpContext context, Func<string, bool, SyntheticResponse> handler, int externalPort)
    {
        var path = context.Request.Path.Value ?? "/";
        if (_holds.TryGetValue(path, out var hold))
        {
            await hold.Task.WaitAsync(context.RequestAborted);
        }

        var response = handler(path, context.Connection.LocalPort == externalPort);
        if (response.Abort)
        {
            context.Abort();
            return;
        }

        if (response.DelayMs > 0)
        {
            await Task.Delay(response.DelayMs, context.RequestAborted);
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
            await context.Response.Body.WriteAsync(response.Bytes, context.RequestAborted);
        }
        else if (response.Text is not null)
        {
            await context.Response.WriteAsync(response.Text, context.RequestAborted);
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
