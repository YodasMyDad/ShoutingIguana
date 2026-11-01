//! WebView2 settings Tauri doesn't expose, reached through webview2-com with the controller `with_webview` hands
//! over on the UI thread. Windows only: WKWebView and WebKitGTK have no equivalents.

use webview2_com::Microsoft::Web::WebView2::Win32::{
    ICoreWebView2ContextMenuRequestedEventArgs, ICoreWebView2Controller, ICoreWebView2Settings3, ICoreWebView2_11,
    ICoreWebView2_19, COREWEBVIEW2_CONTEXT_MENU_ITEM_KIND, COREWEBVIEW2_CONTEXT_MENU_ITEM_KIND_SEPARATOR,
    COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL, COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL_LOW,
    COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL_NORMAL,
};
use webview2_com::{take_pwstr, ContextMenuRequestedEventHandler};
use windows_core::{Interface, PWSTR};

use crate::context_menu::{self, MenuItem};

/// Once, as the window is created. `inspect` keeps Inspect in the context menu (debug builds).
pub fn configure(controller: &ICoreWebView2Controller, inspect: bool) {
    // SAFETY: called on the UI thread with the live controller, as `with_webview` guarantees.
    if let Err(error) = unsafe { disable_browser_keys(controller) } {
        log::warn!("Couldn't turn off the webview's browser keys: {error}");
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

/// Hidden in the tray: WebView2 trims its processes' working sets, and the page, told it isn't visible, stops
/// rendering and slows its timers (docs/v2-ui-spike.md, "Hidden in the tray").
pub fn enter_tray(controller: &ICoreWebView2Controller) {
    set_memory_target(controller, COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL_LOW);
    // SAFETY: on the UI thread, with the live controller.
    if let Err(error) = unsafe { controller.SetIsVisible(false) } {
        log::warn!("Couldn't tell the webview it is hidden: {error}");
    }
}

/// Back from the tray, before the window shows.
pub fn leave_tray(controller: &ICoreWebView2Controller) {
    // SAFETY: on the UI thread, with the live controller.
    if let Err(error) = unsafe { controller.SetIsVisible(true) } {
        log::warn!("Couldn't tell the webview it is visible: {error}");
    }
    set_memory_target(controller, COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL_NORMAL);
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
