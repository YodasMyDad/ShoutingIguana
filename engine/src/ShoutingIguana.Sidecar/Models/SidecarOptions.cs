namespace ShoutingIguana.Sidecar.Models;

/// <summary>How a sidecar runs: from its command line and environment, or set by tests that host it in-process.</summary>
internal sealed record SidecarOptions
{
    /// <summary>
    /// The longest a shutdown waits. Stopping a crawl cancels its requests at once, so what it waits for is each crawl
    /// closing its file (a second or two) and requests still in flight; past this, the sidecar exits anyway, and a crawl
    /// that hadn't closed is left interrupted, still resumable.
    /// </summary>
    public static readonly TimeSpan DEFAULT_SHUTDOWN_TIMEOUT = TimeSpan.FromSeconds(30);

    /// <summary>Where new crawls are saved, and whose top level the library lists.</summary>
    public required string DataDirectory { get; init; }

    /// <summary>Holds <c>library.json</c>, the user's defaults and the logs. One sidecar at a time uses it.</summary>
    public required string StateDirectory { get; init; }

    /// <summary>The bearer token every request must carry. Null makes one up, which the ready line then gives: development only.</summary>
    public string? Token { get; init; }

    /// <summary>Development: a sidecar started without a token makes one up. Never set by the shell.</summary>
    public bool Development { get; init; }

    /// <summary>Origins (the app's webviews) allowed to call the API from a browser.</summary>
    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];

    /// <summary>The shell's process: when it ends, the sidecar shuts down.</summary>
    public int? ParentProcessId { get; init; }

    /// <summary>
    /// Shut down when stdin says <c>shutdown</c> or reaches its end: the shell holds the other end, so it closes when the
    /// shell ends however it ends. Opt-in, since a sidecar started any other way may have a closed or empty stdin.
    /// </summary>
    public bool StdinLifeline { get; init; }

    /// <summary>The port to listen on, on 127.0.0.1. 0 picks a free one.</summary>
    public int Port { get; init; }

    public bool ConsoleLogging { get; init; } = true;

    public bool FileLogging { get; init; } = true;

    /// <summary>How often an idle event stream gets a comment, so nothing between it and the UI times it out.</summary>
    public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>An event stream this far behind is disconnected; the UI reconnects and is up to date at once.</summary>
    public int MaxQueuedEvents { get; init; } = 1_000;

    /// <summary>How long a shutdown waits for crawls to stop cleanly.</summary>
    public TimeSpan ShutdownTimeout { get; init; } = DEFAULT_SHUTDOWN_TIMEOUT;

    /// <summary>
    /// The longest the library spends reading one file's summary. A legitimate crawl of millions of URLs takes well
    /// under a second; a file crafted to be slow is listed as unreadable instead of holding up the list.
    /// </summary>
    public TimeSpan SummaryTimeLimit { get; init; } = TimeSpan.FromSeconds(10);
}
