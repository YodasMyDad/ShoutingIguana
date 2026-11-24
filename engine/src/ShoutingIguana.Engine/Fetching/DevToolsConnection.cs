using System.Buffers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// One connection to a browser's own debugging port: commands out, answers and events back. The installed browser is
/// driven with this and not with Playwright, which marks the browser as automated, and a site's human check then
/// never clears. Only what a page load needs is sent, and nothing is run in the page but a read of its HTML.
/// </summary>
internal sealed class DevToolsConnection : IAsyncDisposable
{
    /// <summary>A browser that hasn't answered a command in this long is not going to.</summary>
    private const int ANSWER_SECONDS = 30;

    private readonly ClientWebSocket _socket = new();
    private readonly SemaphoreSlim _sending = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _closing = new();
    private Task _reading = Task.CompletedTask;
    private int _lastId;

    /// <summary>An event the browser sent: the session it is for (null for the browser's own), its name, and its parameters.</summary>
    public event Action<string?, string, JsonElement>? Event;

    public bool IsOpen => _socket.State == WebSocketState.Open;

    public static async Task<DevToolsConnection> ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        var connection = new DevToolsConnection();
        try
        {
            await connection._socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        connection._reading = Task.Run(connection.ReadAsync, CancellationToken.None);
        return connection;
    }

    /// <summary>Sends one command and returns its result. Throws when the browser refuses it, or has gone.</summary>
    /// <param name="timeout">How long to wait for the answer. Left out, <see cref="ANSWER_SECONDS"/>.</param>
    public async Task<JsonElement> SendAsync(string method, object? parameters = null, string? sessionId = null,
        CancellationToken cancellationToken = default, TimeSpan? timeout = null)
    {
        using var patience = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        patience.CancelAfter(timeout ?? TimeSpan.FromSeconds(ANSWER_SECONDS));
        var caller = cancellationToken;
        cancellationToken = patience.Token;
        var id = Interlocked.Increment(ref _lastId);
        var answer = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = answer;
        try
        {
            var message = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
            {
                ["id"] = id,
                ["method"] = method,
                ["params"] = parameters ?? new Dictionary<string, object?>(),
                ["sessionId"] = sessionId
            }.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value));

            await _sending.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _socket.SendAsync(message, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sending.Release();
            }

            return await answer.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested)
        {
            throw new IOException($"The browser didn't answer {method}.");
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or InvalidOperationException && !IsOpen)
        {
            throw new IOException("The browser has gone.", ex);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task ReadAsync()
    {
        var buffer = new ArrayBufferWriter<byte>(64 * 1024);
        try
        {
            while (_socket.State == WebSocketState.Open)
            {
                buffer.Clear();
                ValueWebSocketReceiveResult received;
                do
                {
                    received = await _socket.ReceiveAsync(buffer.GetMemory(64 * 1024), _closing.Token).ConfigureAwait(false);
                    buffer.Advance(received.Count);
                }
                while (!received.EndOfMessage);

                if (received.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                Dispatch(buffer.WrittenMemory);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException or IOException)
        {
            // The browser closed, or this connection did.
        }
        finally
        {
            foreach (var waiting in _pending.Values)
            {
                waiting.TrySetException(new IOException("The browser has gone."));
            }
        }
    }

    private void Dispatch(ReadOnlyMemory<byte> message)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(message);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return;
        }

        if (root.TryGetProperty("id", out var id))
        {
            if (!_pending.TryGetValue(id.GetInt32(), out var answer))
            {
                return;
            }

            if (root.TryGetProperty("error", out var error))
            {
                var text = error.TryGetProperty("message", out var said) ? said.GetString() : error.GetRawText();
                answer.TrySetException(new InvalidOperationException(text));
            }
            else
            {
                answer.TrySetResult(root.TryGetProperty("result", out var result) ? result : default);
            }

            return;
        }

        if (root.TryGetProperty("method", out var method) && method.GetString() is { } name)
        {
            var session = root.TryGetProperty("sessionId", out var sessionId) ? sessionId.GetString() : null;
            var parameters = root.TryGetProperty("params", out var given) ? given : default;
            try
            {
                Event?.Invoke(session, name, parameters);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A handler's fault must not end the reading of the browser's answers.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _closing.CancelAsync().ConfigureAwait(false);
        try
        {
            await _reading.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
        }

        _socket.Dispose();
        _sending.Dispose();
        _closing.Dispose();
    }
}
