namespace ShoutingIguana.Engine.Fetching.Models;

/// <summary>The part of a screen a window may cover, without the taskbar or the dock, in the browser's own units.</summary>
internal sealed record ScreenArea(int Left, int Top, int Width, int Height);
