using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// Chromium doesn't authenticate to a SOCKS5 proxy. This listens on loopback with no login and, for each connection
/// Chromium asks for, opens that tunnel on the upstream proxy with the crawl's user name and password. The browser is
/// pointed here, so a rendered page goes through the same proxy as the HTTP fetch.
/// </summary>
internal sealed class Socks5AuthRelay : IAsyncDisposable
{
    private const byte VERSION = 5;
    private const byte NO_AUTH = 0;
    private const byte USER_PASSWORD = 2;
    private const byte CONNECT = 1;

    private readonly TcpListener _listener;
    private readonly string _upstreamHost;
    private readonly int _upstreamPort;
    private readonly string _user;
    private readonly string _password;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _clients = [];
    private readonly Task _accepting;

    private Socks5AuthRelay(string upstreamHost, int upstreamPort, string user, string password)
    {
        _upstreamHost = upstreamHost;
        _upstreamPort = upstreamPort;
        _user = user;
        _password = password;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _accepting = AcceptAsync();
    }

    public int Port { get; }

    /// <summary>The address Chromium is given: a SOCKS5 proxy on loopback that asks for no login.</summary>
    public string Address => $"socks5://127.0.0.1:{Port}";

    public static Socks5AuthRelay Start(string upstreamHost, int upstreamPort, string user, string password) =>
        new(upstreamHost, upstreamPort, user, password);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        try
        {
            await _accepting.ConfigureAwait(false);
            await Task.WhenAll(_clients).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException or IOException)
        {
            // Stopping.
        }

        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            var served = ServeAsync(client);
            lock (_clients)
            {
                _clients.Add(served);
            }
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                await TunnelAsync(client.GetStream(), _stop.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException or EndOfStreamException)
            {
                // Chromium closed it, or the upstream proxy refused.
            }
        }
    }

    private async Task TunnelAsync(NetworkStream fromBrowser, CancellationToken cancellationToken)
    {
        var greeting = await ReadAsync(fromBrowser, 2, cancellationToken).ConfigureAwait(false);
        await ReadAsync(fromBrowser, greeting[1], cancellationToken).ConfigureAwait(false);
        await fromBrowser.WriteAsync(new byte[] { VERSION, NO_AUTH }, cancellationToken).ConfigureAwait(false);

        var request = await ReadAsync(fromBrowser, 4, cancellationToken).ConfigureAwait(false);
        var address = request[3] switch
        {
            1 => await ReadAsync(fromBrowser, 4, cancellationToken).ConfigureAwait(false),
            3 => await ReadPrefixedAsync(fromBrowser, cancellationToken).ConfigureAwait(false),
            4 => await ReadAsync(fromBrowser, 16, cancellationToken).ConfigureAwait(false),
            _ => null
        };
        var port = await ReadAsync(fromBrowser, 2, cancellationToken).ConfigureAwait(false);
        if (greeting[0] != VERSION || request[0] != VERSION || request[1] != CONNECT || address is null)
        {
            await fromBrowser.WriteAsync(Reply(7), cancellationToken).ConfigureAwait(false);
            return;
        }

        using var upstream = new TcpClient();
        try
        {
            await upstream.ConnectAsync(_upstreamHost, _upstreamPort, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            await fromBrowser.WriteAsync(Reply(5), cancellationToken).ConfigureAwait(false);
            return;
        }

        var toProxy = upstream.GetStream();
        if (!await LoginAsync(toProxy, cancellationToken).ConfigureAwait(false) ||
            !await ConnectAsync(toProxy, request[3], address, port, cancellationToken).ConfigureAwait(false))
        {
            await fromBrowser.WriteAsync(Reply(5), cancellationToken).ConfigureAwait(false);
            return;
        }

        await fromBrowser.WriteAsync(Reply(0), cancellationToken).ConfigureAwait(false);
        using var done = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var up = fromBrowser.CopyToAsync(toProxy, done.Token);
        var down = toProxy.CopyToAsync(fromBrowser, done.Token);
        await Task.WhenAny(up, down).ConfigureAwait(false);
        await done.CancelAsync().ConfigureAwait(false);
    }

    private async Task<bool> LoginAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(new byte[] { VERSION, 1, USER_PASSWORD }, cancellationToken).ConfigureAwait(false);
        var chosen = await ReadAsync(stream, 2, cancellationToken).ConfigureAwait(false);
        if (chosen[1] != USER_PASSWORD)
        {
            return false;
        }

        var user = Encoding.UTF8.GetBytes(_user);
        var password = Encoding.UTF8.GetBytes(_password);
        var login = new byte[3 + user.Length + password.Length];
        login[0] = 1;
        login[1] = (byte)user.Length;
        user.CopyTo(login, 2);
        login[2 + user.Length] = (byte)password.Length;
        password.CopyTo(login, 3 + user.Length);
        await stream.WriteAsync(login, cancellationToken).ConfigureAwait(false);
        var answer = await ReadAsync(stream, 2, cancellationToken).ConfigureAwait(false);
        return answer[1] == 0;
    }

    private static async Task<bool> ConnectAsync(NetworkStream stream, byte kind, byte[] address, byte[] port, CancellationToken cancellationToken)
    {
        var request = new byte[4 + (kind == 3 ? 1 + address.Length : address.Length) + 2];
        request[0] = VERSION;
        request[1] = CONNECT;
        request[3] = kind;
        var at = 4;
        if (kind == 3)
        {
            request[at++] = (byte)address.Length;
        }

        address.CopyTo(request, at);
        at += address.Length;
        port.CopyTo(request, at);
        await stream.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        var reply = await ReadAsync(stream, 4, cancellationToken).ConfigureAwait(false);
        var skip = reply[3] switch
        {
            1 => 4,
            3 => (await ReadAsync(stream, 1, cancellationToken).ConfigureAwait(false))[0],
            4 => 16,
            _ => -1
        };
        if (skip < 0)
        {
            return false;
        }

        await ReadAsync(stream, skip + 2, cancellationToken).ConfigureAwait(false);
        return reply[1] == 0;
    }

    private static byte[] Reply(byte code) => [VERSION, code, 0, 1, 0, 0, 0, 0, 0, 0];

    private static async Task<byte[]> ReadPrefixedAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var length = (await ReadAsync(stream, 1, cancellationToken).ConfigureAwait(false))[0];
        return await ReadAsync(stream, length, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadAsync(NetworkStream stream, int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer;
    }
}
