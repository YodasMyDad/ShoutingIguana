using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Sidecar.Hosting;
using ShoutingIguana.Sidecar.Tests.Support;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>
/// The shell review's case (L3): an exception nothing caught went only to stderr, so a crash left no cause in the
/// sidecar's own log, and the shell kept just the last stack line.
/// </summary>
public sealed class UnhandledExceptionLogTests : IDisposable
{
    private readonly string _root = TestSidecar.NewRoot();

    public void Dispose() => TestSidecar.DeleteRoot(_root);

    [Fact]
    public async Task AnExceptionNothingCaughtGoesToTheFileLogWithItsStack()
    {
        await using (var sidecar = await TestSidecar.StartAsync(_root, options => options with { FileLogging = true }))
        {
            var log = new UnhandledExceptionLog(sidecar.Host.Services.GetRequiredService<ILogger<UnhandledExceptionLog>>());
            log.Log(null, new UnhandledExceptionEventArgs(Thrown(), isTerminating: true));
        }

        var text = string.Join('\n', Directory.GetFiles(Path.Combine(_root, "state", "logs"), "sidecar-*.log").Select(ReadShared));
        Assert.Contains("[FTL]", text);
        Assert.Contains("The sidecar stopped on an exception nothing caught", text);
        Assert.Contains("System.InvalidOperationException: The crawl store is corrupt.", text);
        Assert.Contains(nameof(Thrown), text);
    }

    private static InvalidOperationException Thrown()
    {
        try
        {
            throw new InvalidOperationException("The crawl store is corrupt.");
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }
    }

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
