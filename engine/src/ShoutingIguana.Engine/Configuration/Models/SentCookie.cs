namespace ShoutingIguana.Engine.Configuration.Models;

/// <summary>
/// A cookie the crawl sends from the start, as a browser that is already signed in would. An empty
/// <see cref="Domain"/> means the start URL's host. A crawl file keeps the name, domain and path, and not the value.
/// </summary>
public sealed record SentCookie
{
    public string Name { get; init; } = "";

    /// <summary>Empty in a crawl file. Whoever resumes the crawl supplies it again.</summary>
    public string Value { get; init; } = "";

    /// <summary>Empty means the start URL's host.</summary>
    public string Domain { get; init; } = "";

    /// <summary>Empty means the whole host.</summary>
    public string Path { get; init; } = "";

    public bool Secure { get; init; }
}
