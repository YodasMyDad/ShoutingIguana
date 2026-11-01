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
        var (process, ready) = await StartAsync("one");

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

    /// <summary>A client for the sidecar that wrote this ready line, once it has answered a health check.</summary>
    private static async Task<HttpClient> AssertServesAsync(JsonNode ready)
    {
        var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{(int)ready["port"]!}/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (string)ready["token"]!);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("api/health")).StatusCode);
        return client;
    }

    private async Task<(Process Process, JsonNode Ready)> StartAsync(string name, string[]? extra = null, int? parentProcessId = null)
    {
        var start = StartInfo(name, extra ?? []);
        if (parentProcessId is { } parent)
        {
            start.Environment["SI_PARENT_PID"] = parent.ToString(System.Globalization.CultureInfo.InvariantCulture);
            start.Environment["SI_SIDECAR_TOKEN"] = "a-token-from-the-shell";
        }

        var process = Process.Start(start)!;
        _processes.Add(process);

        // Drained, so a full stderr pipe can't block the sidecar.
        _ = process.StandardError.ReadToEndAsync();
        var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TIMEOUT);
        Assert.False(line is null, "The sidecar exited without a ready line.");
        return (process, JsonNode.Parse(line)!);
    }

    private ProcessStartInfo StartInfo(string name, string[] extra)
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
