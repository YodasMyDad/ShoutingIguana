using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ShoutingIguana.TestSite;

/// <summary>
/// A SOCKS5 proxy on loopback for tests (RFC 1928), which requires a user name and password (RFC 1929) and tunnels a
/// CONNECT to the address asked for once they're right. It offers no other way in: a client that can't log in is told
/// none of its methods will do. It keeps every login tried and every tunnel opened, so a test can say what went through.
/// </summary>
public sealed class Socks5ProxyServer : IAsyncDisposable
{
    private const byte VERSION = 5;
    private const byte USER_PASSWORD = 2;
    private const byte NO_ACCEPTABLE_METHOD = 0xFF;
    private const byte LOGIN_VERSION = 1;
    private const byte CONNECT = 1;

    private readonly TcpListener _listener;
    private readonly string _user;
    private readonly string _password;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<(string User, string Password, bool Accepted)> _logins = new();
    private readonly ConcurrentQueue<string> _tunnels = new();
    private readonly ConcurrentBag<Task> _clients = [];
    private readonly Task _accepting;
    private int _greetings;

    private Socks5ProxyServer(string user, string password)
    {
        _user = user;
        _password = password;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _accepting = AcceptAsync();
    }

    public int Port { get; }

    /// <summary>Every login tried, in order, and whether it was let in.</summary>
    public IReadOnlyList<(string User, string Password, bool Accepted)> Logins => [.. _logins];

    /// <summary>Every <c>host:port</c> a tunnel was opened to, in order.</summary>
    public IReadOnlyList<string> Tunnels => [.. _tunnels];

    /// <summary>How many clients said hello, whether or not they could log in.</summary>
    public int Greetings => Volatile.Read(ref _greetings);

    /// <summary>A proxy that lets in this user name with this password, and no one else.</summary>
    public static Socks5ProxyServer Start(string user, string password) => new(user, password);

    /// <summary>The proxy's address as a crawl's options take it, with a login (escaped as a URL's) or without one.</summary>
    public string Address(string? user = null, string? password = null) => user is null
        ? $"socks5://127.0.0.1:{Port}"
        : $"socks5://{Uri.EscapeDataString(user)}:{Uri.EscapeDataString(password ?? string.Empty)}@127.0.0.1:{Port}";

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        try
        {
            await _accepting.ConfigureAwait(false);
            await Task.WhenAll(_clients).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
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

            _clients.Add(ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                await ServeAsync(client.GetStream(), _stop.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException or EndOfStreamException)
            {
                // The client went, or the proxy is stopping.
            }
        }
    }

    private async Task ServeAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        // The greeting: the version, then the ways in the client offers. Only a login will do.
        var greeting = await ReadAsync(stream, 2, cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _greetings);
        var methods = await ReadAsync(stream, greeting[1], cancellationToken).ConfigureAwait(false);
        if (greeting[0] != VERSION || !methods.Contains(USER_PASSWORD))
        {
            await stream.WriteAsync(new byte[] { VERSION, NO_ACCEPTABLE_METHOD }, cancellationToken).ConfigureAwait(false);
            return;
        }

        await stream.WriteAsync(new byte[] { VERSION, USER_PASSWORD }, cancellationToken).ConfigureAwait(false);

        // RFC 1929: the login's version, the user name and the password, each after its length.
        var version = await ReadAsync(stream, 1, cancellationToken).ConfigureAwait(false);
        var user = Encoding.UTF8.GetString(await ReadAsync(stream, (await ReadAsync(stream, 1, cancellationToken).ConfigureAwait(false))[0], cancellationToken).ConfigureAwait(false));
        var password = Encoding.UTF8.GetString(await ReadAsync(stream, (await ReadAsync(stream, 1, cancellationToken).ConfigureAwait(false))[0], cancellationToken).ConfigureAwait(false));
        var accepted = version[0] == LOGIN_VERSION && user == _user && password == _password;
        _logins.Enqueue((user, password, accepted));
        await stream.WriteAsync(new byte[] { LOGIN_VERSION, accepted ? (byte)0 : (byte)1 }, cancellationToken).ConfigureAwait(false);
        if (!accepted)
        {
            return;
        }

        // The request: CONNECT to an IPv4 address, a name or an IPv6 address, and a port.
        var request = await ReadAsync(stream, 4, cancellationToken).ConfigureAwait(false);
        var host = request[3] switch
        {
            1 => new IPAddress(await ReadAsync(stream, 4, cancellationToken).ConfigureAwait(false)).ToString(),
            3 => Encoding.ASCII.GetString(await ReadAsync(stream, (await ReadAsync(stream, 1, cancellationToken).ConfigureAwait(false))[0], cancellationToken).ConfigureAwait(false)),
            4 => new IPAddress(await ReadAsync(stream, 16, cancellationToken).ConfigureAwait(false)).ToString(),
            _ => null
        };
        var portBytes = await ReadAsync(stream, 2, cancellationToken).ConfigureAwait(false);
        var port = (portBytes[0] << 8) | portBytes[1];
        if (request[0] != VERSION || request[1] != CONNECT || host is null)
        {
            await stream.WriteAsync(Reply(7), cancellationToken).ConfigureAwait(false);
            return;
        }

        using var target = new TcpClient();
        try
        {
            await target.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            await stream.WriteAsync(Reply(5), cancellationToken).ConfigureAwait(false);
            return;
        }

        _tunnels.Enqueue($"{host}:{port}");
        await stream.WriteAsync(Reply(0), cancellationToken).ConfigureAwait(false);

        // Each way until either side is done.
        var upstream = target.GetStream();
        using var done = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var up = stream.CopyToAsync(upstream, done.Token);
        var down = upstream.CopyToAsync(stream, done.Token);
        await Task.WhenAny(up, down).ConfigureAwait(false);
        await done.CancelAsync().ConfigureAwait(false);
    }

    /// <summary>The answer to a request: the reply code, and an IPv4 address and port the client doesn't use.</summary>
    private static byte[] Reply(byte code) => [VERSION, code, 0, 1, 0, 0, 0, 0, 0, 0];

    private static async Task<byte[]> ReadAsync(NetworkStream stream, int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer;
    }
}
