namespace ShoutingIguana.Engine.Fetching.Models;

/// <summary>A browser this computer has, which the crawl can open as a person would.</summary>
/// <param name="Name">What people call it: "Chrome", "Edge", "Chromium".</param>
/// <param name="Path">Its program file.</param>
internal sealed record InstalledBrowser(string Name, string Path);
