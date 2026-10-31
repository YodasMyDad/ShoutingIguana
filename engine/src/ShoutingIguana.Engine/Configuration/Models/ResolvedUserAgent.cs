namespace ShoutingIguana.Engine.Configuration.Models;

/// <param name="Header">Value sent in the User-Agent header.</param>
/// <param name="RobotsToken">Product token matched against robots.txt groups and robots meta names.</param>
public sealed record ResolvedUserAgent(string Header, string RobotsToken);
