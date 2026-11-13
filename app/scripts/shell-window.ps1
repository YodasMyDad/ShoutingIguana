# The shell's window, from outside, for scripts/smoke-shell.mjs (Windows only). What Playwright can't do over
# WebView2's DevTools port: keys as the webview itself receives real ones, the tray icon, native dialogs, the window.
#
#   shell-window.ps1 state <app pid>              {"window":bool,"dialog":"title"|null,"webviewWindows":n} as JSON
#   shell-window.ps1 keys <app pid> <vk,vk,...>   presses keys (virtual-key codes, no modifiers) in the webview
#   shell-window.ps1 tray-click <app pid>         a left click on the tray icon
#   shell-window.ps1 press <app pid> <button>     presses a button of the app's native dialog
#   shell-window.ps1 close <app pid>              closes the main window, as its X does
#
# Why `keys` isn't Playwright's keyboard: a key sent over DevTools enters below WebView2's own key handling. It
# never raises AcceleratorKeyPressed (so the shell's F10 and Alt handling isn't run), and Chromium either skips its
# browser accelerators for it (no native key code) or runs them whatever AreBrowserAcceleratorKeysEnabled says (with
# one). A key message in the webview's own queue is handled as a real key press is. The window needs the thread's
# focus for that, which a test run's window may not have: it is given here (AttachThreadInput, SetFocus).
param([Parameter(Mandatory)][string]$Command, [Parameter(Mandatory)][int]$AppPid, [string]$Argument)

Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class ShellWindow {
  delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr lParam);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr lParam);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
  [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
  [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
  [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from, uint to, bool attach);
  [DllImport("user32.dll")] static extern IntPtr SetFocus(IntPtr hwnd);
  [DllImport("user32.dll")] static extern IntPtr SetActiveWindow(IntPtr hwnd);
  [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
  [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

  public static string Text(IntPtr hwnd) { var text = new StringBuilder(512); GetWindowText(hwnd, text, 512); return text.ToString(); }
  public static string Class(IntPtr hwnd) { var text = new StringBuilder(256); GetClassName(hwnd, text, 256); return text.ToString(); }
  public static bool Visible(IntPtr hwnd) { return IsWindowVisible(hwnd); }

  /// Every top-level window of a process.
  public static List<IntPtr> TopLevel(uint pid) {
    var found = new List<IntPtr>();
    EnumWindows(delegate(IntPtr hwnd, IntPtr l) { uint owner; GetWindowThreadProcessId(hwnd, out owner); if (owner == pid) found.Add(hwnd); return true; }, IntPtr.Zero);
    return found;
  }

  public static List<IntPtr> Children(IntPtr parent) {
    var found = new List<IntPtr>();
    EnumChildWindows(parent, delegate(IntPtr hwnd, IntPtr l) { found.Add(hwnd); return true; }, IntPtr.Zero);
    return found;
  }

  /// Gives a window its own thread's keyboard focus, as a click in it would.
  public static void Focus(IntPtr top, IntPtr hwnd) {
    uint pid; uint thread = GetWindowThreadProcessId(hwnd, out pid); uint me = GetCurrentThreadId();
    bool attached = AttachThreadInput(me, thread, true);
    SetActiveWindow(top); SetFocus(hwnd);
    if (attached) AttachThreadInput(me, thread, false);
  }

  /// A key press as Windows queues one: WM_KEYDOWN then WM_KEYUP, with the key's scan code.
  public static void Key(IntPtr hwnd, uint key) {
    long scan = MapVirtualKey(key, 0);
    PostMessage(hwnd, 0x0100, new IntPtr(key), new IntPtr(1 | (scan << 16)));
    System.Threading.Thread.Sleep(60);
    PostMessage(hwnd, 0x0101, new IntPtr(key), new IntPtr(1 | (scan << 16) | (1L << 30) | (1L << 31)));
    System.Threading.Thread.Sleep(60);
  }

  public static void Post(IntPtr hwnd, uint message, long wParam, long lParam) { PostMessage(hwnd, message, new IntPtr(wParam), new IntPtr(lParam)); }
  public static void Click(IntPtr button) { SendMessage(button, 0x00F5, IntPtr.Zero, IntPtr.Zero); }
}
'@

$windows = [ShellWindow]::TopLevel($AppPid)
$main = $windows | Where-Object { [ShellWindow]::Class($_) -eq 'Tauri Window' } | Select-Object -First 1
if (-not $main) { Write-Error "Process $AppPid has no app window"; exit 1 }
$dialog = $windows | Where-Object { [ShellWindow]::Class($_) -eq '#32770' -and [ShellWindow]::Visible($_) } | Select-Object -First 1

switch ($Command) {
  'state' {
    $webview = Get-CimInstance Win32_Process -Filter "ParentProcessId=$AppPid AND Name='msedgewebview2.exe'" | Select-Object -First 1
    # The webview's own top-level windows: a find bar, a print or downloads window, DevTools. None when idle.
    $extra = if ($webview) { @([ShellWindow]::TopLevel($webview.ProcessId) | Where-Object { [ShellWindow]::Visible($_) }).Count } else { 0 }
    # The webview's surfaces inside the window: one for the page, and one more while WebView2's print dialog is up.
    $surfaces = @([ShellWindow]::Children($main) | Where-Object { [ShellWindow]::Class($_) -eq 'Chrome_RenderWidgetHostHWND' -and [ShellWindow]::Visible($_) }).Count
    @{ window = [ShellWindow]::Visible($main); dialog = if ($dialog) { [ShellWindow]::Text($dialog) } else { $null }; webviewWindows = $extra; surfaces = $surfaces } | ConvertTo-Json -Compress
  }
  'keys' {
    $target = [ShellWindow]::Children($main) | Where-Object { [ShellWindow]::Class($_) -eq 'Chrome_WidgetWin_1' } | Select-Object -First 1
    if (-not $target) { Write-Error 'The window has no webview'; exit 1 }
    [ShellWindow]::Focus($main, $target)
    foreach ($key in $Argument -split ',') { [ShellWindow]::Key($target, [uint32]$key) }
  }
  'tray-click' {
    # What Windows posts the tray icon's window for a click: its callback message (6002) with WM_LBUTTONUP.
    $tray = $windows | Where-Object { [ShellWindow]::Class($_) -eq 'tray_icon_app' } | Select-Object -First 1
    if (-not $tray) { Write-Error 'The app has no tray icon'; exit 1 }
    [ShellWindow]::Post($tray, 6002, 0, 0x0202)
  }
  'press' {
    if (-not $dialog) { Write-Error 'The app shows no native dialog'; exit 1 }
    $button = [ShellWindow]::Children($dialog) | Where-Object { [ShellWindow]::Text($_) -eq $Argument } | Select-Object -First 1
    if (-not $button) { Write-Error "The dialog has no '$Argument' button"; exit 1 }
    [ShellWindow]::Click($button)
  }
  'close' { [ShellWindow]::Post($main, 0x0010, 0, 0) }
  default { Write-Error "Unknown command '$Command'"; exit 1 }
}
