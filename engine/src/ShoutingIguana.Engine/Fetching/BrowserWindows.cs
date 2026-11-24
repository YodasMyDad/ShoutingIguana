using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// What Windows itself must be asked for about the installed browser's window, which the debugging protocol can't do:
/// keep it off the taskbar while it is out of sight (a button that shows nothing when pressed is worse than none),
/// and put it in front of the person when they are needed. Windows lets only the program in front put a window
/// there, so a browser asked to "activate" its window from the background only flashes its taskbar button.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class BrowserWindows
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x80;
    private const long WS_EX_APPWINDOW = 0x40000;
    private const int SW_HIDE = 0;
    private const int SW_SHOWNOACTIVATE = 4;
    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;
    private const uint GW_OWNER = 4;
    private const uint SWP_NOSIZE = 0x1;
    private const uint SWP_NOMOVE = 0x2;
    private const uint SWP_SHOWWINDOW = 0x40;
    private const uint SPI_GETWORKAREA = 0x30;
    private const byte VK_MENU = 0x12;
    private const uint KEYEVENTF_KEYUP = 0x2;
    private static readonly nint HWND_TOPMOST = -1;
    private static readonly nint HWND_NOTOPMOST = -2;

    /// <summary>The main screen without its taskbar, in the units the browser places its window by. Null when Windows won't say.</summary>
    public static ScreenArea? WorkArea()
    {
        if (!SystemParametersInfo(SPI_GETWORKAREA, 0, out var area, 0))
        {
            return null;
        }

        // A program that reads real pixels gets them scaled; one that doesn't is given the browser's units already.
        var scale = GetDpiForSystem() / 96.0;
        if (scale <= 0)
        {
            scale = 1;
        }

        var screen = new ScreenArea((int)(area.Left / scale), (int)(area.Top / scale), (int)((area.Right - area.Left) / scale),
            (int)((area.Bottom - area.Top) / scale));
        return screen is { Width: > 0, Height: > 0 } ? screen : null;
    }

    /// <summary>Takes the browser's windows off the taskbar and out of Alt+Tab. They stay drawn, wherever they are.</summary>
    /// <returns>Whether a window was found.</returns>
    public static bool KeepOffTaskbar(int processId)
    {
        var windows = Of(processId);
        foreach (var window in windows)
        {
            var style = GetWindowLongPtr(window, GWL_EXSTYLE).ToInt64();
            if ((style & WS_EX_TOOLWINDOW) != 0)
            {
                continue;
            }

            // The taskbar reads a window's style when it is shown.
            ShowWindow(window, SW_HIDE);
            SetWindowLongPtr(window, GWL_EXSTYLE, new nint((style | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW));
            ShowWindow(window, SW_SHOWNOACTIVATE);
        }

        return windows.Count > 0;
    }

    /// <summary>Gives the browser's windows their taskbar button back, restores them, and puts them in front with the keyboard.</summary>
    /// <returns>Whether one of them is now the window in front.</returns>
    public static bool BringToFront(int processId)
    {
        var windows = Of(processId);
        foreach (var window in windows)
        {
            var style = GetWindowLongPtr(window, GWL_EXSTYLE).ToInt64();
            if ((style & WS_EX_TOOLWINDOW) != 0)
            {
                ShowWindow(window, SW_HIDE);
                SetWindowLongPtr(window, GWL_EXSTYLE, new nint((style & ~WS_EX_TOOLWINDOW) | WS_EX_APPWINDOW));
                ShowWindow(window, SW_SHOW);
            }

            if (IsIconic(window))
            {
                ShowWindow(window, SW_RESTORE);
            }

            Raise(window, tapAlt: false);
            if (GetForegroundWindow() != window)
            {
                // Windows gives the foreground to whoever last had a key pressed: a tap of Alt is the usual way to be let through.
                Raise(window, tapAlt: true);
            }
        }

        return IsInFront(processId);
    }

    /// <summary>Whether one of the browser's windows is the one in front.</summary>
    public static bool IsInFront(int processId)
    {
        var front = GetForegroundWindow();
        return front != 0 && Of(processId).Contains(front);
    }

    private static void Raise(nint window, bool tapAlt)
    {
        var front = GetForegroundWindow();
        var frontThread = front == 0 ? 0 : GetWindowThreadProcessId(front, out _);
        var thisThread = GetCurrentThreadId();
        var attached = frontThread != 0 && frontThread != thisThread && AttachThreadInput(thisThread, frontThread, true);
        try
        {
            if (tapAlt)
            {
                keybd_event(VK_MENU, 0, 0, 0);
                keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, 0);
            }

            // Above every other window even if the keyboard is refused, and then an ordinary window again.
            SetWindowPos(window, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            SetWindowPos(window, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            BringWindowToTop(window);
            SetForegroundWindow(window);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(thisThread, frontThread, false);
            }
        }
    }

    /// <summary>The browser's own top-level windows that are shown: its browser windows, not the helpers it keeps hidden.</summary>
    private static List<nint> Of(int processId)
    {
        List<nint> found = [];
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner == processId && IsWindowVisible(window) && GetWindow(window, GW_OWNER) == 0)
            {
                var name = new StringBuilder(64);
                GetClassName(window, name, name.Capacity);
                if (name.ToString().StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal))
                {
                    found.Add(window);
                }
            }

            return true;
        }, 0);
        return found;
    }

    private delegate bool EnumWindowsCallback(nint window, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out int processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder name, int capacity);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint thread, uint to, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out Rect area, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

#pragma warning disable SA1300, IDE1006 // Windows' own name.
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
#pragma warning restore SA1300, IDE1006
}
