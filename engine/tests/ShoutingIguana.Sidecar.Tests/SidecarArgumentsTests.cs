namespace ShoutingIguana.Sidecar.Tests;

/// <summary>The command line and environment the Tauri shell starts the sidecar with.</summary>
public sealed class SidecarArgumentsTests
{
    private static readonly string DATA = Path.Combine(Path.GetTempPath(), "si-data");
    private static readonly string STATE = Path.Combine(Path.GetTempPath(), "si-state");

    /// <summary>As the shell makes them: 32 random bytes as hex.</summary>
    private const string SHELL_TOKEN = "0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0";

    [Fact]
    public void ReadsTheFoldersPortAndEnvironment()
    {
        var environment = new Dictionary<string, string>
        {
            [SidecarArguments.TOKEN_VARIABLE] = SHELL_TOKEN,
            [SidecarArguments.ORIGINS_VARIABLE] = " http://tauri.localhost , tauri://localhost/,http://localhost:5173",
            [SidecarArguments.PARENT_VARIABLE] = "4242"
        };

        Assert.True(SidecarArguments.TryParse(["--data-dir", DATA, $"--state-dir={STATE}", "--port", "53123", "--stdin-lifeline"],
            environment.GetValueOrDefault, out var options, out var error), error);

        Assert.Equal(DATA, options.DataDirectory);
        Assert.Equal(STATE, options.StateDirectory);
        Assert.Equal(53123, options.Port);
        Assert.Equal(SHELL_TOKEN, options.Token);
        Assert.Equal(["http://tauri.localhost", "tauri://localhost", "http://localhost:5173"], options.AllowedOrigins);
        Assert.Equal(4242, options.ParentProcessId);
        Assert.True(options.StdinLifeline);
    }

    /// <summary>Read once, then gone from the environment, so no process the sidecar starts inherits it.</summary>
    [Fact]
    public void TheTokenLeavesTheEnvironmentOnceRead()
    {
        Environment.SetEnvironmentVariable(SidecarArguments.TOKEN_VARIABLE, SHELL_TOKEN);
        try
        {
            Assert.True(SidecarProgram.TryReadOptions(["--data-dir", DATA, "--state-dir", STATE], out var options, out var error), error);

            Assert.Equal(SHELL_TOKEN, options.Token);
            Assert.Null(Environment.GetEnvironmentVariable(SidecarArguments.TOKEN_VARIABLE));
        }
        finally
        {
            Environment.SetEnvironmentVariable(SidecarArguments.TOKEN_VARIABLE, null);
        }
    }

    [Fact]
    /// <summary>For development only: the shell always gives a token.</summary>
    public void DefaultsToAFreePortAMadeUpTokenAndTheUsersFolders()
    {
        Assert.True(SidecarArguments.TryParse(["--dev"], _ => null, out var options, out var error), error);

        Assert.Equal(0, options.Port);
        Assert.Null(options.Token);
        Assert.True(options.Development);
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
    [InlineData(SidecarArguments.TOKEN_VARIABLE, "two words two words two words two words")]
    [InlineData(SidecarArguments.TOKEN_VARIABLE, "0123456789abcdef0123456789abcde")]
    public void RefusesEnvironmentItCantUse(string variable, string value)
    {
        Assert.False(SidecarArguments.TryParse([], name => name == variable ? value : null, out _, out var error));
        Assert.Contains(variable, error);
    }

    /// <summary>A sidecar makes its own token only when started for development: the shell always gives one.</summary>
    [Fact]
    public void RefusesToGoWithoutATokenUnlessForDevelopment()
    {
        Assert.False(SidecarArguments.TryParse(["--data-dir", DATA, "--state-dir", STATE], _ => null, out _, out var error));
        Assert.Contains(SidecarArguments.TOKEN_VARIABLE, error);
        Assert.Contains("--dev", error);

        Assert.True(SidecarArguments.TryParse(["--data-dir", DATA, "--state-dir", STATE, "--dev"], _ => null, out var development, out error), error);
        Assert.True(development.Development);
        Assert.Null(development.Token);

        // A token given wins, development or not.
        Assert.True(SidecarArguments.TryParse(["--dev"], name => name == SidecarArguments.TOKEN_VARIABLE ? SHELL_TOKEN : null, out var given, out error), error);
        Assert.Equal(SHELL_TOKEN, given.Token);
    }
}
