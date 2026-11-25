using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// The computer's own Chrome or Edge, started as a person starts it and spoken to over its debugging port. Its window
/// is kept out of sight until a person is needed. It has its own profile folder, never the person's: the one
/// <see cref="BrowserProfile.Folder"/> names when no other crawl is using it, and otherwise one made for this crawl
/// and deleted after.
/// </summary>
internal sealed class InstalledBrowserProcess : IAsyncDisposable
{
    private const int START_MS = 30_000;
    /// <summary>The port the browser was given, kept in its profile so one left running can be asked to close.</summary>
    private const string PORT_FILE = "shoutingiguana.port";
    private const string LOCK_FILE = "shoutingiguana.lock";

    /// <summary>Where a window is put to be out of sight on Windows, which keeps drawing it there.</summary>
    private const int OFF_SCREEN = -32_000;

    private readonly Process _process;
    private readonly string _profile;
    private readonly bool _temporaryProfile;
    private readonly FileStream? _profileLock;
    private readonly ILogger _logger;

    /// <summary>The window that had the keyboard when this one was put in front of the person. Windows only.</summary>
    private nint _shownOver;

    private InstalledBrowserProcess(Process process, DevToolsConnection connection, InstalledBrowser browser, string profile,
        bool temporaryProfile, FileStream? profileLock, ILogger logger)
    {
        _process = process;
        Connection = connection;
        Browser = browser;
        _profile = profile;
        _temporaryProfile = temporaryProfile;
        _profileLock = profileLock;
        _logger = logger;
    }

    public DevToolsConnection Connection { get; }

    public InstalledBrowser Browser { get; }

    public bool IsRunning => !_process.HasExited && Connection.IsOpen;

    /// <param name="proxyServer">The proxy the crawl uses, as Chromium's own switch takes it. Null for none.</param>
    public static async Task<InstalledBrowserProcess> StartAsync(InstalledBrowser browser, string? proxyServer, ILogger logger,
        CancellationToken cancellationToken)
    {
        var (profile, temporary, profileLock) = OpenProfile(logger);
        Process? process = null;
        try
        {
            var portFile = Path.Combine(profile, PORT_FILE);
            await CloseLeftOverAsync(portFile, logger).ConfigureAwait(false);

            var start = new ProcessStartInfo(browser.Path)
            {
                UseShellExecute = false,
                // The sidecar keeps standard output for its ready line, and the browser writes to both.
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            // A port of its own choosing, named outright. Asked to pick one itself (port 0), Chrome takes it that a
            // test is driving it and tells every page so, and a site's check then never clears.
            var port = FreePort();
            await File.WriteAllTextAsync(portFile, port.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
            string[] arguments =
            [
                "--remote-debugging-port=" + port.ToString(CultureInfo.InvariantCulture),
                "--user-data-dir=" + profile,
                "--no-first-run",
                "--no-default-browser-check",
                "--window-size=1100,800",
                OperatingSystem.IsWindows() ? $"--window-position={OFF_SCREEN},{OFF_SCREEN}" : "--start-minimized"
            ];
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            if (proxyServer is not null)
            {
                start.ArgumentList.Add("--proxy-server=" + proxyServer);
                // Chromium sends loopback around a proxy unless told not to.
                start.ArgumentList.Add("--proxy-bypass-list=<-loopback>");
            }

            start.ArgumentList.Add("about:blank");
            var front = OperatingSystem.IsWindows() ? BrowserWindows.InFront() : 0;
            process = Process.Start(start) ?? throw new InvalidOperationException($"{browser.Name} didn't start.");
            process.OutputDataReceived += static (_, _) => { };
            process.ErrorDataReceived += static (_, _) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var endpoint = await EndpointAsync(port, process, browser, cancellationToken).ConfigureAwait(false);
            var connection = await DevToolsConnection.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            await KeepOutOfSightAsync(process, front, cancellationToken).ConfigureAwait(false);
            return new InstalledBrowserProcess(process, connection, browser, profile, temporary, profileLock, logger);
        }
        catch
        {
            Kill(process, logger);
            process?.Dispose();
            profileLock?.Dispose();
            if (temporary)
            {
                DeleteProfile(profile, logger);
            }

            throw;
        }
    }

    /// <summary>
    /// Puts the window that holds <paramref name="targetId"/> in front of the person, on that tab: restored, whole
    /// and in the middle of the main screen, with its taskbar button, and with the keyboard where the system allows.
    /// </summary>
    public async Task<WindowShown> ShowAsync(string targetId, CancellationToken cancellationToken)
    {
        var window = await WindowAsync(targetId, cancellationToken).ConfigureAwait(false);
        var screen = await ScreenAsync(targetId, cancellationToken).ConfigureAwait(false);
        var place = WindowPlacement.Centred(screen);

        // A window's place can only be set once it is restored, so that comes first, by itself.
        await SetBoundsAsync(window, new { windowState = "normal" }, cancellationToken).ConfigureAwait(false);
        await SetBoundsAsync(window, new { left = place.Left, top = place.Top, width = place.Width, height = place.Height }, cancellationToken)
            .ConfigureAwait(false);
        await Connection.SendAsync("Target.activateTarget", new { targetId }, cancellationToken: cancellationToken).ConfigureAwait(false);
        bool? inFront = null;
        if (OperatingSystem.IsWindows())
        {
            // Whoever has the keyboard now gets it back when the window is put away again.
            if (!BrowserWindows.IsInFront(_process.Id))
            {
                _shownOver = BrowserWindows.InFront();
            }

            inFront = BrowserWindows.BringToFront(_process.Id);
        }

        var bounds = await BoundsAsync(window, cancellationToken).ConfigureAwait(false);
        if (!WindowPlacement.IsOn(bounds, screen))
        {
            // A window that was still being restored can miss its first move.
            await SetBoundsAsync(window, new { windowState = "normal" }, cancellationToken).ConfigureAwait(false);
            await SetBoundsAsync(window, new { left = place.Left, top = place.Top, width = place.Width, height = place.Height }, cancellationToken)
                .ConfigureAwait(false);
            bounds = await BoundsAsync(window, cancellationToken).ConfigureAwait(false);
        }

        return new WindowShown(bounds, screen, inFront);
    }

    /// <summary>Puts the window out of sight again.</summary>
    public async Task HideAsync(string targetId, CancellationToken cancellationToken)
    {
        var window = await WindowAsync(targetId, cancellationToken).ConfigureAwait(false);
        if (OperatingSystem.IsWindows())
        {
            await SetBoundsAsync(window, new { windowState = "normal" }, cancellationToken).ConfigureAwait(false);
            await SetBoundsAsync(window, new { left = OFF_SCREEN, top = OFF_SCREEN }, cancellationToken).ConfigureAwait(false);
            BrowserWindows.KeepOffTaskbar(_process.Id);

            // Out of sight it must not keep the keyboard: it goes back to the window the person was in.
            BrowserWindows.GiveBack(_process.Id, _shownOver);
        }
        else
        {
            await SetBoundsAsync(window, new { windowState = "minimized" }, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Where the window that holds <paramref name="targetId"/> is now.</summary>
    public async Task<WindowBounds> BoundsAsync(string targetId, CancellationToken cancellationToken) =>
        await BoundsAsync(await WindowAsync(targetId, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

    /// <summary>Whether the browser's window is the one in front. Null where that can't be asked.</summary>
    public bool? IsInFront => OperatingSystem.IsWindows() ? BrowserWindows.IsInFront(_process.Id) : null;

    private async Task<WindowBounds> BoundsAsync(int windowId, CancellationToken cancellationToken)
    {
        var result = await Connection.SendAsync("Browser.getWindowBounds", new { windowId }, cancellationToken: cancellationToken).ConfigureAwait(false);
        var bounds = result.GetProperty("bounds");
        int Read(string name) => bounds.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;
        return new WindowBounds(Read("left"), Read("top"), Read("width"), Read("height"),
            bounds.TryGetProperty("windowState", out var state) ? state.GetString() ?? "normal" : "normal");
    }

    /// <summary>
    /// The main screen, less its taskbar or dock. Windows is asked. Elsewhere a blank tab of the browser says what
    /// screen its window is on: never the site's page, which is left alone while its check runs.
    /// </summary>
    private async Task<ScreenArea> ScreenAsync(string targetId, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            return BrowserWindows.WorkArea() ?? WindowPlacement.AssumedScreen;
        }

        try
        {
            var targets = await Connection.SendAsync("Target.getTargets", cancellationToken: cancellationToken).ConfigureAwait(false);
            var blank = targets.GetProperty("targetInfos").EnumerateArray().FirstOrDefault(target =>
                target.GetProperty("type").GetString() == "page" && target.GetProperty("targetId").GetString() != targetId &&
                target.GetProperty("url").GetString() == "about:blank");
            if (blank.ValueKind != JsonValueKind.Object)
            {
                return WindowPlacement.AssumedScreen;
            }

            var attached = await Connection.SendAsync("Target.attachToTarget", new { targetId = blank.GetProperty("targetId").GetString(), flatten = true },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var sessionId = attached.GetProperty("sessionId").GetString()!;
            try
            {
                var measured = await Connection.SendAsync("Runtime.evaluate",
                    new { expression = "[screen.availLeft || 0, screen.availTop || 0, screen.availWidth, screen.availHeight].join(',')", returnByValue = true },
                    sessionId, cancellationToken).ConfigureAwait(false);
                var parts = (measured.GetProperty("result").GetProperty("value").GetString() ?? string.Empty).Split(',');
                if (parts.Length == 4 && parts.All(part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
                {
                    var numbers = parts.Select(part => int.Parse(part, CultureInfo.InvariantCulture)).ToArray();
                    if (numbers[2] > 0 && numbers[3] > 0)
                    {
                        return new ScreenArea(numbers[0], numbers[1], numbers[2], numbers[3]);
                    }
                }
            }
            finally
            {
                await Connection.SendAsync("Target.detachFromTarget", new { sessionId }, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            _logger.LogDebug(ex, "The screen could not be measured");
        }

        return WindowPlacement.AssumedScreen;
    }

    private async Task<int> WindowAsync(string targetId, CancellationToken cancellationToken)
    {
        var window = await Connection.SendAsync("Browser.getWindowForTarget", new { targetId }, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return window.GetProperty("windowId").GetInt32();
    }

    private Task SetBoundsAsync(int windowId, object bounds, CancellationToken cancellationToken) =>
        Connection.SendAsync("Browser.setWindowBounds", new { windowId, bounds }, cancellationToken: cancellationToken);

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (IsRunning)
            {
                using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await Connection.SendAsync("Browser.close", cancellationToken: patience.Token).ConfigureAwait(false);
                await _process.WaitForExitAsync(patience.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogDebug(ex, "Asking {Browser} to close failed", Browser.Name);
        }

        await Connection.DisposeAsync().ConfigureAwait(false);
        Kill(_process, _logger);
        _process.Dispose();
        _profileLock?.Dispose();
        if (_temporaryProfile)
        {
            DeleteProfile(_profile, _logger);
        }
    }

    /// <summary>
    /// On Windows the window is drawn far off the screen, and its taskbar button is taken away: pressed, it would show
    /// nothing. The window is given a moment to exist.
    /// </summary>
    /// <param name="front">The window that had the keyboard before the browser started, which takes it.</param>
    private static async Task KeepOutOfSightAsync(Process process, nint front, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        for (var attempt = 0; attempt < 30 && !process.HasExited && !BrowserWindows.KeepOffTaskbar(process.Id); attempt++)
        {
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        BrowserWindows.GiveBack(process.Id, front);
    }

    /// <summary>
    /// Opens a tab for a page. A browser that opens one puts its window in front, though it is out of sight, and what
    /// the person typed next would go to a page they can't see: the keyboard is given back to the window that had it.
    /// </summary>
    /// <returns>The tab's target id.</returns>
    public async Task<string> OpenTabAsync(CancellationToken cancellationToken)
    {
        var front = OperatingSystem.IsWindows() ? BrowserWindows.InFront() : 0;
        var created = await Connection.SendAsync("Target.createTarget", new { url = "about:blank" }, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (OperatingSystem.IsWindows())
        {
            BrowserWindows.GiveBack(_process.Id, front);
        }

        return created.GetProperty("targetId").GetString()!;
    }

    /// <summary>Gives the keyboard to <paramref name="front"/> if the browser has taken it while out of sight. Windows only.</summary>
    public void KeepKeyboardWith(nint front)
    {
        if (OperatingSystem.IsWindows())
        {
            BrowserWindows.GiveBack(_process.Id, front);
        }
    }

    /// <summary>The window that has the keyboard now. Zero where that can't be asked.</summary>
    public static nint WindowInFront() => OperatingSystem.IsWindows() ? BrowserWindows.InFront() : 0;

    /// <summary>
    /// The kept profile, when no other crawl has it open: two browsers can't share one. Otherwise a folder for this
    /// crawl alone.
    /// </summary>
    private static (string Profile, bool Temporary, FileStream? Lock) OpenProfile(ILogger logger)
    {
        if (BrowserProfile.Folder is { Length: > 0 } kept)
        {
            try
            {
                Directory.CreateDirectory(kept);
                var held = new FileStream(Path.Combine(kept, LOCK_FILE), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return (kept, false, held);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "The browser profile {Folder} is in use; this crawl gets its own", kept);
            }
        }

        var own = Path.Combine(Path.GetTempPath(), "shoutingiguana-browser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(own);
        return (own, true, null);
    }

    /// <summary>
    /// A browser a crawl that ended badly left running would keep the profile, and a second one started on it would
    /// only hand over to the first and stop. It is asked to close first.
    /// </summary>
    private static async Task CloseLeftOverAsync(string portFile, ILogger logger)
    {
        if (!File.Exists(portFile))
        {
            return;
        }

        try
        {
            using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var text = await File.ReadAllTextAsync(portFile, patience.Token).ConfigureAwait(false);
            if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var port) &&
                await BrowserSocketAsync(port, patience.Token).ConfigureAwait(false) is { } endpoint)
            {
                await using var connection = await DevToolsConnection.ConnectAsync(endpoint, patience.Token).ConfigureAwait(false);
                await connection.SendAsync("Browser.close", cancellationToken: patience.Token).ConfigureAwait(false);
                await Task.Delay(1_000, patience.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogDebug(ex, "No browser was left running on the profile");
        }
    }

    /// <summary>The browser's own debugging socket, once it is listening.</summary>
    private static async Task<Uri> EndpointAsync(int port, Process process, InstalledBrowser browser, CancellationToken cancellationToken)
    {
        var waited = Stopwatch.StartNew();
        while (waited.ElapsedMilliseconds < START_MS)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                throw new InvalidOperationException($"{browser.Name} closed as soon as it started.");
            }

            if (await BrowserSocketAsync(port, cancellationToken).ConfigureAwait(false) is { } endpoint)
            {
                return endpoint;
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException($"{browser.Name} didn't start in {START_MS / 1000} seconds.");
    }

    /// <summary>What the browser on <paramref name="port"/> says its debugging socket is. Null when nothing answers there.</summary>
    private static async Task<Uri?> BrowserSocketAsync(int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var version = await client.GetStringAsync($"http://127.0.0.1:{port}/json/version", cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(version);
            return document.RootElement.TryGetProperty("webSocketDebuggerUrl", out var socket) && socket.GetString() is { } address
                ? new Uri(address)
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or UriFormatException or OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void Kill(Process? process, ILogger logger)
    {
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3_000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            logger.LogDebug(ex, "Stopping the browser failed");
        }
    }

    private static void DeleteProfile(string profile, ILogger logger)
    {
        // The browser's last processes let go of their files a moment after it closes.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(profile))
                {
                    Directory.Delete(profile, recursive: true);
                }

                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Deleting the browser profile {Folder} failed", profile);
                Thread.Sleep(300);
            }
        }
    }

    /// <summary>A header list as the debugging protocol gives it, an object of names to values, with the names in lower case.</summary>
    public static List<ResponseHeader> Headers(JsonElement headers)
    {
        List<ResponseHeader> list = [];
        if (headers.ValueKind != JsonValueKind.Object)
        {
            return list;
        }

        foreach (var header in headers.EnumerateObject())
        {
            // Repeated headers come joined by a line break.
            foreach (var value in (header.Value.GetString() ?? string.Empty).Split('\n'))
            {
                list.Add(new ResponseHeader(header.Name.ToLowerInvariant(), value));
            }
        }

        return list;
    }
}
