namespace ShoutingIguana.Engine.Parsing.Models;

[Flags]
public enum LinkFlags : byte
{
    None = 0,
    NoFollow = 1,
    Sponsored = 2,
    Ugc = 4,

    /// <summary>The anchor wraps an image; its text is the image's alt text.</summary>
    ImageLink = 8,

    /// <summary>A head-only element (canonical, hreflang) that was found in the body, where Google ignores it.</summary>
    OutsideHead = 16,

    /// <summary>A fragment-only href (<c>#section</c>) pointing at the same page.</summary>
    SamePage = 32,

    /// <summary>Came from an HTTP Link header rather than the HTML.</summary>
    FromHttpHeader = 64
}
