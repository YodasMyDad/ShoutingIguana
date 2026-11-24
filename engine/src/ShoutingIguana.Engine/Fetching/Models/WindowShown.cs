namespace ShoutingIguana.Engine.Fetching.Models;

/// <summary>How the browser's window stood once it had been put in front of the person.</summary>
/// <param name="Bounds">Where the browser says the window is.</param>
/// <param name="Screen">The screen it was placed on.</param>
/// <param name="InFront">Whether it is the window in front, with the keyboard. Null where that can't be asked.</param>
internal sealed record WindowShown(WindowBounds Bounds, ScreenArea Screen, bool? InFront)
{
    /// <summary>The whole window is on the screen, restored.</summary>
    public bool IsOnScreen => WindowPlacement.IsOn(Bounds, Screen);
}
