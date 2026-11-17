namespace ShoutingIguana.Engine.Fetching.Models;

/// <summary>Why a SOCKS5 proxy wouldn't open a tunnel to the site, as .NET's SOCKS client was told.</summary>
internal enum SocksRefusal
{
    /// <summary>It asked for a user name and password (RFC 1929), and refused the ones given.</summary>
    LoginRefused,

    /// <summary>It asks for a user name and password, and none was given.</summary>
    LoginNeeded,

    /// <summary>It offered no way in .NET has: a login of another kind (GSSAPI, say), or none at all.</summary>
    NoUsableMethod,

    /// <summary>It let the crawler in, but couldn't connect to the site.</summary>
    SiteUnreachable,

    /// <summary>Something else in the proxy's answer: not a SOCKS5 proxy, say.</summary>
    Other
}
