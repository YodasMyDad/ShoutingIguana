namespace ShoutingIguana.Engine.Fetching.Models;

/// <summary>What happened when an address was fetched through a proxy (<see cref="ProxyTester"/>).</summary>
public enum ProxyTestOutcome
{
    /// <summary>The site's answer came back through the proxy, whatever its status.</summary>
    Ok,

    /// <summary>The proxy wants a user name and password, or didn't accept the ones given (407).</summary>
    ProxyAuthRequired,

    /// <summary>Nothing answered at the proxy's address, or the address couldn't be found.</summary>
    ProxyRefused,

    /// <summary>A connection to the proxy couldn't be made in time.</summary>
    ProxyUnreachable,

    /// <summary>The proxy answered but couldn't reach the site.</summary>
    SiteUnreachable,

    /// <summary>The site's answer didn't come back in time.</summary>
    Timeout,

    /// <summary>A secure connection couldn't be set up: a certificate wasn't trusted, say.</summary>
    Tls,

    /// <summary>Anything else.</summary>
    Failed
}
