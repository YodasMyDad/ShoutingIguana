namespace ShoutingIguana.Sidecar.Tests;

/// <summary>The command line and environment the Tauri shell starts the sidecar with.</summary>
public sealed class SidecarArgumentsTests
{
    private static readonly string DATA = Path.Combine(Path.GetTempPath(), "si-data");
    private static readonly string STATE = Path.Combine(Path.GetTempPath(), "si-state");

    [Fact]
    public void ReadsTheFoldersPortAndEnvironment()
    {
        var environment = new Dictionary<string, string>
        {
            [SidecarArguments.TOKEN_VARIABLE] = "s3cret-token",
            [SidecarArguments.ORIGINS_VARIABLE] = " http://tauri.localhost , tauri://localhost/,http://localhost:5173",
            [SidecarArguments.PARENT_VARIABLE] = "4242"
        };

        Assert.True(SidecarArguments.TryParse(["--data-dir", DATA, $"--state-dir={STATE}", "--port", "53123", "--stdin-lifeline"],
            environment.GetValueOrDefault, out var options, out var error), error);

        Assert.Equal(DATA, options.DataDirectory);
        Assert.Equal(STATE, options.StateDirectory);
        Assert.Equal(53123, options.Port);
        Assert.Equal("s3cret-token", options.Token);
        Assert.Equal(["http://tauri.localhost", "tauri://localhost", "http://localhost:5173"], options.AllowedOrigins);
        Assert.Equal(4242, options.ParentProcessId);
        Assert.True(options.StdinLifeline);
    }

    /// <summary>Read once, then gone from the environment, so no process the sidecar starts inherits it.</summary>
    [Fact]
    public void TheTokenLeavesTheEnvironmentOnceRead()
    {
        Environment.SetEnvironmentVariable(SidecarArguments.TOKEN_VARIABLE, "token-from-the-shell");
        try
        {
            Assert.True(SidecarProgram.TryReadOptions(["--data-dir", DATA, "--state-dir", STATE], out var options, out var error), error);

            Assert.Equal("token-from-the-shell", options.Token);
            Assert.Null(Environment.GetEnvironmentVariable(SidecarArguments.TOKEN_VARIABLE));
        }
        finally
        {
            Environment.SetEnvironmentVariable(SidecarArguments.TOKEN_VARIABLE, null);
        }
    }

    [Fact]
    public void DefaultsToAFreePortAMadeUpTokenAndTheUsersFolders()
    {
        Assert.True(SidecarArguments.TryParse([], _ => null, out var options, out var error), error);

        Assert.Equal(0, options.Port);
        Assert.Null(options.Token);
        Assert.Empty(options.AllowedOrigins);
        Assert.Null(options.ParentProcessId);
        Assert.False(options.StdinLifeline);
        Assert.EndsWith("Shouting Iguana", options.DataDirectory);
        Assert.EndsWith("ShoutingIguana", options.StateDirectory);
        Assert.True(Path.IsPathFullyQualified(options.DataDirectory) && Path.IsPathFullyQualified(options.StateDirectory));
    }

    [Theory]
    [InlineData("--verbose", "", "Unknown option")]
    [InlineData("--data-dir", "", "needs a value")]
    [InlineData("--port", "65536", "--port")]
    [InlineData("--port", "-1", "--port")]
    [InlineData("--port", "http", "--port")]
    public void RefusesArgumentsItCantUse(string name, string value, string message)
    {
        string[] args = value.Length == 0 ? [name] : [name, value];

        Assert.False(SidecarArguments.TryParse(args, _ => null, out _, out var error));
        Assert.Contains(message, error);
    }

    [Theory]
    [InlineData(SidecarArguments.PARENT_VARIABLE, "shell")]
    [InlineData(SidecarArguments.PARENT_VARIABLE, "0")]
    [InlineData(SidecarArguments.ORIGINS_VARIABLE, "tauri.localhost")]
    [InlineData(SidecarArguments.ORIGINS_VARIABLE, "http://tauri.localhost/app")]
    [InlineData(SidecarArguments.ORIGINS_VARIABLE, "http://user@tauri.localhost")]
    [InlineData(SidecarArguments.TOKEN_VARIABLE, "two words")]
    public void RefusesEnvironmentItCantUse(string variable, string value)
    {
        Assert.False(SidecarArguments.TryParse([], name => name == variable ? value : null, out _, out var error));
        Assert.Contains(variable, error);
    }
}
