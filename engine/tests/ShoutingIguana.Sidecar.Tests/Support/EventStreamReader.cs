using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace ShoutingIguana.Sidecar.Tests.Support;

/// <summary>Reads <c>GET /api/events</c> as the UI does, with fetch: the token in a header, the stream parsed line by line.</summary>
internal sealed class EventStreamReader : IAsyncDisposable
{
    public static readonly TimeSpan TIMEOUT = TimeSpan.FromMinutes(2);

    private readonly HttpResponseMessage _response;
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<SseEvent> _events = Channel.CreateUnbounded<SseEvent>();
    private readonly Task _reading;
    private int _comments;

    private EventStreamReader(HttpResponseMessage response, Stream stream)
    {
        _response = response;
        _reading = ReadAsync(stream);
    }

    /// <summary>Every event read so far, in order. Read it through <see cref="Seen"/>, which copies it under its lock.</summary>
    private List<SseEvent> History { get; } = [];

    /// <summary>Keep-alive comments read so far.</summary>
    public int Comments => Volatile.Read(ref _comments);

    /// <summary>Completes when the server ends the stream.</summary>
    public Task Ended => _reading;

    public static async Task<EventStreamReader> ConnectAsync(HttpClient client)
    {
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "api/events"), HttpCompletionOption.ResponseHeadersRead);
        Assert.True(response.IsSuccessStatusCode, $"GET api/events: {(int)response.StatusCode}");
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        return new EventStreamReader(response, await response.Content.ReadAsStreamAsync());
    }

    /// <summary>Every event read so far, in order.</summary>
    public IReadOnlyList<SseEvent> Seen()
    {
        lock (History)
        {
            return [.. History];
        }
    }

    /// <summary>The next event with this name that matches, skipping the rest.</summary>
    public async Task<JsonNode> NextAsync(string name, Func<JsonNode, bool>? match = null)
    {
        using var timeout = new CancellationTokenSource(TIMEOUT);
        try
        {
            while (true)
            {
                var next = await _events.Reader.ReadAsync(timeout.Token);
                if (next.Name == name && (match is null || match(next.Data)))
                {
                    return next.Data;
                }
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"No '{name}' event came. Seen: {string.Join(", ", Seen().Select(seen => $"{seen.Name} {seen.Data.ToJsonString()}"))}");
        }
        catch (ChannelClosedException)
        {
            throw new InvalidOperationException($"The stream ended before a '{name}' event. Seen: {string.Join(", ", Seen().Select(seen => seen.Name))}");
        }
    }

    /// <summary>The next crawl event for a crawl with this status.</summary>
    public Task<JsonNode> CrawlStatusAsync(string crawlId, string status) =>
        NextAsync("crawl", data => (string?)data["crawlId"] == crawlId && (string?)data["status"] == status);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _response.Dispose();
        try
        {
            await _reading;
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Disconnected.
        }
    }

    private async Task ReadAsync(Stream stream)
    {
        try
        {
            using var reader = new StreamReader(stream);
            string? name = null;
            List<string> data = [];
            while (await reader.ReadLineAsync(_stop.Token) is { } line)
            {
                if (line.Length == 0)
                {
                    if (name is not null)
                    {
                        var parsed = new SseEvent(name, JsonNode.Parse(string.Join('\n', data))!);
                        lock (History)
                        {
                            History.Add(parsed);
                        }

                        _events.Writer.TryWrite(parsed);
                    }

                    (name, data) = (null, []);
                }
                else if (line.StartsWith(':'))
                {
                    Interlocked.Increment(ref _comments);
                }
                else if (line.StartsWith("event: ", StringComparison.Ordinal))
                {
                    name = line["event: ".Length..];
                }
                else if (line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    data.Add(line["data: ".Length..]);
                }
            }
        }
        finally
        {
            _events.Writer.TryComplete();
        }
    }
}
