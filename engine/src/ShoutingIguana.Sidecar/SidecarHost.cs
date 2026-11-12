using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Sidecar.Api;
using ShoutingIguana.Sidecar.Crawls;
using ShoutingIguana.Sidecar.Errors;
using ShoutingIguana.Sidecar.Events;
using ShoutingIguana.Sidecar.Exports;
using ShoutingIguana.Sidecar.Hosting;
using ShoutingIguana.Sidecar.Library;
using ShoutingIguana.Sidecar.Models;
using ShoutingIguana.Sidecar.Options;
using ShoutingIguana.Sidecar.Security;
using ShoutingIguana.Sidecar.Serialization;

namespace ShoutingIguana.Sidecar;

/// <summary>
/// The sidecar's web host on 127.0.0.1. The program runs one; tests start as many as they like in-process, each on a free
/// port with its own folders. One state folder serves one sidecar at a time.
/// </summary>
internal sealed class SidecarHost : IAsyncDisposable
{
    /// <summary>The largest request body accepted; anything larger is refused with 413.</summary>
    public const long MAX_BODY_BYTES = 1024 * 1024;

    private readonly WebApplication _app;
    private readonly StateFolderLock _stateLock;
    private readonly Lock _sync = new();
    private Task? _stopped;

    private SidecarHost(WebApplication app, StateFolderLock stateLock, int port, string token, bool tokenGenerated)
    {
        _app = app;
        _stateLock = stateLock;
        Port = port;
        Token = token;
        TokenGenerated = tokenGenerated;
    }

    public int Port { get; }

    public string Token { get; }

    /// <summary>The sidecar made its token up (development only), so the ready line gives it.</summary>
    public bool TokenGenerated { get; }

    public IServiceProvider Services => _app.Services;

    /// <summary>The one line the program writes to stdout.</summary>
    public string ReadyLine =>
        JsonSerializer.Serialize(new ReadyEvent("ready", Port, SidecarInfo.Version, TokenGenerated ? Token : null), SidecarJsonContext.Default.ReadyEvent);

    /// <exception cref="StateFolderInUseException">Another sidecar is using the state folder.</exception>
    /// <exception cref="IOException">The port is taken, or the server would listen anywhere but 127.0.0.1.</exception>
    /// <exception cref="OperationCanceledException">Shutdown was asked for (Ctrl+C, SIGTERM) before the sidecar was serving.</exception>
    public static async Task<SidecarHost> StartAsync(SidecarOptions options, CancellationToken cancellationToken = default)
    {
        var token = options.Token ?? (options.Development
            ? SidecarSecurity.CreateToken()
            : throw new InvalidOperationException("A sidecar needs a token, unless it runs for development."));

        var stateLock = StateFolderLock.Acquire(options.StateDirectory);
        WebApplication? app = null;
        try
        {
            app = Build(options, token, out var endpoint);

            // The state folder is this sidecar's alone now: whatever part files an earlier one left noted there (it was
            // killed mid-export) are deleted before anything is served, so no export of this run is among them.
            app.Services.GetRequiredService<ExportJournal>().Recover();
            await app.StartAsync(cancellationToken).ConfigureAwait(false);

            // The one endpoint the code configured, bound now (its port is the real one even when 0 was asked for). Nothing
            // the environment set can have added or moved it: PreferHostingUrls is off, so ASPNETCORE_URLS is ignored.
            var bound = (IPEndPoint)endpoint.EndPoint;
            if (!IPAddress.IsLoopback(bound.Address) || !bound.Address.Equals(IPAddress.Loopback))
            {
                throw new IOException($"The engine would have listened on {bound}, not on 127.0.0.1.");
            }

            var host = new SidecarHost(app, stateLock, bound.Port, token, options.Token is null);
            app.Logger.LogInformation("Shouting Iguana sidecar {Version} listening on 127.0.0.1:{Port}; data in {DataDirectory}, state in {StateDirectory}",
                SidecarInfo.Version, host.Port, options.DataDirectory, options.StateDirectory);
            return host;
        }
        catch
        {
            if (app is not null)
            {
                await app.DisposeAsync().ConfigureAwait(false);
            }

            stateLock.Dispose();
            throw;
        }
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
        _stateLock.Dispose();
    }

    private static WebApplication Build(SidecarOptions options, string token, out ListenOptions endpoint)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = [],
            ApplicationName = "si-sidecar",
            EnvironmentName = Environments.Production,
            ContentRootPath = AppContext.BaseDirectory
        });

        // Only what is set here: no settings files. Clearing the sources doesn't unset the host options already read from
        // ASPNETCORE_* (the reason the old "no environment variable can add an endpoint" was wrong), so PreferHostingUrls
        // is forced off: the one Listen endpoint below is then the only one, and ASPNETCORE_URLS is ignored.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection([new(WebHostDefaults.PreferHostingUrlsKey, "false")]);
        SidecarLogging.Configure(builder.Logging, options);
        builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = options.ShutdownTimeout);
        builder.Services.Configure<ConsoleLifetimeOptions>(console => console.SuppressStatusMessages = true);

        ListenOptions? bound = null;
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = MAX_BODY_BYTES;
            kestrel.Listen(IPAddress.Loopback, options.Port, listen => bound = listen);
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
        builder.Services.AddSingleton(new ReadCache());
        builder.Services.AddSingleton<GridData>();
        builder.Services.AddSingleton<ExportJournal>();
        builder.Services.AddSingleton<ExportManager>();

        // Crawls are told to stop as soon as shutdown begins, before the web server waits for requests in flight.
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
        GridEndpoints.Map(app);
        ExportEndpoints.Map(app);
        endpoint = bound!;
        return app;
    }
}
