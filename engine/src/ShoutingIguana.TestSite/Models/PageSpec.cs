namespace ShoutingIguana.TestSite.Models;

/// <summary>One HTML page of the synthetic site. The defaults make a page that passes every check; a planted issue changes one thing.</summary>
internal sealed record PageSpec
{
    /// <summary>Use the page's own absolute URL as its canonical.</summary>
    public const string SELF = "self";

    public required string Path { get; init; }

    /// <summary>Null leaves the title element out.</summary>
    public required string? Title { get; init; }

    /// <summary>Null leaves the meta description out.</summary>
    public required string? Description { get; init; }

    /// <summary>Null leaves the H1 out.</summary>
    public required string? H1 { get; init; }

    /// <summary>HTML inside main, after the H1.</summary>
    public required string Content { get; init; }

    /// <summary>The canonical href as written: <see cref="SELF"/>, any other value, or null for none.</summary>
    public string? Canonical { get; init; } = SELF;

    /// <summary>Content of a robots meta tag, e.g. "noindex".</summary>
    public string? Robots { get; init; }

    public string? JsonLd { get; init; }

    /// <summary>Raw HTML appended to the head.</summary>
    public string? ExtraHead { get; init; }

    public bool Viewport { get; init; } = true;

    /// <summary>The viewport meta tag's content, when <see cref="Viewport"/> is on.</summary>
    public string ViewportContent { get; init; } = "width=device-width, initial-scale=1";

    public bool Lang { get; init; } = true;

    /// <summary>The html element's lang, when <see cref="Lang"/> is on.</summary>
    public string LangValue { get; init; } = "en";

    public bool Charset { get; init; } = true;

    public bool OpenGraph { get; init; } = true;

    /// <summary>The template's X card, a large one, filled from the Open Graph tags (batch 9).</summary>
    public bool TwitterCard { get; init; } = true;

    /// <summary>The site's header, menu and footer around the content; false leaves the page with only its own links.</summary>
    public bool Chrome { get; init; } = true;
}
