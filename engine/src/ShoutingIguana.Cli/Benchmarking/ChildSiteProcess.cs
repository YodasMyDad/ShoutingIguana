using System.Diagnostics;
using System.Globalization;
using ShoutingIguana.Cli.Commands;

namespace ShoutingIguana.Cli.Benchmarking;

/// <summary>
/// Runs the synthetic site as a separate copy of this executable, so the site's own allocations are not
/// counted against the crawler's memory.
/// </summary>
internal sealed class ChildSiteProcess : IDisposable
{
    private readonly Process _process;

    private ChildSiteProcess(Process process, Uri baseUri, Uri externalBaseUri)
    {
        _process = process;
        BaseUri = baseUri;
        ExternalBaseUri = externalBaseUri;
    }

    public Uri BaseUri { get; }

    public Uri ExternalBaseUri { get; }

    /// <param name="deadTwin">Plant <see cref="TestSite.Models.SyntheticSiteOptions.DeadTwin"/>.</param>
    public static async Task<ChildSiteProcess> StartAsync(int pages, bool deadTwin, CancellationToken cancellationToken)
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the running executable.");
        var start = new ProcessStartInfo(processPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        // Launched through "dotnet si.dll": pass the dll so the child runs the same program.
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            start.ArgumentList.Add(typeof(ChildSiteProcess).Assembly.Location);
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
                    var urls = line[TestSiteCommand.READY_PREFIX.Length..].Split(' ');
                    return new ChildSiteProcess(process, new Uri(urls[0]), new Uri(urls[1]));
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

    public void Dispose()
    {
        Stop(_process);
        _process.Dispose();
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
