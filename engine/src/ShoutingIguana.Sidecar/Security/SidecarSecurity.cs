using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Primitives;

namespace ShoutingIguana.Sidecar.Security;

/// <summary>Who may call the API: requests that carry the token, addressed to the loopback port, from an allowed origin if any.</summary>
internal sealed class SidecarSecurity(string token, IReadOnlyList<string> allowedOrigins)
{
    private const string BEARER = "Bearer ";

    // Compared as hashes, so the comparison takes the same time whatever the length of what was sent.
    private readonly byte[] _tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
    private readonly HashSet<string> _origins = new(allowedOrigins, StringComparer.OrdinalIgnoreCase);

    /// <summary>A token for a sidecar started without one, as the shell makes them: 32 random bytes as hex.</summary>
    public static string CreateToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// A browser fills in the Host header from the address it was given, so a page that rebinds its own name to
    /// 127.0.0.1 still says its own name. Only the loopback names, on the port this connection came in on, are accepted.
    /// </summary>
    public static bool IsAllowedHost(HostString host, int localPort) =>
        host.Port == localPort && (host.Host == "127.0.0.1" || string.Equals(host.Host, "localhost", StringComparison.OrdinalIgnoreCase));

    public bool IsAllowedOrigin(string origin) => _origins.Contains(origin);

    public bool IsAuthorized(StringValues authorization)
    {
        if (authorization.Count != 1 || authorization[0] is not { } value || !value.StartsWith(BEARER, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var given = SHA256.HashData(Encoding.UTF8.GetBytes(value[BEARER.Length..]));
        return CryptographicOperations.FixedTimeEquals(given, _tokenHash);
    }
}
