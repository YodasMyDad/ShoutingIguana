using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using ShoutingIguana.Engine;
using ShoutingIguana.Sidecar.Tests.Support;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>The real si-sidecar process, as the Tauri shell starts it: the ready line, the token, stdout and the exit code.</summary>
public sealed class ExecutableTests : IDisposable
{
    private static readonly TimeSpan TIMEOUT = TimeSpan.FromSeconds(60);

    /// <summary>As the shell makes them, for each launch: 32 random bytes as hex.</summary>
    private const string SHELL_TOKEN = "8c1f4a2e9b7d6c5a3f2e1d0c9b8a7f6e5d4c3b2a1f0e9d8c7b6a5f4e3d2c1b0a";

    private readonly string _root = TestSidecar.NewRoot();
    private readonly List<Process> _processes = [];

    public void Dispose()
    {
        foreach (var process in _processes)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }

            process.Dispose();
        }

        TestSidecar.DeleteRoot(_root);
    }

    [Fact]
    public async Task WritesOneReadyLineServesWithTheTokenItMadeUpAndExitsCleanly()
    {
        var (process, ready) = await StartAsync("one", ["--dev"], token: null);

        Assert.Equal("ready", (string?)ready["event"]);
        Assert.Equal(EngineInfo.Version, (string?)ready["version"]);
        var port = (int)ready["port"]!;
        var token = (string)ready["token"]!;
        Assert.Matches("^[0-9a-f]{64}$", token);

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("api/health")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var health = JsonNode.Parse(await client.GetStringAsync("api/health"))!;
        Assert.Equal(process.Id, (int?)health["pid"]);
        Assert.Equal(Path.Combine(_root, "one", "data"), (string?)health["dataDir"]);

        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("api/shutdown", null)).StatusCode);
        Assert.Equal(0, await ExitCodeAsync(process));

        // Nothing but the ready line ever goes to stdout; the log went to stderr and the state folder.
        Assert.Equal(string.Empty, await process.StandardOutput.ReadToEndAsync());
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(_root, "one", "state", "logs"), "sidecar-*.log"));
    }

    [Fact]
    public async Task ShutsDownWhenItsParentEnds()
    {
        var (parent, _) = await StartAsync("parent");
        var (child, ready) = await StartAsync("child", parentProcessId: parent.Id);
        Assert.False(child.HasExited);
        Assert.Null(ready["token"]);

        parent.Kill(entireProcessTree: true);

        Assert.Equal(0, await ExitCodeAsync(child));
    }

    [Fact]
    public async Task WithTheLifelineAShutdownLineOnStdinShutsItDown()
    {
        var (process, ready) = await StartAsync("line", ["--stdin-lifeline"]);
        (await AssertServesAsync(ready)).Dispose();

        await process.StandardInput.WriteLineAsync("shutdown");
        await process.StandardInput.FlushAsync();

        Assert.Equal(0, await ExitCodeAsync(process));
    }

    /// <summary>What happens when the shell dies, however it dies: its end of the pipe closes.</summary>
    [Fact]
    public async Task WithTheLifelineTheEndOfStdinShutsItDown()
    {
        var (process, ready) = await StartAsync("end", ["--stdin-lifeline"]);
        (await AssertServesAsync(ready)).Dispose();

        process.StandardInput.Close();

        Assert.Equal(0, await ExitCodeAsync(process));
    }

    /// <summary>The lifeline is opt-in: a sidecar started any other way may have no stdin at all.</summary>
    [Fact]
    public async Task WithoutTheLifelineTheEndOfStdinChangesNothing()
    {
        var (process, ready) = await StartAsync("no-lifeline");

        process.StandardInput.Close();
        await Task.Delay(TimeSpan.FromSeconds(1));

        Assert.False(process.HasExited);
        using var client = await AssertServesAsync(ready);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("api/shutdown", null)).StatusCode);
        Assert.Equal(0, await ExitCodeAsync(process));
    }

    [Fact]
    public async Task RefusesArgumentsItCantUseWithExitCode2()
    {
        var process = Process.Start(StartInfo("bad", ["--verbose"]))!;
        _processes.Add(process);

        Assert.Equal(2, await ExitCodeAsync(process));
        Assert.Equal(string.Empty, await process.StandardOutput.ReadToEndAsync());
        Assert.Contains("Unknown option '--verbose'", await process.StandardError.ReadToEndAsync());
    }

    /// <summary>Only a sidecar started for development makes its own token; the shell's must be long enough not to guess.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("0123456789abcdef")]
    public async Task RefusesToStartWithoutAGoodTokenUnlessForDevelopment(string? token)
    {
        var process = Process.Start(StartInfo("no-token", [], token))!;
        _processes.Add(process);

        Assert.Equal(2, await ExitCodeAsync(process));
        Assert.Equal(string.Empty, await process.StandardOutput.ReadToEndAsync());
        Assert.Contains("SI_SIDECAR_TOKEN", await process.StandardError.ReadToEndAsync());
    }

    /// <summary>
    /// Two sidecars on one state folder would write the same library and defaults. The second exits with its own code and
    /// says why on stderr; the first carries on.
    /// </summary>
    [Fact]
    public async Task ASecondSidecarOnTheSameStateFolderExitsWithCode3()
    {
        var (_, ready) = await StartAsync("shared");

        var second = Process.Start(StartInfo("shared", []))!;
        _processes.Add(second);

        Assert.Equal(3, await ExitCodeAsync(second));
        Assert.Equal(string.Empty, await second.StandardOutput.ReadToEndAsync());
        Assert.Contains("already using", await second.StandardError.ReadToEndAsync());
        (await AssertServesAsync(ready)).Dispose();
    }

    /// <summary>
    /// ASP.NET Core reads ASPNETCORE_* before the sidecar sets anything. A developer's environment (or anything that can
    /// set one) must still not move the listener off 127.0.0.1, and the ready line names the port it really uses.
    /// </summary>
    [Fact]
    public async Task TheEnvironmentCantMoveTheListenerOffTheLoopbackAddress()
    {
        var elsewhere = FreePort();
        var (_, ready) = await StartAsync("environment", environment: new Dictionary<string, string>
        {
            ["ASPNETCORE_URLS"] = $"http://127.0.0.2:{elsewhere}",
            ["ASPNETCORE_PREFERHOSTINGURLS"] = "true",
            ["ASPNETCORE_HTTP_PORTS"] = elsewhere.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });

        Assert.True((int)ready["port"]! > 0);
        (await AssertServesAsync(ready)).Dispose();
        using var other = new System.Net.Sockets.TcpClient();
        await Assert.ThrowsAnyAsync<System.Net.Sockets.SocketException>(() => other.ConnectAsync("127.0.0.2", elsewhere).WaitAsync(TIMEOUT));
    }

    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>A client for the sidecar that wrote this ready line, once it has answered a health check.</summary>
    private static async Task<HttpClient> AssertServesAsync(JsonNode ready)
    {
        var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{(int)ready["port"]!}/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (string?)ready["token"] ?? SHELL_TOKEN);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("api/health")).StatusCode);
        return client;
    }

    /// <param name="token">What the shell sets in SI_SIDECAR_TOKEN; null sets nothing, as only development does.</param>
    private async Task<(Process Process, JsonNode Ready)> StartAsync(string name, string[]? extra = null, int? parentProcessId = null,
        string? token = SHELL_TOKEN, IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = StartInfo(name, extra ?? [], token);
        if (parentProcessId is { } parent)
        {
            start.Environment["SI_PARENT_PID"] = parent.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        foreach (var (variable, value) in environment ?? new Dictionary<string, string>())
        {
            start.Environment[variable] = value;
        }

        var process = Process.Start(start)!;
        _processes.Add(process);

        // Drained, so a full stderr pipe can't block the sidecar.
        _ = process.StandardError.ReadToEndAsync();
        var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TIMEOUT);
        Assert.False(line is null, "The sidecar exited without a ready line.");
        return (process, JsonNode.Parse(line)!);
    }

    private ProcessStartInfo StartInfo(string name, string[] extra, string? token = SHELL_TOKEN)
    {
        var start = new ProcessStartInfo(DotnetHost())
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "si-sidecar.dll"));
        foreach (var argument in new[] { "--data-dir", Path.Combine(_root, name, "data"), "--state-dir", Path.Combine(_root, name, "state") }.Concat(extra))
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment.Remove("SI_SIDECAR_TOKEN");
        start.Environment.Remove("SI_PARENT_PID");
        if (token is not null)
        {
            start.Environment["SI_SIDECAR_TOKEN"] = token;
        }

        start.Environment["SI_SIDECAR_ORIGINS"] = TestSidecar.ORIGIN;
        return start;
    }

    private static async Task<int> ExitCodeAsync(Process process)
    {
        await process.WaitForExitAsync().WaitAsync(TIMEOUT);
        return process.ExitCode;
    }

    /// <summary>The dotnet the tests run on.</summary>
    private static string DotnetHost() =>
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host
            ? host
            : Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet" ? Environment.ProcessPath! : "dotnet";
}
