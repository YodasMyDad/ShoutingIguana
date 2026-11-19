namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>What a <see cref="FixExample"/>'s code is written in, so a host can colour it and say where it goes.</summary>
public enum FixLanguage
{
    /// <summary>A page's markup: a tag, an attribute.</summary>
    Html,

    /// <summary>A line of an HTTP response's headers, as <c>Name: value</c>.</summary>
    HttpHeader,

    /// <summary>Lines of a robots.txt file.</summary>
    RobotsTxt,

    /// <summary>XML, as an XML sitemap is written.</summary>
    Xml,

    /// <summary>JSON, as a JSON-LD block's content is written.</summary>
    Json
}
