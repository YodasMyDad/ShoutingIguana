namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A URL robots.txt kept the crawl from, with why.</summary>
/// <param name="Line">The line of the stored file that refused it, for <see cref="RobotsBlockReason.Rule"/>.</param>
/// <param name="Rule">That line's text.</param>
/// <param name="Error">How robots.txt failed, for <see cref="RobotsBlockReason.Unreachable"/>, when stored.</param>
public sealed record RobotsBlockedUrl(int Id, string Url, RobotsBlockReason Reason, int? Line, string? Rule, string? Error);
