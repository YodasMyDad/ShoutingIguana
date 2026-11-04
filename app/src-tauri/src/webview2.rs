//! WebView2 settings Tauri doesn't expose, reached through webview2-com with the controller `with_webview` hands
//! over on the UI thread. Windows only: WKWebView and WebKitGTK have no equivalents.

use webview2_com::Microsoft::Web::WebView2::Win32::{
    ICoreWebView2ContextMenuRequestedEventArgs, ICoreWebView2Controller, ICoreWebView2Settings3, ICoreWebView2_11,
    ICoreWebView2_19, ICoreWebView2_3, COREWEBVIEW2_CONTEXT_MENU_ITEM_KIND,
    COREWEBVIEW2_CONTEXT_MENU_ITEM_KIND_SEPARATOR, COREWEBVIEW2_KEY_EVENT_KIND, COREWEBVIEW2_KEY_EVENT_KIND_KEY_DOWN,
    COREWEBVIEW2_KEY_EVENT_KIND_SYSTEM_KEY_DOWN, COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL,
    COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL_LOW, COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL_NORMAL,
};
use webview2_com::{
    take_pwstr, AcceleratorKeyPressedEventHandler, ContextMenuRequestedEventHandler, TrySuspendCompletedHandler,
};
use windows_core::{Interface, PWSTR};
use windows_sys::Win32::UI::Input::KeyboardAndMouse::{GetKeyState, VK_CONTROL, VK_F10, VK_MENU, VK_SHIFT};

use crate::context_menu::{self, MenuItem};

/// Once, as the window is created. `inspect` keeps Inspect in the context menu (debug builds).
pub fn configure(controller: &ICoreWebView2Controller, inspect: bool) {
    // SAFETY: called on the UI thread with the live controller, as `with_webview` guarantees.
    if let Err(error) = unsafe { disable_browser_keys(controller) } {
        log::warn!("Couldn't turn off the webview's browser keys: {error}");
    }
    if let Err(error) = unsafe { keep_keys_out_of_the_window_menu(controller) } {
        log::warn!("Couldn't keep F10 and Alt out of the window's menu: {error}");
    }
    if let Err(error) = unsafe { trim_context_menus(controller, inspect) } {
        log::warn!("Couldn't trim the webview's context menu: {error}");
    }
}

/// The app's keyboard registry owns Ctrl+F, Ctrl+P, Ctrl+R, F5, F3, Alt+Left and the rest, and zoom goes through
/// the webview's zoom API, so none of them may do their browser things.
unsafe fn disable_browser_keys(controller: &ICoreWebView2Controller) -> windows_core::Result<()> {
    unsafe {
        controller
            .CoreWebView2()?
            .Settings()?
            .cast::<ICoreWebView2Settings3>()?
            .SetAreBrowserAcceleratorKeysEnabled(false)
    }
}

/// With the browser keys off, every reserved key reaches the page and does nothing in WebView2 (F7's caret browsing,
/// Ctrl+S, Ctrl+J, Shift+Esc and the rest, checked with real key presses). Two keys the page leaves alone still do
/// something in Windows: F10, and a tap of Alt on its own, put the window in its menu mode, where the next key
/// pressed is lost. The app uses neither, so they stop here; Shift+F10, Alt+Space and Alt+F4 work as before.
unsafe fn keep_keys_out_of_the_window_menu(controller: &ICoreWebView2Controller) -> windows_core::Result<()> {
    let mut keys = MenuKeys::default();
    let handler = AcceleratorKeyPressedEventHandler::create(Box::new(move |_, args| {
        let Some(args) = args else { return Ok(()) };
        let mut kind = COREWEBVIEW2_KEY_EVENT_KIND::default();
        let mut key = 0;
        // SAFETY: WebView2 calls this on the UI thread with live arguments. The key states are this thread's, which
        // Windows keeps in step with the webview's window (its child).
        unsafe {
            args.KeyEventKind(&mut kind)?;
            args.VirtualKey(&mut key)?;
            let down =
                kind == COREWEBVIEW2_KEY_EVENT_KIND_KEY_DOWN || kind == COREWEBVIEW2_KEY_EVENT_KIND_SYSTEM_KEY_DOWN;
            let held = |key: u16| GetKeyState(i32::from(key)) < 0;
            if keys.opens_the_window_menu(key, down, held(VK_SHIFT) || held(VK_CONTROL)) {
                args.SetHandled(true)?;
            }
        }
        Ok(())
    }));
    let mut token = 0;
    unsafe { controller.add_AcceleratorKeyPressed(&handler, &mut token) }
}

/// Follows the keys to tell the two that would open the window's menu: F10 with neither Shift nor Ctrl, and Alt let
/// go with no other key pressed since it went down.
#[derive(Debug, Default)]
struct MenuKeys {
    alt_alone: bool,
}

impl MenuKeys {
    fn opens_the_window_menu(&mut self, key: u32, down: bool, shift_or_ctrl: bool) -> bool {
        let alt = key == u32::from(VK_MENU);
        match (alt, down) {
            (true, true) => {
                self.alt_alone = true;
                false
            }
            (true, false) => std::mem::take(&mut self.alt_alone),
            (false, true) => {
                self.alt_alone = false;
                key == u32::from(VK_F10) && !shift_or_ctrl
            }
            (false, false) => false,
        }
    }
}

unsafe fn trim_context_menus(controller: &ICoreWebView2Controller, inspect: bool) -> windows_core::Result<()> {
    let handler = ContextMenuRequestedEventHandler::create(Box::new(move |_, args| {
        if let Some(args) = args {
            // SAFETY: WebView2 calls this on the UI thread with live arguments.
            if let Err(error) = unsafe { trim(&args, inspect) } {
                log::debug!("Couldn't trim a context menu: {error}");
            }
        }
        Ok(())
    }));
    let mut token = 0;
    unsafe { controller.CoreWebView2()?.cast::<ICoreWebView2_11>()?.add_ContextMenuRequested(&handler, &mut token) }
}

unsafe fn trim(args: &ICoreWebView2ContextMenuRequestedEventArgs, inspect: bool) -> windows_core::Result<()> {
    unsafe {
        let items = args.MenuItems()?;
        let mut count = 0;
        items.Count(&mut count)?;
        let mut entries = Vec::with_capacity(count as usize);
        for index in 0..count {
            let item = items.GetValueAtIndex(index)?;
            let mut kind = COREWEBVIEW2_CONTEXT_MENU_ITEM_KIND::default();
            item.Kind(&mut kind)?;
            let mut name = PWSTR::null();
            item.Name(&mut name)?;
            entries.push((take_pwstr(name), kind == COREWEBVIEW2_CONTEXT_MENU_ITEM_KIND_SEPARATOR));
        }
        let menu: Vec<MenuItem<'_>> =
            entries.iter().map(|(name, separator)| MenuItem { name, separator: *separator }).collect();
        let keep = context_menu::kept(&menu, inspect);
        log::debug!(
            "Context menu: {}",
            entries
                .iter()
                .zip(&keep)
                .map(|((name, _), kept)| if *kept { name.clone() } else { format!("-{name}") })
                .collect::<Vec<_>>()
                .join(", ")
        );
        for index in (0..count).rev() {
            if !keep[index as usize] {
                items.RemoveValueAtIndex(index)?;
            }
        }
        if !keep.contains(&true) {
            // Nothing left to show: no menu at all, rather than an empty one.
            args.SetHandled(true)?;
        }
        Ok(())
    }
}

/// Hidden in the tray: WebView2 trims its processes' working sets, the page is told it isn't visible, and then it is
/// suspended, so it does no work at all: no rendering, no timers, no live updates paging memory back in. The shell's
/// own event stream keeps the tray and the notifications going meanwhile. Back from the tray, the page catches up
/// from the engine as after any reconnect.
pub fn enter_tray(controller: &ICoreWebView2Controller) {
    set_memory_target(controller, COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL_LOW);
    // SAFETY: on the UI thread, with the live controller.
    if let Err(error) = unsafe { controller.SetIsVisible(false) } {
        log::warn!("Couldn't tell the webview it is hidden: {error}");
        return;
    }
    let completed = TrySuspendCompletedHandler::create(Box::new(|result, suspended| {
        match (result, suspended) {
            (Ok(()), true) => log::info!("The page is suspended while the window is in the tray"),
            (Ok(()), false) => log::info!("The page couldn't be suspended: it stays hidden"),
            (Err(error), _) => log::warn!("Couldn't suspend the page: {error}"),
        }
        Ok(())
    }));
    // SAFETY: on the UI thread, with the live controller; the webview is hidden, as suspending needs.
    if let Err(error) = unsafe { webview_3(controller).and_then(|core| core.TrySuspend(&completed)) } {
        log::warn!("Couldn't suspend the page: {error}");
    }
}

/// Back from the tray, before the window shows.
pub fn leave_tray(controller: &ICoreWebView2Controller) {
    // SAFETY: on the UI thread, with the live controller.
    if let Err(error) = unsafe { webview_3(controller).and_then(|core| core.Resume()) } {
        log::warn!("Couldn't resume the page: {error}");
    }
    // SAFETY: on the UI thread, with the live controller.
    if let Err(error) = unsafe { controller.SetIsVisible(true) } {
        log::warn!("Couldn't tell the webview it is visible: {error}");
    }
    set_memory_target(controller, COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL_NORMAL);
}

/// The webview's interface for suspending and resuming the page.
unsafe fn webview_3(controller: &ICoreWebView2Controller) -> windows_core::Result<ICoreWebView2_3> {
    unsafe { controller.CoreWebView2()?.cast::<ICoreWebView2_3>() }
}

fn set_memory_target(controller: &ICoreWebView2Controller, level: COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL) {
    // SAFETY: on the UI thread, with the live controller.
    let result = unsafe {
        controller
            .CoreWebView2()
            .and_then(|core| core.cast::<ICoreWebView2_19>())
            .and_then(|core| core.SetMemoryUsageTargetLevel(level))
    };
    if let Err(error) = result {
        log::warn!("Couldn't set the webview's memory target: {error}");
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const F10: u32 = VK_F10 as u32;
    const ALT: u32 = VK_MENU as u32;
    const LEFT: u32 = 0x25;

    /// The case found with real key presses: F10 on its own, then a key, and the key was lost to the window's menu.
    #[test]
    fn f10_alone_is_kept_out_of_the_window_menu_but_shift_f10_is_not() {
        let mut keys = MenuKeys::default();
        assert!(keys.opens_the_window_menu(F10, true, false));
        assert!(!keys.opens_the_window_menu(F10, false, false));
        assert!(!keys.opens_the_window_menu(F10, true, true), "Shift+F10 opens the page's context menu");
    }

    #[test]
    fn only_a_tap_of_alt_on_its_own_is_kept_out_of_the_window_menu() {
        let mut keys = MenuKeys::default();
        assert!(!keys.opens_the_window_menu(ALT, true, false));
        assert!(!keys.opens_the_window_menu(ALT, true, false), "a held Alt repeats");
        assert!(keys.opens_the_window_menu(ALT, false, false));

        // Alt+Left (back) is a chord, not a tap: its Alt goes through.
        assert!(!keys.opens_the_window_menu(ALT, true, false));
        assert!(!keys.opens_the_window_menu(LEFT, true, false));
        assert!(!keys.opens_the_window_menu(LEFT, false, false));
        assert!(!keys.opens_the_window_menu(ALT, false, false));
    }
}
