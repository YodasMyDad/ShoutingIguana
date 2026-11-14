namespace ShoutingIguana.Engine.Fetching.Models;

/// <param name="StartUrl">The address to crawl from, with its scheme.</param>
/// <param name="Reason">How it was chosen.</param>
public sealed record StartAddressChoice(string StartUrl, StartAddressReason Reason);
