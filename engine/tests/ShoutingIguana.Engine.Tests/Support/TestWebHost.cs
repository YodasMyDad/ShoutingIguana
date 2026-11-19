using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
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
    private readonly ConcurrentDictionary<string, bool> _cutShort = new(StringComparer.Ordinal);
    private WebApplication _app = null!;
    private Func<string, bool, SyntheticResponse> _handler = null!;
    private int _externalPort;

    private TestWebHost()
    {
    }

    public Uri BaseUri { get; private set; } = null!;

    public Uri ExternalBaseUri { get; private set; } = null!;

    /// <param name="createHandler">Given both base URIs, returns the response for a path and whether it came to the external port.</param>
    public static async Task<TestWebHost> StartAsync(Func<Uri, Uri, Func<string, bool, SyntheticResponse>> createHandler)
    {
        var host = new TestWebHost();
        ListenOptions? site = null, external = null;
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;

            // Port 0: the OS picks free ports as Kestrel binds them, so tests running side by side can't collide.
            kestrel.Listen(IPAddress.Loopback, 0, listen => site = listen);
            kestrel.Listen(IPAddress.Loopback, 0, listen => external = listen);
        });

        host._app = builder.Build();
        host._app.Run(host.RespondAsync);
        await host._app.StartAsync();

        // Nobody knows the ports until now, so no request can arrive before the handler is set.
        host._externalPort = ((IPEndPoint)external!.EndPoint).Port;
        host.BaseUri = new Uri($"http://127.0.0.1:{((IPEndPoint)site!.EndPoint).Port}/");
        host.ExternalBaseUri = new Uri($"http://127.0.0.1:{host._externalPort}/");
        host._handler = createHandler(host.BaseUri, host.ExternalBaseUri);
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

    /// <summary>
    /// Every request for the path gets its status, its headers and the first half of its body, then the connection breaks:
    /// a server that fell over mid-file. <see cref="SendWhole"/> ends it.
    /// </summary>
    public void CutShort(string path) => _cutShort[path] = true;

    public void SendWhole(string path) => _cutShort.TryRemove(path, out _);

    public async ValueTask DisposeAsync()
    {
        foreach (var path in _holds.Keys)
        {
            Release(path);
        }

        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private async Task RespondAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";
        if (_holds.TryGetValue(path, out var hold))
        {
            await hold.Task.WaitAsync(context.RequestAborted);
        }

        var response = _handler(path, context.Connection.LocalPort == _externalPort);
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

        if (_cutShort.ContainsKey(path) && (response.Bytes ?? (response.Text is null ? null : Encoding.UTF8.GetBytes(response.Text))) is { } whole)
        {
            // The length promises the whole file, so the client knows the half it gets isn't all of it.
            context.Response.ContentLength = whole.Length;
            await context.Response.Body.WriteAsync(whole.AsMemory(0, whole.Length / 2), context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);

            // Long enough for the half to reach the client before the reset does.
            await Task.Delay(200, context.RequestAborted);
            context.Abort();
            return;
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
}
