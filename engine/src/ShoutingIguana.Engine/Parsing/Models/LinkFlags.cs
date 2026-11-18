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

    /// <summary>An href that resolves to the page itself, usually a <c>#section</c>.</summary>
    SamePage = 32,

    /// <summary>Came from an HTTP Link header rather than the HTML.</summary>
    FromHttpHeader = 64,

    /// <summary>
    /// Written without its scheme (<c>//cdn.example.com/app.js</c>), so it takes the page's. Noted by pages parsed from
    /// collection 7 on (<see cref="PageFacts.CURRENT_COLLECTION"/>).
    /// </summary>
    ProtocolRelative = 128
}
