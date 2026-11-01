namespace ShoutingIguana.Sidecar.Models;

/// <summary>How a sidecar runs: from its command line and environment, or set by tests that host it in-process.</summary>
internal sealed record SidecarOptions
{
    /// <summary>The longest a crawl's in-flight request can take (the engine's maximum timeout), plus time to close its file.</summary>
    public static readonly TimeSpan DEFAULT_SHUTDOWN_TIMEOUT = TimeSpan.FromSeconds(300 + 30);

    /// <summary>Where new crawls are saved, and whose top level the library lists.</summary>
    public required string DataDirectory { get; init; }

    /// <summary>Holds <c>library.json</c>, the user's defaults and the logs.</summary>
    public required string StateDirectory { get; init; }

    /// <summary>The bearer token every request must carry. Null makes one up, which the ready line then gives.</summary>
    public string? Token { get; init; }

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
}
