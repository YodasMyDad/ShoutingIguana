using ShoutingIguana.Sidecar.Hosting;
using ShoutingIguana.Sidecar.Tests.Support;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>How the program starts and exits, hosted in-process: the exit codes the shell goes by.</summary>
public sealed class ProgramTests : IDisposable
{
    private readonly string _root = TestSidecar.NewRoot();

    public void Dispose() => TestSidecar.DeleteRoot(_root);

    /// <summary>Ctrl+C or SIGTERM while the sidecar starts: a clean exit, not a crash, and no ready line.</summary>
    [Fact]
    public async Task AShutdownAskedForWhileStartingExitsCleanly()
    {
        using StringWriter output = new(), error = new();
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        Assert.Equal(0, await SidecarProgram.RunAsync(Options(), output, error, stopping.Token));
        Assert.Equal(string.Empty, output.ToString());
        Assert.Equal(string.Empty, error.ToString());

        // Nothing is left holding the state folder.
        await using var next = await TestSidecar.StartAsync(_root);
    }

    [Fact]
    public async Task OneStateFolderServesOneSidecar()
    {
        await using (await TestSidecar.StartAsync(_root))
        {
            await Assert.ThrowsAsync<StateFolderInUseException>(() => SidecarHost.StartAsync(Options()));

            using StringWriter output = new(), error = new();
            Assert.Equal(SidecarProgram.STATE_IN_USE, await SidecarProgram.RunAsync(Options(), output, error));
            Assert.Equal(string.Empty, output.ToString());
            Assert.Contains(Path.Combine(_root, "state"), error.ToString());
        }

        // Free again once the first has gone.
        await using var after = await TestSidecar.StartAsync(_root);
    }

    private Models.SidecarOptions Options() => new()
    {
        DataDirectory = Path.Combine(_root, "data"),
        StateDirectory = Path.Combine(_root, "state"),
        Token = TestSidecar.TOKEN,
        ConsoleLogging = false,
        FileLogging = false
    };
}
