using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using ShoutingIguana.Sidecar.Models;

namespace ShoutingIguana.Sidecar.Tests.Support;

/// <summary>A sidecar hosted in-process on a free port, with its own data and state folders under a temporary root.</summary>
internal sealed class TestSidecar : IAsyncDisposable
{
    public const string TOKEN = "test-token-0123456789abcdef0123456789abcdef";
    public const string ORIGIN = "http://tauri.localhost";

    private readonly bool _ownsRoot;

    private TestSidecar(SidecarHost host, SidecarOptions options, string root, bool ownsRoot)
    {
        Host = host;
        Options = options;
        Root = root;
        _ownsRoot = ownsRoot;
        Client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{host.Port}/"), Timeout = TimeSpan.FromMinutes(3) };
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TOKEN);
    }

    public SidecarHost Host { get; }

    public SidecarOptions Options { get; }

    public string Root { get; }

    public HttpClient Client { get; }

    public string DataDirectory => Options.DataDirectory;

    /// <param name="root">A root to share between sidecars (and so their library and defaults), which the caller deletes.</param>
    public static async Task<TestSidecar> StartAsync(string? root = null, Func<SidecarOptions, SidecarOptions>? configure = null)
    {
        var ownsRoot = root is null;
        root ??= NewRoot();
        var options = new SidecarOptions
        {
            DataDirectory = Path.Combine(root, "data"),
            StateDirectory = Path.Combine(root, "state"),
            Token = TOKEN,
            AllowedOrigins = [ORIGIN],
            ConsoleLogging = false,
            FileLogging = false
        };
        options = configure?.Invoke(options) ?? options;
        return new TestSidecar(await SidecarHost.StartAsync(options), options, root, ownsRoot);
    }

    public static string NewRoot() => Path.Combine(Path.GetTempPath(), $"si-sidecar-{Guid.NewGuid():N}");

    public async Task<JsonNode> GetJsonAsync(string path)
    {
        var (status, body) = await SendAsync(HttpMethod.Get, path);
        Assert.True(status == HttpStatusCode.OK, $"GET {path}: {(int)status} {body?.ToJsonString()}");
        return body!;
    }

    public Task<(HttpStatusCode Status, JsonNode? Body)> PostAsync(string path, JsonNode? body = null) => SendAsync(HttpMethod.Post, path, body);

    public async Task<(HttpStatusCode Status, JsonNode? Body)> SendAsync(HttpMethod method, string path, JsonNode? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        using var response = await Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? null : JsonNode.Parse(text));
    }

    /// <summary>Asserts a problem answer of the given type and status, and returns its body.</summary>
    public static JsonNode AssertProblem((HttpStatusCode Status, JsonNode? Body) answer, HttpStatusCode status, string type)
    {
        Assert.True(answer.Status == status, $"Expected {(int)status} {type}, got {(int)answer.Status} {answer.Body?.ToJsonString()}");
        Assert.Equal(type, (string?)answer.Body?["type"]);
        Assert.Equal((int)status, (int?)answer.Body?["status"]);
        Assert.False(string.IsNullOrWhiteSpace((string?)answer.Body?["title"]));
        return answer.Body!;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Host.DisposeAsync();
        if (_ownsRoot)
        {
            DeleteRoot(Root);
        }
    }

    /// <summary>
    /// Deletes a root, retrying while a file is still closing. Windows refuses a file another handle still holds with a
    /// sharing violation, and one that is being deleted with access denied.
    /// </summary>
    public static void DeleteRoot(string root)
    {
        for (var attempt = 0; Directory.Exists(root); attempt++)
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && attempt < 20)
            {
                Thread.Sleep(100);
            }
        }
    }
}
