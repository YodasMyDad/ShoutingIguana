//! The main window: created hidden, restored to where it was, then shown; hidden to the tray and back.

use std::sync::atomic::Ordering;

use tauri::window::Color;
use tauri::{AppHandle, Manager, Theme, WebviewUrl, WebviewWindowBuilder, Window, WindowEvent};
use tauri_plugin_store::StoreExt;
use tauri_plugin_window_state::{AppHandleExt, StateFlags};

use crate::live::notify;
use crate::quit;
use crate::shell::Shell;
use crate::sync::lock;

pub const MAIN_WINDOW: &str = "main";

const LIGHT_BACKGROUND: Color = Color(0xff, 0xff, 0xff, 0xff);
const DARK_BACKGROUND: Color = Color(0x16, 0x18, 0x1d, 0xff);

/// The shell's own settings, beside the webview's stores in the app's data folder.
const SHELL_STORE: &str = "shell.json";
const TRAY_NOTICE_SHOWN: &str = "trayNoticeShown";

/// Size, position and maximised are remembered. Visibility never is, so a window that was hidden in the tray when
/// the app quit comes back shown.
pub fn state_flags() -> StateFlags {
    StateFlags::SIZE | StateFlags::POSITION | StateFlags::MAXIMIZED
}

pub fn create(app: &AppHandle) -> tauri::Result<()> {
    let window = WebviewWindowBuilder::new(app, MAIN_WINDOW, WebviewUrl::default())
        .title("Shouting Iguana")
        .inner_size(1280.0, 800.0)
        .min_inner_size(960.0, 600.0)
        .center()
        .visible(false)
        .build()?;
    // The window-state plugin restored the window as it was created. Before it shows, the OS theme's background,
    // so the window never flashes white in dark mode.
    let dark = window.theme().is_ok_and(|theme| theme == Theme::Dark);
    if let Err(error) = window.set_background_color(Some(if dark { DARK_BACKGROUND } else { LIGHT_BACKGROUND })) {
        log::warn!("Couldn't set the window's background: {error}");
    }
    #[cfg(windows)]
    {
        let inspect = cfg!(debug_assertions);
        if let Err(error) =
            window.with_webview(move |webview| crate::webview2::configure(&webview.controller(), inspect))
        {
            log::warn!("Couldn't configure the webview: {error}");
        }
    }
    window.show()?;
    if let Err(error) = window.set_focus() {
        log::debug!("Couldn't focus the window: {error}");
    }
    Ok(())
}

/// Closing the window hides it to the tray while a crawl or an analysis is live; otherwise it quits the app.
pub fn on_event(window: &Window, event: &WindowEvent) {
    let WindowEvent::CloseRequested { api, .. } = event else { return };
    if window.label() != MAIN_WINDOW {
        return;
    }
    api.prevent_close();
    let app = window.app_handle();
    let Some(shell) = app.try_state::<Shell>() else { return };
    if shell.is_quitting() {
        return;
    }
    let live = lock(&shell.live).kinds().any();
    if live {
        hide_to_tray(app);
    } else {
        quit::request(app);
    }
}

pub fn hide_to_tray(app: &AppHandle) {
    let Some(window) = app.get_webview_window(MAIN_WINDOW) else { return };
    if let Err(error) = app.save_window_state(state_flags()) {
        log::warn!("Couldn't save the window's size and position: {error}");
    }
    if let Err(error) = window.hide() {
        log::warn!("Couldn't hide the window: {error}");
        return;
    }
    #[cfg(windows)]
    if let Err(error) = window.with_webview(|webview| crate::webview2::enter_tray(&webview.controller())) {
        log::warn!("Couldn't lower the webview's footprint: {error}");
    }
    if let Some(shell) = app.try_state::<Shell>() {
        shell.window_hidden.store(true, Ordering::SeqCst);
        shell.tray.changed(false);
    }
    log::info!("The window is hidden in the tray");
    first_time_in_tray(app);
}

/// Hides the window without the tray: the app is quitting with nothing to stop.
pub fn hide(app: &AppHandle) {
    if let Some(window) = app.get_webview_window(MAIN_WINDOW) {
        if let Err(error) = window.hide() {
            log::debug!("Couldn't hide the window: {error}");
        }
    }
}

/// Shows, restores and focuses the window, from the tray if it was hidden there.
pub fn show(app: &AppHandle) {
    let Some(window) = app.get_webview_window(MAIN_WINDOW) else { return };
    // The webview is visible again before the window is, so the page renders as it appears.
    #[cfg(windows)]
    if let Err(error) = window.with_webview(|webview| crate::webview2::leave_tray(&webview.controller())) {
        log::warn!("Couldn't restore the webview: {error}");
    }
    for result in [window.show(), window.unminimize(), window.set_focus()] {
        if let Err(error) = result {
            log::debug!("Couldn't bring the window forward: {error}");
        }
    }
    if let Some(shell) = app.try_state::<Shell>() {
        if shell.window_hidden.swap(false, Ordering::SeqCst) {
            shell.tray.changed(false);
            log::info!("The window is back from the tray");
        }
    }
}

/// The window is where the user is looking, so it shows its own toasts.
pub fn is_in_front(app: &AppHandle) -> bool {
    app.get_webview_window(MAIN_WINDOW).is_some_and(|window| {
        window.is_visible().unwrap_or(false)
            && !window.is_minimized().unwrap_or(false)
            && window.is_focused().unwrap_or(false)
    })
}

/// The first time ever the window hides to the tray, say so.
fn first_time_in_tray(app: &AppHandle) {
    let store = match app.store(SHELL_STORE) {
        Ok(store) => store,
        Err(error) => {
            log::warn!("Couldn't open the shell's settings: {error}");
            return;
        }
    };
    if store.get(TRAY_NOTICE_SHOWN).and_then(|shown| shown.as_bool()) == Some(true) {
        return;
    }
    notify::show(app, "Still crawling", "Shouting Iguana keeps crawling in the tray. Quit from its menu.");
    store.set(TRAY_NOTICE_SHOWN, true);
    if let Err(error) = store.save() {
        log::warn!("Couldn't save the shell's settings: {error}");
    }
}
