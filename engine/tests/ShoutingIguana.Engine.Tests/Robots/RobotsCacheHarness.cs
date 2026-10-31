using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShoutingIguana.Engine.Configuration;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Robots;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Crawling;
using ShoutingIguana.Engine.Tests.Fetching;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Tests.Robots;

/// <summary>
/// A <see cref="RobotsCache"/> wired the way a crawl wires it (gates, retrying fetcher, writer) on a fake clock. The
/// clock is pumped forward while a lookup is in flight, so retry delays pass without real waiting.
/// </summary>
internal sealed class RobotsCacheHarness : IAsyncDisposable
{
    private static readonly TimeSpan PUMP_STEP = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan PUMP_LIMIT = TimeSpan.FromSeconds(30);

    private readonly HttpFetcher _fetcher;
    private readonly CrawlWriter _writer;
    private readonly string _path;
    private bool _writerClosed;

    private RobotsCacheHarness(CrawlOptions options)
    {
        var startUri = new Uri(options.StartUrl);
        var scope = new CrawlScope(startUri, options.IncludeSubdomains, options.StayInStartFolder);
        Gates = new HostGateRegistry(scope, isInternal => new HostGate(isInternal ? options.Concurrency : 2, TimeSpan.Zero, Time));
        _fetcher = new HttpFetcher(options, UserAgents.Resolve(options.UserAgent, null), scope, Time);
        _path = Path.Combine(Path.GetTempPath(), $"si-robots-{Guid.NewGuid():N}.sicrawl");
        _writer = CrawlWriter.Open(_path, options, resume: false, new CrawlCounters(), NullLogger.Instance);
        Robots = new RobotsCache(new RetryingFetcher(_fetcher, Gates, scope, options.MaxRetries, Observer, Time, new NoJitter()), Gates,
            _writer, scope, options, UserAgents.ROBOTS_TOKEN, Observer, Time, NullLogger.Instance, CancellationToken.None);
    }

    public FakeTimeProvider Time { get; } = new();

    public RecordingObserver Observer { get; } = new();

    public HostGateRegistry Gates { get; }

    public RobotsCache Robots { get; }

    public static RobotsCacheHarness Create(Uri startUri, Func<CrawlOptions, CrawlOptions>? configure = null)
    {
        var options = new CrawlOptions { StartUrl = startUri.AbsoluteUri };
        return new RobotsCacheHarness(configure?.Invoke(options) ?? options);
    }

    /// <summary>A loopback URL nothing listens on, so connecting to it fails at once.</summary>
    public static Uri DeadSite()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return new Uri($"http://127.0.0.1:{port}/");
    }

    public async Task<RobotsTxt> GetAsync(Uri uri)
    {
        var lookup = Robots.GetAsync(uri);
        var deadline = DateTime.UtcNow + PUMP_LIMIT;
        while (!lookup.IsCompleted && DateTime.UtcNow < deadline)
        {
            Time.Advance(PUMP_STEP);
            await Task.Delay(5);
        }

        return await lookup;
    }

    /// <summary>Closes the writer, so everything recorded is committed, and reads back the robots table.</summary>
    public async Task<IReadOnlyList<RobotsRecord>> ReadRecordsAsync()
    {
        await CloseWriterAsync();
        using var reader = AnalysisReader.Open(_path);
        return reader.ReadRobots();
    }

    public async ValueTask DisposeAsync()
    {
        await CloseWriterAsync();
        _fetcher.Dispose();
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(file);
        }
    }

    private async Task CloseWriterAsync()
    {
        if (!_writerClosed)
        {
            _writerClosed = true;
            await _writer.DisposeAsync();
        }
    }
}
