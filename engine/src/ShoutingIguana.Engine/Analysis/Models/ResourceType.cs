namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>
/// What a URL is, as <see cref="ResourceTypes.Classify"/> tells: by the content type of a successful response, otherwise
/// by the extension of its path. Stored in the URL summary as a number, so never reorder these.
/// </summary>
public enum ResourceType : byte
{
    /// <summary>HTML, or a URL without a file extension that wasn't fetched successfully (most of those are pages).</summary>
    Page,
    Image,
    Script,
    Stylesheet,
    Font,

    /// <summary>Video or audio.</summary>
    Media,
    Pdf,

    /// <summary>Anything else that was fetched: XML, JSON, plain text, an archive...</summary>
    Other
}
