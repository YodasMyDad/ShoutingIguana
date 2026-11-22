namespace ShoutingIguana.Engine.Configuration.Models;

/// <summary>
/// A user name and password for one host, sent as HTTP Basic or Digest when that host asks. An empty
/// <see cref="Host"/> means the site being crawled, including its www twin. A crawl file keeps the user name and
/// the host, and not the password.
/// </summary>
public sealed record SiteLogin
{
    /// <summary>The host this login is for. Empty means the start URL's site.</summary>
    public string Host { get; init; } = "";

    public string Username { get; init; } = "";

    /// <summary>Empty in a crawl file. Whoever resumes the crawl supplies it again.</summary>
    public string Password { get; init; } = "";
}
