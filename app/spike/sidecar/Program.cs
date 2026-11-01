// Dummy sidecar for the step 3 stack spike. It proves the mechanics the real engine sidecar must follow:
// token by environment variable, 127.0.0.1 on port 0, one JSON ready line on stdout, Host + Origin + bearer checks,
// CORS preflight, paged rows with simulated latency, SSE progress, and leaving when its parent goes away.
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Net.Http.Headers;
using SpikeSidecar;

var processStart = Process.GetCurrentProcess().StartTime;

var token = Environment.GetEnvironmentVariable("SI_TOKEN");
if (string.IsNullOrEmpty(token) || token.Length < 32)
{
    Log.Event("fatal", w => w.WriteString("reason", "SI_TOKEN is missing or shorter than 32 characters"));
    return 2;
}
// Children of the sidecar must not inherit the token.
Environment.SetEnvironmentVariable("SI_TOKEN", null);
var expectedAuthorization = Encoding.ASCII.GetBytes("Bearer " + token);

var allowedOrigins = (Environment.GetEnvironmentVariable("SI_ALLOWED_ORIGINS") ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .ToHashSet(StringComparer.Ordinal);
var (latencyMin, latencyMax) = Helpers.ParseRange(Environment.GetEnvironmentVariable("SI_LATENCY_MS"), 20, 50);

if (OperatingSystem.IsWindows())
{
    // Task.Delay otherwise rounds up to the 15.6 ms system tick, so "20-50 ms" would really be 31-62 ms.
    Native.TimeBeginPeriod(1);
}

// Test hook: a slow engine start, to show the shell paints before the sidecar is ready.
if (int.TryParse(Environment.GetEnvironmentVariable("SI_STARTUP_DELAY_MS"), out var startupDelay) && startupDelay > 0)
{
    Thread.Sleep(startupDelay);
}

var builder = WebApplication.CreateSlimBuilder(args);
builder.Logging.ClearProviders(); // stdout carries only the ready line; diagnostics go to stderr
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(3));
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Listen(IPAddress.Loopback, 0);
    kestrel.AddServerHeader = false;
});

var app = builder.Build();
var lifetime = app.Lifetime;
var simulation = new CrawlSimulation();
var stats = new RequestStats();
var boundPort = 0;

app.Use(async (context, next) =>
{
    var request = context.Request;
    var response = context.Response;
    var origin = request.Headers.Origin.ToString();
    var isPreflight = HttpMethods.IsOptions(request.Method)
        && request.Headers.ContainsKey(HeaderNames.AccessControlRequestMethod);

    // DNS rebinding defence: only the exact address we bound is accepted.
    var hostOk = request.Host.Host == "127.0.0.1" && request.Host.Port == boundPort;
    var originOk = origin.Length == 0 || allowedOrigins.Contains(origin);

    if (!hostOk)
    {
        stats.Rejected("host");
        stats.Shape(context, "rejected-host");
        response.StatusCode = StatusCodes.Status421MisdirectedRequest;
        return;
    }

    if (origin.Length > 0)
    {
        if (!originOk)
        {
            stats.Rejected("origin");
            stats.Shape(context, "rejected-origin");
            response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        response.Headers.AccessControlAllowOrigin = origin;
        response.Headers.Append(HeaderNames.Vary, "Origin");
        response.Headers["Timing-Allow-Origin"] = origin;
    }
    response.Headers.CacheControl = "no-store";

    if (isPreflight)
    {
        stats.Preflight();
        stats.Shape(context, "preflight");
        response.Headers.AccessControlAllowMethods = "GET, POST, OPTIONS";
        response.Headers.AccessControlAllowHeaders = "authorization, content-type, last-event-id";
        response.Headers.AccessControlMaxAge = "7200";
        if (string.Equals(request.Headers["Access-Control-Request-Private-Network"], "true", StringComparison.OrdinalIgnoreCase))
        {
            response.Headers["Access-Control-Allow-Private-Network"] = "true";
        }
        response.StatusCode = StatusCodes.Status204NoContent;
        return;
    }

    var authorization = Encoding.ASCII.GetBytes(request.Headers.Authorization.ToString());
    if (!CryptographicOperations.FixedTimeEquals(authorization, expectedAuthorization))
    {
        stats.Rejected("token");
        stats.Shape(context, "rejected-token");
        response.Headers.WWWAuthenticate = "Bearer";
        response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    stats.Shape(context, "ok");
    await next(context);
});

app.MapGet("/api/health", (HttpContext context) => Helpers.WriteJson(context, w =>
{
    w.WriteBoolean("ok", true);
    w.WriteNumber("pid", Environment.ProcessId);
    w.WriteNumber("uptimeMs", Math.Round((DateTime.Now - processStart).TotalMilliseconds));
}));

app.MapGet("/api/echo", (HttpContext context) => Helpers.WriteJson(context, w =>
{
    foreach (var name in new[] { "Origin", "Host", "Referer", "User-Agent", "Sec-Fetch-Site", "Sec-Fetch-Mode", "Sec-Fetch-Dest", "Sec-CH-UA", "Accept-Language" })
    {
        w.WriteString(name, context.Request.Headers[name].ToString());
    }
    w.WriteString("protocol", context.Request.Protocol);
    w.WriteStartArray("headerNames");
    foreach (var header in context.Request.Headers)
    {
        w.WriteStringValue(header.Key);
    }
    w.WriteEndArray();
}));

app.MapGet("/api/urls", (HttpContext context) =>
{
    var query = context.Request.Query;
    var offset = int.TryParse(query["offset"], out var o) ? o : 0;
    var limit = int.TryParse(query["limit"], out var l) ? l : 200;
    return WriteUrlPage(context, offset, limit);
});

// Same data, one fixed URL: the CORS preflight cache is keyed by URL, so this avoids a preflight per page.
app.MapPost("/api/urls/query", async (HttpContext context) =>
{
    using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
    var offset = body.RootElement.TryGetProperty("offset", out var o) ? o.GetInt32() : 0;
    var limit = body.RootElement.TryGetProperty("limit", out var l) ? l.GetInt32() : 200;
    await WriteUrlPage(context, offset, limit);
});

app.MapGet("/api/progress/stream", (HttpContext context) =>
{
    var intervalMs = int.TryParse(context.Request.Query["intervalMs"], out var i) ? Math.Clamp(i, 20, 5_000) : 500;
    stats.SseConnected();
    Log.Event("sse-connect", w =>
    {
        w.WriteString("lastEventId", context.Request.Headers["Last-Event-ID"].ToString());
        w.WriteNumber("intervalMs", intervalMs);
    });
    return TypedResults.ServerSentEvents(ProgressEvents(simulation, intervalMs, context.RequestAborted, lifetime.ApplicationStopping));
});

app.MapGet("/api/debug/stats", (HttpContext context) => Helpers.WriteJson(context, stats.Write));

app.MapPost("/api/debug/exit", (HttpContext context) =>
{
    Log.Event("debug-exit");
    _ = Task.Run(async () =>
    {
        await Task.Delay(50);
        Environment.Exit(3); // a crash, as far as the shell can tell
    });
    context.Response.StatusCode = StatusCodes.Status202Accepted;
    return Task.CompletedTask;
});

await app.StartAsync();
boundPort = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port;

// The one line the shell waits for. Nothing else is ever written to stdout.
Console.Out.Write("{\"event\":\"ready\",\"port\":" + boundPort + "}\n");
Console.Out.Flush();
Log.Event("timing", w =>
{
    w.WriteNumber("pid", Environment.ProcessId);
    w.WriteNumber("processStartToReadyMs", Math.Round((DateTime.Now - processStart).TotalMilliseconds, 1));
    w.WriteString("allowedOrigins", string.Join(",", allowedOrigins));
});

if (int.TryParse(Environment.GetEnvironmentVariable("SI_PARENT_PID"), out var parentPid))
{
    Lifecycle.WatchParent(parentPid, lifetime);
}
if (Environment.GetEnvironmentVariable("SI_WATCH_STDIN") == "1")
{
    Lifecycle.WatchStdin(lifetime);
}

await app.WaitForShutdownAsync();
Log.Event("exit", w => w.WriteString("reason", Lifecycle.Reason));
return 0;

async Task WriteUrlPage(HttpContext context, int offset, int limit)
{
    offset = Math.Clamp(offset, 0, UrlRowGenerator.Total);
    limit = Math.Clamp(limit, 1, 1_000);
    var delay = latencyMin + (int)(Helpers.Mix((ulong)offset) % (ulong)(latencyMax - latencyMin + 1));
    var stopwatch = Stopwatch.StartNew();
    try
    {
        await Task.Delay(delay, context.RequestAborted);
    }
    catch (OperationCanceledException)
    {
        stats.Aborted();
        return;
    }
    stats.Latency(stopwatch.Elapsed.TotalMilliseconds);
    var end = Math.Min(UrlRowGenerator.Total, offset + limit);
    await Helpers.WriteJson(context, w =>
    {
        w.WriteNumber("total", UrlRowGenerator.Total);
        w.WriteNumber("offset", offset);
        w.WriteStartArray("rows");
        for (var index = offset; index < end; index++)
        {
            UrlRowGenerator.Write(w, index);
        }
        w.WriteEndArray();
    });
}

static async IAsyncEnumerable<SseItem<string>> ProgressEvents(
    CrawlSimulation simulation, int intervalMs, [EnumeratorCancellation] CancellationToken aborted, CancellationToken stopping)
{
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(aborted, stopping);
    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(intervalMs));
    long id = 0;
    var t = simulation.ElapsedSeconds;
    var lastNotice = simulation.NoticeIndex(t);
    yield return new SseItem<string>(simulation.ProgressJson(t), "progress")
    {
        EventId = (++id).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ReconnectionInterval = TimeSpan.FromSeconds(1),
    };
    while (true)
    {
        bool ticked;
        try
        {
            ticked = await timer.WaitForNextTickAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            ticked = false;
        }
        if (!ticked)
        {
            yield break;
        }
        t = simulation.ElapsedSeconds;
        yield return new SseItem<string>(simulation.ProgressJson(t), "progress")
        {
            EventId = (++id).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        var notice = simulation.NoticeIndex(t);
        if (notice != lastNotice)
        {
            lastNotice = notice;
            yield return new SseItem<string>(simulation.NoticeJson(notice), "notice")
            {
                EventId = (++id).ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
        }
    }
}

internal static partial class Native
{
    [LibraryImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    internal static partial uint TimeBeginPeriod(uint period);
}

internal static class Log
{
    private static readonly Lock Gate = new();

    public static void Event(string name, Action<Utf8JsonWriter>? fields = null)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("event", name);
            writer.WriteString("at", DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            fields?.Invoke(writer);
            writer.WriteEndObject();
        }
        lock (Gate)
        {
            Console.Error.WriteLine(Encoding.UTF8.GetString(buffer.WrittenSpan));
        }
    }
}

internal static class Lifecycle
{
    private static int _stopping;

    public static string Reason { get; private set; } = "host-stopped";

    public static void WatchParent(int parentPid, IHostApplicationLifetime lifetime)
    {
        try
        {
            var parent = Process.GetProcessById(parentPid);
            parent.EnableRaisingEvents = true;
            parent.Exited += (_, _) => Stop(lifetime, "parent-exited");
            Log.Event("parent-watch", w => w.WriteNumber("parentPid", parentPid));
            if (parent.HasExited)
            {
                Stop(lifetime, "parent-exited");
            }
        }
        catch (ArgumentException)
        {
            Stop(lifetime, "parent-missing");
        }
    }

    public static void WatchStdin(IHostApplicationLifetime lifetime)
    {
        var thread = new Thread(() =>
        {
            using var reader = new StreamReader(Console.OpenStandardInput());
            while (reader.ReadLine() is { } line)
            {
                if (line.Trim() == "shutdown")
                {
                    Stop(lifetime, "stdin-shutdown");
                    return;
                }
            }
            Stop(lifetime, "stdin-eof");
        })
        { IsBackground = true, Name = "stdin-watch" };
        thread.Start();
        Log.Event("stdin-watch");
    }

    private static void Stop(IHostApplicationLifetime lifetime, string reason)
    {
        if (Interlocked.Exchange(ref _stopping, 1) == 1)
        {
            return;
        }
        Reason = reason;
        Log.Event("stopping", w => w.WriteString("reason", reason));
        lifetime.StopApplication();
        _ = Task.Run(async () =>
        {
            await Task.Delay(5_000);
            Environment.Exit(4); // graceful stop took too long
        });
    }
}

internal sealed class RequestStats
{
    private readonly ConcurrentDictionary<string, byte> _shapes = new();
    private readonly ConcurrentDictionary<string, long> _rejected = new();
    private readonly Lock _latencyGate = new();
    private readonly List<double> _latencies = [];
    private long _preflights;
    private long _requests;
    private long _aborted;
    private long _sseConnections;

    public void Preflight() => Interlocked.Increment(ref _preflights);

    public void Aborted() => Interlocked.Increment(ref _aborted);

    public void SseConnected() => Interlocked.Increment(ref _sseConnections);

    public void Rejected(string why) => _rejected.AddOrUpdate(why, 1, (_, n) => n + 1);

    public void Latency(double milliseconds)
    {
        Interlocked.Increment(ref _requests);
        lock (_latencyGate)
        {
            _latencies.Add(milliseconds);
        }
    }

    /// <summary>Logs each distinct request shape once, so the exact Origin and Host values a webview sends are on record.</summary>
    public void Shape(HttpContext context, string outcome)
    {
        var headers = context.Request.Headers;
        var path = context.Request.Path.Value ?? "";
        var pathKind = path.StartsWith("/api/urls", StringComparison.Ordinal) ? "/api/urls*" : path;
        var key = string.Join('|', context.Request.Method, pathKind, headers.Origin.ToString(), context.Request.Host.Value,
            headers["Access-Control-Request-Headers"].ToString(), headers["Access-Control-Request-Private-Network"].ToString(),
            headers["Sec-Fetch-Site"].ToString(), headers["Sec-Fetch-Mode"].ToString(), outcome);
        if (!_shapes.TryAdd(key, 0))
        {
            return;
        }
        Log.Event("request-shape", w =>
        {
            w.WriteString("outcome", outcome);
            w.WriteString("method", context.Request.Method);
            w.WriteString("path", pathKind);
            w.WriteString("origin", headers.Origin.ToString());
            w.WriteString("host", context.Request.Host.Value);
            w.WriteString("acrMethod", headers["Access-Control-Request-Method"].ToString());
            w.WriteString("acrHeaders", headers["Access-Control-Request-Headers"].ToString());
            w.WriteString("acrPrivateNetwork", headers["Access-Control-Request-Private-Network"].ToString());
            w.WriteString("secFetchSite", headers["Sec-Fetch-Site"].ToString());
            w.WriteString("secFetchMode", headers["Sec-Fetch-Mode"].ToString());
            w.WriteString("referer", headers.Referer.ToString());
            w.WriteString("userAgent", headers.UserAgent.ToString());
        });
    }

    public void Write(Utf8JsonWriter writer)
    {
        writer.WriteNumber("preflights", Interlocked.Read(ref _preflights));
        writer.WriteNumber("pageRequests", Interlocked.Read(ref _requests));
        writer.WriteNumber("aborted", Interlocked.Read(ref _aborted));
        writer.WriteNumber("sseConnections", Interlocked.Read(ref _sseConnections));
        writer.WriteStartObject("rejected");
        foreach (var (why, count) in _rejected)
        {
            writer.WriteNumber(why, count);
        }
        writer.WriteEndObject();
        double[] sorted;
        lock (_latencyGate)
        {
            sorted = [.. _latencies.Order()];
        }
        writer.WriteNumber("latencyP50Ms", sorted.Length == 0 ? 0 : Math.Round(sorted[sorted.Length / 2], 1));
        writer.WriteNumber("latencyP95Ms", sorted.Length == 0 ? 0 : Math.Round(sorted[(int)(sorted.Length * 0.95)], 1));
        writer.WriteNumber("latencyMaxMs", sorted.Length == 0 ? 0 : Math.Round(sorted[^1], 1));
    }
}

internal static class Helpers
{
    public static (int Min, int Max) ParseRange(string? text, int min, int max)
    {
        var parts = (text ?? "").Split('-');
        return parts.Length == 2 && int.TryParse(parts[0], out var a) && int.TryParse(parts[1], out var b) && a <= b
            ? (a, b)
            : (min, max);
    }

    public static ulong Mix(ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    public static async Task WriteJson(HttpContext context, Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>(4_096);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength = buffer.WrittenCount;
        await context.Response.Body.WriteAsync(buffer.WrittenMemory, context.RequestAborted);
    }
}
