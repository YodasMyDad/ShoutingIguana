using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// What a SOCKS5 proxy's refusal meant. .NET speaks SOCKS5 itself, a user name and password included (RFC 1929, from the
/// proxy's credentials), and reports any refusal as a <see cref="HttpRequestError.ProxyTunnelError"/> around its own
/// internal <c>SocksException</c>, whose message is the only thing that says which refusal it was. Its runtime libraries'
/// messages aren't translated, so they're read as the runtime writes them; one this doesn't know is
/// <see cref="SocksRefusal.Other"/>, with the message as it came.
/// </summary>
internal static class SocksRefusals
{
    /// <summary>The refusal behind a failed request through a SOCKS5 proxy, and the runtime's words for it; null for any other failure.</summary>
    public static (SocksRefusal Refusal, string Message)? Of(HttpRequestException exception)
    {
        if (exception.HttpRequestError != HttpRequestError.ProxyTunnelError)
        {
            return null;
        }

        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner.GetType().Name != "SocksException")
            {
                continue;
            }

            var message = inner.Message;
            var refusal = message switch
            {
                _ when message.StartsWith("Failed to authenticate", StringComparison.Ordinal) => SocksRefusal.LoginRefused,
                _ when message.StartsWith("SOCKS server requested username", StringComparison.Ordinal) => SocksRefusal.LoginNeeded,
                _ when message.StartsWith("SOCKS server did not return a suitable authentication method", StringComparison.Ordinal) => SocksRefusal.NoUsableMethod,
                _ when message.StartsWith("SOCKS server failed to connect to the destination", StringComparison.Ordinal) => SocksRefusal.SiteUnreachable,
                _ => SocksRefusal.Other
            };
            return (refusal, message);
        }

        return null;
    }

    /// <summary>
    /// The refusal in plain words. A proxy that offers nothing usable to a crawler with no login is asking for one: .NET
    /// offers a user name and password only when it has them.
    /// </summary>
    public static string Describe(SocksRefusal refusal, string message, bool hasCredentials) => refusal switch
    {
        SocksRefusal.LoginRefused => "The SOCKS5 proxy didn't accept the user name and password",
        SocksRefusal.LoginNeeded => "The SOCKS5 proxy asks for a user name and password",
        SocksRefusal.NoUsableMethod when !hasCredentials => "The SOCKS5 proxy asks for a user name and password",
        SocksRefusal.NoUsableMethod => "The SOCKS5 proxy asks for a kind of login the crawler can't give: only a user name and password can be used",
        SocksRefusal.SiteUnreachable => "The SOCKS5 proxy couldn't reach the site",
        _ => $"The SOCKS5 proxy refused: {message}"
    };

    /// <summary>Whether asking again could help: a login refused or missing stays so.</summary>
    public static bool IsTransient(SocksRefusal refusal) => refusal is SocksRefusal.SiteUnreachable or SocksRefusal.Other;
}
