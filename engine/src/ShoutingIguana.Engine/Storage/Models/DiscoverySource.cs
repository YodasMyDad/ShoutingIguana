namespace ShoutingIguana.Engine.Storage.Models;

public enum DiscoverySource : byte
{
    Seed = 0,
    Link = 1,
    Sitemap = 2,

    /// <summary>
    /// Another version of the site (bare domain or www, http or https), probed with a status check at the start of the
    /// crawl, or where such a probe redirects. A URL the site itself then links to, lists or redirects to takes that
    /// source instead.
    /// </summary>
    SiteVersion = 3
}
