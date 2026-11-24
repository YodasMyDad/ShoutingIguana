using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Tests.Fetching;

/// <summary>
/// Where the browser's window goes when a person is asked to complete a site's check: whole on the main screen, in
/// its middle, restored. A window left where it is kept out of sight, or only un-minimised, can't be got into view.
/// </summary>
public sealed class WindowPlacementTests
{
    [Theory]
    [InlineData(0, 0, 1920, 1040)]
    [InlineData(0, 0, 1280, 680)]
    [InlineData(0, 25, 1440, 815)]
    [InlineData(0, 0, 1024, 560)]
    [InlineData(0, 0, 800, 480)]
    public void TheWindowIsWholeOnTheScreenAndInItsMiddle(int left, int top, int width, int height)
    {
        var screen = new ScreenArea(left, top, width, height);

        var window = WindowPlacement.Centred(screen);

        Assert.Equal("normal", window.State);
        Assert.True(WindowPlacement.IsOn(window, screen));
        Assert.True(window.Left >= screen.Left && window.Top >= screen.Top);
        Assert.True(window.Left + window.Width <= screen.Left + screen.Width);
        Assert.True(window.Top + window.Height <= screen.Top + screen.Height);
        Assert.InRange(window.Width, 1, WindowPlacement.WIDTH);
        Assert.InRange(window.Height, 1, WindowPlacement.HEIGHT);

        // The same space each side, to a unit.
        Assert.InRange((window.Left - screen.Left) - (screen.Left + screen.Width - window.Left - window.Width), -1, 1);
        Assert.InRange((window.Top - screen.Top) - (screen.Top + screen.Height - window.Top - window.Height), -1, 1);
    }

    [Fact]
    public void AScreenWithRoomGetsTheFullSize()
    {
        var window = WindowPlacement.Centred(new ScreenArea(0, 0, 1920, 1040));

        Assert.Equal(new WindowBounds(410, 120, WindowPlacement.WIDTH, WindowPlacement.HEIGHT), window);
    }

    [Fact]
    public void AWindowOutOfSightIsNotOnTheScreen()
    {
        var screen = new ScreenArea(0, 0, 1920, 1040);

        Assert.False(WindowPlacement.IsOn(new WindowBounds(-32_000, -32_000, 1100, 800), screen));
        Assert.False(WindowPlacement.IsOn(new WindowBounds(410, 120, 1100, 800, "minimized"), screen));
        Assert.False(WindowPlacement.IsOn(new WindowBounds(1500, 120, 1100, 800), screen));
        Assert.False(WindowPlacement.IsOn(new WindowBounds(410, 600, 1100, 800), screen));

        // A restored window's invisible border reaches a little past the screen's edge.
        Assert.True(WindowPlacement.IsOn(new WindowBounds(-7, 0, 1100, 800), screen));
    }

    /// <summary>
    /// The real browser: out of sight as it starts, whole on the screen and restored once shown, and out of sight
    /// again once hidden. On Windows it is the window in front. Needs a browser and a screen, as a person's computer has.
    /// </summary>
    [Fact]
    public async Task TheBrowsersWindowIsShownOnTheScreenAndPutAwayAgain()
    {
        if (InstalledBrowsers.Find() is not { } found || !InstalledBrowsers.HasDisplay())
        {
            return;
        }

        var kept = BrowserProfile.Folder;
        BrowserProfile.Folder = null;
        try
        {
            using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using var browser = await InstalledBrowserProcess.StartAsync(found, null, NullLogger.Instance, patience.Token);
            var targetId = await browser.OpenTabAsync(patience.Token);

            // Out of sight, it never keeps the keyboard: what a person typed would go to a page they can't see.
            var tookKeyboard = browser.IsInFront;
            var before = await browser.BoundsAsync(targetId, patience.Token);
            var shown = await browser.ShowAsync(targetId, patience.Token);
            var during = await browser.BoundsAsync(targetId, patience.Token);
            var inFront = browser.IsInFront;
            await browser.HideAsync(targetId, patience.Token);
            var after = await browser.BoundsAsync(targetId, patience.Token);
            var keptKeyboard = browser.IsInFront;

            Assert.False(WindowPlacement.IsOn(before, shown.Screen), $"Before it was shown the window was at {before}");
            Assert.True(shown.IsOnScreen, $"Shown, the window was at {shown.Bounds} on {shown.Screen}");
            Assert.Equal("normal", during.State);
            Assert.True(WindowPlacement.IsOn(during, shown.Screen), $"While shown the window was at {during} on {shown.Screen}");
            Assert.False(WindowPlacement.IsOn(after, shown.Screen), $"Hidden again, the window was at {after}");
            if (OperatingSystem.IsWindows() && Environment.UserInteractive)
            {
                Assert.True(shown.InFront, "Shown, the window was not the one in front");
                Assert.True(inFront, "A moment later the window was no longer the one in front");
                Assert.False(tookKeyboard, "Out of sight, with a tab just opened, the browser had the keyboard");
                Assert.False(keptKeyboard, "Hidden again, the browser still had the keyboard");
            }
        }
        finally
        {
            BrowserProfile.Folder = kept;
        }
    }
}
