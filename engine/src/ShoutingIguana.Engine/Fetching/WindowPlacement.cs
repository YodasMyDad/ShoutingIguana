using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// Where the browser's window goes when a person is needed: in the middle of the main screen, whole, at a size a
/// site's check fits in. A screen smaller than that gets a window that fills it less a margin.
/// </summary>
internal static class WindowPlacement
{
    public const int WIDTH = 1100;
    public const int HEIGHT = 800;

    /// <summary>Kept clear between the window and the screen's edges when the screen is the smaller.</summary>
    private const int MARGIN = 16;

    /// <summary>A window's invisible resize border reaches a few units past what is drawn.</summary>
    private const int BORDER = 12;

    /// <summary>What is assumed of a screen that could not be measured: the smallest laptop's.</summary>
    public static ScreenArea AssumedScreen { get; } = new(0, 0, 1280, 720);

    public static WindowBounds Centred(ScreenArea screen)
    {
        var width = Math.Max(1, Math.Min(WIDTH, screen.Width - (2 * MARGIN)));
        var height = Math.Max(1, Math.Min(HEIGHT, screen.Height - (2 * MARGIN)));
        return new WindowBounds(screen.Left + ((screen.Width - width) / 2), screen.Top + ((screen.Height - height) / 2), width, height);
    }

    /// <summary>Whether the whole window is on <paramref name="screen"/>, and neither minimised nor out of sight.</summary>
    public static bool IsOn(WindowBounds window, ScreenArea screen) =>
        window.State != "minimized" && window.Width > 0 && window.Height > 0 &&
        window.Left >= screen.Left - BORDER && window.Top >= screen.Top - BORDER &&
        window.Left + window.Width <= screen.Left + screen.Width + BORDER &&
        window.Top + window.Height <= screen.Top + screen.Height + BORDER;
}
