using System.Diagnostics;
using System.Globalization;
using System.Net;
using ShoutingIguana.Cli.Commands;
using ShoutingIguana.TestSite;

namespace ShoutingIguana.Cli.Benchmarking;

/// <summary>
/// Runs the synthetic site as a separate copy of this executable, so the site's own allocations are not
/// counted against the crawler's memory.
/// </summary>
internal sealed class ChildSiteProcess : IDisposable
{
    private readonly Process _process;
    private readonly string _controlToken;

    private ChildSiteProcess(Process process, Uri baseUri, Uri externalBaseUri, string controlToken)
    {
        _process = process;
        BaseUri = baseUri;
        ExternalBaseUri = externalBaseUri;
        _controlToken = controlToken;
    }

    public Uri BaseUri { get; }

    public Uri ExternalBaseUri { get; }

    /// <param name="deadTwin">Plant <see cref="TestSite.Models.SyntheticSiteOptions.DeadTwin"/>.</param>
    public static async Task<ChildSiteProcess> StartAsync(int pages, bool deadTwin, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Program(out var dll))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        if (dll is not null)
        {
            start.ArgumentList.Add(dll);
        }

        start.ArgumentList.Add("testsite");
        start.ArgumentList.Add("--pages");
        start.ArgumentList.Add(pages.ToString(CultureInfo.InvariantCulture));
        if (deadTwin)
        {
            start.ArgumentList.Add("--dead-twin");
        }

        var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the synthetic site.");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
                if (line is null)
                {
                    var error = await process.StandardError.ReadToEndAsync(cancellationToken);
                    throw new InvalidOperationException($"The synthetic site exited before it was ready. {error}");
                }

                if (line.StartsWith(TestSiteCommand.READY_PREFIX, StringComparison.Ordinal))
                {
                    var fields = line[TestSiteCommand.READY_PREFIX.Length..].Split(' ');
                    return new ChildSiteProcess(process, new Uri(fields[0]), new Uri(fields[1]), fields[2]);
                }
            }
        }
        catch
        {
            Stop(process);
            process.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Switches the site to another revision, as its owner would publish fixes between two crawls, through its control
    /// route with the token its ready line gave (<see cref="SyntheticSiteServer.REVISION_ROUTE"/>). The URLs and ports stay.
    /// </summary>
    public async Task SetRevisionAsync(int revision, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUri, $"{SyntheticSiteServer.REVISION_ROUTE}{revision.ToString(CultureInfo.InvariantCulture)}"));
        request.Headers.Add(SyntheticSiteServer.CONTROL_TOKEN_HEADER, _controlToken);
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NoContent)
        {
            throw new InvalidOperationException($"The synthetic site wouldn't switch to revision {revision}: {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
    }

    public void Dispose()
    {
        Stop(_process);
        _process.Dispose();
    }

    /// <summary>
    /// The program that runs this one again, and the dll to pass it when it's the .NET host: this executable, run as
    /// <c>si</c> or <c>dotnet si.dll</c>; or, from another host (a test runner), the <c>si</c> beside this assembly.
    /// </summary>
    private static string Program(out string? dll)
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the running executable.");
        var assembly = typeof(ChildSiteProcess).Assembly.Location;
        var name = Path.GetFileNameWithoutExtension(processPath);
        dll = null;
        if (name.Equals(Path.GetFileNameWithoutExtension(assembly), StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        var apphost = Path.Combine(Path.GetDirectoryName(assembly)!, Path.GetFileNameWithoutExtension(assembly) + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
        if (!name.Equals("dotnet", StringComparison.OrdinalIgnoreCase) && File.Exists(apphost))
        {
            return apphost;
        }

        // Launched through "dotnet si.dll", or with no apphost beside it: pass the dll so the child runs the same program.
        dll = assembly;
        return name.Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? processPath : "dotnet";
    }

    private static void Stop(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(TimeSpan.FromSeconds(10));
        }
    }
}
