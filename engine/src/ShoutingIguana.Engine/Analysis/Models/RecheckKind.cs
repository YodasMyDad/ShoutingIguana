namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>What a recheck of a check reads again (<see cref="IssueDefinition.Recheck"/>). Served in camel case.</summary>
public enum RecheckKind
{
    /// <summary>The check's URLs, and what is needed to judge them: what they name, or what refers to them.</summary>
    Urls,

    /// <summary>Every sitemap the crawl read, and the conventional <c>/sitemap.xml</c> where the crawl probed for one.</summary>
    Sitemaps,

    /// <summary>The robots.txt of each origin the check's issues name.</summary>
    RobotsTxt,

    /// <summary>The probes of the site's other versions (the bare domain or www, over http or https), and where they redirect.</summary>
    SiteVersions
}
