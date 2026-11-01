using System.Globalization;
using ShoutingIguana.Sidecar.Models;

namespace ShoutingIguana.Sidecar;

/// <summary>Reads the sidecar's command line and environment, as the Tauri shell sets them.</summary>
internal static class SidecarArguments
{
    public const string TOKEN_VARIABLE = "SI_SIDECAR_TOKEN";
    public const string ORIGINS_VARIABLE = "SI_SIDECAR_ORIGINS";
    public const string PARENT_VARIABLE = "SI_PARENT_PID";

    public const string USAGE = """
        si-sidecar: the Shouting Iguana engine as a localhost API for the desktop app.

        Options:
          --data-dir <folder>   Where new crawls are saved (default: Documents/Shouting Iguana).
          --state-dir <folder>  The library, the user's defaults and the logs (default: <local app data>/ShoutingIguana).
          --port <number>       Port on 127.0.0.1 (default 0: a free port, given in the ready line).
          --stdin-lifeline      Shut down on a "shutdown" line on stdin, or when stdin ends (the shell has gone).

        Environment:
          SI_SIDECAR_TOKEN      Bearer token for every request, removed from the environment once read. Without it,
                                one is made up and given in the ready line.
          SI_SIDECAR_ORIGINS    Comma-separated origins allowed to call the API from a browser.
          SI_PARENT_PID         The shell's process id: when it ends, the sidecar shuts down.
        """;

    private const string STDIN_LIFELINE = "--stdin-lifeline";

    public static bool IsHelp(IReadOnlyList<string> args) => args.Any(arg => arg is "--help" or "-h" or "-?");

    /// <param name="error">One line for stderr when the arguments can't be used.</param>
    public static bool TryParse(IReadOnlyList<string> args, Func<string, string?> environment, out SidecarOptions options, out string error)
    {
        options = null!;
        error = string.Empty;
        string? dataDirectory = null, stateDirectory = null;
        var port = 0;
        var stdinLifeline = false;

        for (var index = 0; index < args.Count; index++)
        {
            if (args[index] == STDIN_LIFELINE)
            {
                stdinLifeline = true;
                continue;
            }

            var (name, value) = Split(args[index]);
            if (name is not ("--data-dir" or "--state-dir" or "--port"))
            {
                error = $"Unknown option '{args[index]}'. Use --data-dir, --state-dir, --port or {STDIN_LIFELINE} (--help lists them).";
                return false;
            }

            if (value is null)
            {
                if (index + 1 >= args.Count)
                {
                    error = $"{name} needs a value.";
                    return false;
                }

                value = args[++index];
            }

            switch (name)
            {
                case "--data-dir":
                    dataDirectory = value;
                    break;
                case "--state-dir":
                    stateDirectory = value;
                    break;
                default:
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port > 65535)
                    {
                        error = $"--port must be a number from 0 to 65535, not '{value}'.";
                        return false;
                    }

                    break;
            }
        }

        if (!TryReadParent(environment(PARENT_VARIABLE), out var parent, out error) ||
            !TryReadOrigins(environment(ORIGINS_VARIABLE), out var origins, out error) ||
            !TryReadToken(environment(TOKEN_VARIABLE), out var token, out error) ||
            !TryFullPath(dataDirectory ?? DefaultDataDirectory(), "--data-dir", out var data, out error) ||
            !TryFullPath(stateDirectory ?? DefaultStateDirectory(), "--state-dir", out var state, out error))
        {
            return false;
        }

        options = new SidecarOptions
        {
            DataDirectory = data,
            StateDirectory = state,
            Token = token,
            AllowedOrigins = origins,
            ParentProcessId = parent,
            Port = port,
            StdinLifeline = stdinLifeline
        };
        return true;
    }

    private static (string Name, string? Value) Split(string arg)
    {
        var equals = arg.IndexOf('=');
        return arg.StartsWith("--", StringComparison.Ordinal) && equals > 0 ? (arg[..equals], arg[(equals + 1)..]) : (arg, null);
    }

    private static bool TryReadParent(string? value, out int? parent, out string error)
    {
        parent = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
        {
            error = $"{PARENT_VARIABLE} must be a process id, not '{value}'.";
            return false;
        }

        parent = id;
        return true;
    }

    /// <summary>Origins as browsers send them: a scheme and a host, perhaps a port, and nothing after.</summary>
    private static bool TryReadOrigins(string? value, out IReadOnlyList<string> origins, out string error)
    {
        origins = [];
        error = string.Empty;
        List<string> found = [];
        foreach (var origin in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var trimmed = origin.TrimEnd('/');
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 || uri.AbsolutePath != "/" ||
                uri.Query.Length > 0 || uri.Fragment.Length > 0 || trimmed.Contains('?') || trimmed.Contains('#'))
            {
                error = $"{ORIGINS_VARIABLE} has '{origin}', which isn't an origin such as http://tauri.localhost.";
                return false;
            }

            found.Add(trimmed);
        }

        origins = found;
        return true;
    }

    /// <summary>The token travels in a header, so it must be printable ASCII without spaces.</summary>
    private static bool TryReadToken(string? value, out string? token, out string error)
    {
        token = string.IsNullOrWhiteSpace(value) ? null : value;
        error = token is not null && !token.All(character => character is > ' ' and <= '~')
            ? $"{TOKEN_VARIABLE} must be printable ASCII without spaces."
            : string.Empty;
        return error.Length == 0;
    }

    private static bool TryFullPath(string path, string option, out string fullPath, out string error)
    {
        fullPath = string.Empty;
        error = string.Empty;
        try
        {
            fullPath = Path.GetFullPath(path);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"{option} '{path}' isn't a usable folder: {exception.Message}";
            return false;
        }
    }

    private static string DefaultDataDirectory()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(documents.Length > 0 ? documents : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Shouting Iguana");
    }

    private static string DefaultStateDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShoutingIguana");
}
