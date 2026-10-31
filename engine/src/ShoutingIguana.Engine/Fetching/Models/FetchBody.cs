namespace ShoutingIguana.Engine.Fetching.Models;

/// <summary>How much of a response body the fetcher reads.</summary>
internal enum FetchBody
{
    /// <summary>Headers only. Used for status checks and resources.</summary>
    None,

    /// <summary>Read and decode the body when the response is a successful HTML document.</summary>
    Html,

    /// <summary>Read and decode any successful body as text (robots.txt).</summary>
    Text
}
