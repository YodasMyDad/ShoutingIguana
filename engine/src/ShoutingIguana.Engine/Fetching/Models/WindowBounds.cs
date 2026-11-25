namespace ShoutingIguana.Engine.Fetching.Models;

/// <summary>Where a browser window is, as the debugging protocol gives it.</summary>
/// <param name="State">"normal", "minimized", "maximized" or "fullscreen".</param>
internal sealed record WindowBounds(int Left, int Top, int Width, int Height, string State = "normal");
