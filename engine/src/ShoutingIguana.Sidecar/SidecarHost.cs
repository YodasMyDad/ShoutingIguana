using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using ShoutingIguana.Sidecar.Api;
using ShoutingIguana.Sidecar.Crawls;
using ShoutingIguana.Sidecar.Errors;
using ShoutingIguana.Sidecar.Events;
using ShoutingIguana.Sidecar.Hosting;
using ShoutingIguana.Sidecar.Library;
using ShoutingIguana.Sidecar.Models;
using ShoutingIguana.Sidecar.Options;
using ShoutingIguana.Sidecar.Security;
using ShoutingIguana.Sidecar.Serialization;

namespace ShoutingIguana.Sidecar;

/// <summary>
/// The sidecar's web host on 127.0.0.1. The program runs one; tests start as many as they like in-process, each on a free
/// port with its own folders.
/// </summary>
internal sealed class SidecarHost : IAsyncDisposable
{
    /// <summary>The largest request body accepted; anything larger is refused with 413.</summary>
    public const long MAX_BODY_BYTES = 1024 * 1024;

    private readonly WebApplication _app;
    private readonly Lock _sync = new();
    private Task? _stopped;

    private SidecarHost(WebApplication app, int port, string token, bool tokenGenerated)
    {
        _app = app;
        Port = port;
        Token = token;
        TokenGenerated = tokenGenerated;
    }

    public int Port { get; }

    public string Token { get; }

    /// <summary>The sidecar made its token up, so the ready line gives it.</summary>
    public bool TokenGenerated { get; }

    public IServiceProvider Services => _app.Services;

    /// <summary>The one line the program writes to stdout.</summary>
    public string ReadyLine =>
        JsonSerializer.Serialize(new ReadyEvent("ready", Port, SidecarInfo.Version, TokenGenerated ? Token : null), SidecarJsonContext.Default.ReadyEvent);

    public static async Task<SidecarHost> StartAsync(SidecarOptions options, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(options.StateDirectory);
        var token = options.Token ?? SidecarSecurity.CreateToken();
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = [],
            ApplicationName = "si-sidecar",
            EnvironmentName = Environments.Production,
            ContentRootPath = AppContext.BaseDirectory
        });

        // Only what is set here: no settings files, and no environment variable can add an endpoint or change a limit.
        builder.Configuration.Sources.Clear();
        SidecarLogging.Configure(builder.Logging, options);
        builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = options.ShutdownTimeout);
        builder.Services.Configure<ConsoleLifetimeOptions>(console => console.SuppressStatusMessages = true);

        ListenOptions? endpoint = null;
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = MAX_BODY_BYTES;
            kestrel.Listen(IPAddress.Loopback, options.Port, listen => endpoint = listen);
        });

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(new SidecarSecurity(token, options.AllowedOrigins));
        builder.Services.AddSingleton(new AlertLog());
        builder.Services.AddSingleton<EventHub>();
        builder.Services.AddSingleton<CrawlSummaryCache>();
        builder.Services.AddSingleton<CrawlLibrary>();
        builder.Services.AddSingleton<DefaultsStore>();
        builder.Services.AddSingleton<CrawlManager>();
        builder.Services.AddSingleton<CrawlData>();

        // Hosted services registered here stop after the web server, once no request can start anything new.
        builder.Services.AddHostedService<CrawlShutdown>();
        if (options.ParentProcessId is { } parent)
        {
            builder.Services.AddHostedService(services => new ParentProcessWatcher(parent,
                services.GetRequiredService<IHostApplicationLifetime>(), services.GetRequiredService<ILogger<ParentProcessWatcher>>()));
        }

        if (options.StdinLifeline)
        {
            builder.Services.AddHostedService(services => new StdinLifeline(Console.In,
                services.GetRequiredService<IHostApplicationLifetime>(), services.GetRequiredService<ILogger<StdinLifeline>>()));
        }

        var app = builder.Build();
        app.UseMiddleware<ProblemMiddleware>();
        app.UseMiddleware<RequestGuard>();
        AppEndpoints.Map(app);
        OptionsEndpoints.Map(app);
        LibraryEndpoints.Map(app);
        CrawlEndpoints.Map(app);
        AnalysisEndpoints.Map(app);

        try
        {
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await app.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        var host = new SidecarHost(app, ((IPEndPoint)endpoint!.EndPoint).Port, token, options.Token is null);
        app.Logger.LogInformation("Shouting Iguana sidecar {Version} listening on 127.0.0.1:{Port}; data in {DataDirectory}, state in {StateDirectory}",
            SidecarInfo.Version, host.Port, options.DataDirectory, options.StateDirectory);
        return host;
    }

    /// <summary>Completes once the sidecar has shut down: the shutdown call, Ctrl+C, SIGTERM or the parent ending.</summary>
    public async Task WaitForShutdownAsync()
    {
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lifetime = _app.Services.GetRequiredService<IHostApplicationLifetime>();
        await using (lifetime.ApplicationStopping.Register(() => stopping.TrySetResult()).ConfigureAwait(false))
        {
            await stopping.Task.ConfigureAwait(false);
        }

        await StopAsync().ConfigureAwait(false);
    }

    /// <summary>Stops crawls (resumable) and analyses (nothing stored), then the server. Safe to call more than once.</summary>
    public Task StopAsync()
    {
        lock (_sync)
        {
            return _stopped ??= _app.StopAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }
}
