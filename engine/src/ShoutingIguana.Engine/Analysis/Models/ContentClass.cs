namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>What a successful response's Content-Type says it is, as the file checks need it: one byte a URL.</summary>
internal enum ContentClass : byte
{
    /// <summary>Not a successful response, so nothing was said.</summary>
    None,

    Html,

    /// <summary>A JavaScript type: <c>text/javascript</c>, <c>application/javascript</c> and their older names.</summary>
    Script,

    /// <summary><c>text/css</c>.</summary>
    Stylesheet,

    /// <summary>Any <c>image/*</c> type.</summary>
    Image,

    /// <summary>Any other well-formed type.</summary>
    Other,

    /// <summary>A successful response with no Content-Type.</summary>
    Missing,

    /// <summary>A Content-Type that isn't <c>type/subtype</c>.</summary>
    Invalid
}
