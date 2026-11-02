namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>How a URL moves on to the next.</summary>
public enum RedirectVia
{
    /// <summary>An HTTP redirect: a 3xx with a Location.</summary>
    Http,

    /// <summary>A meta refresh with no delay, which search engines treat as a redirect.</summary>
    MetaRefresh
}
