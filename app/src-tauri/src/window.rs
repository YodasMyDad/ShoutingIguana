//! The main window: created hidden, restored to where it was, then shown; hidden to the tray and back.

use std::sync::atomic::Ordering;

use tauri::window::Color;
use tauri::{AppHandle, Manager, Theme, Url, WebviewUrl, WebviewWindowBuilder, Window, WindowEvent};
use tauri_plugin_opener::OpenerExt;
use tauri_plugin_store::StoreExt;
use tauri_plugin_window_state::{AppHandleExt, StateFlags};

use crate::live::notify;
use crate::shell::Shell;
use crate::sidecar::WEBVIEW_ORIGIN;
use crate::sync::lock;
use crate::{profile, quit};

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
    let dev_server = if cfg!(debug_assertions) { app.config().build.dev_url.clone() } else { None };
    let opener = app.clone();
    let mut builder = WebviewWindowBuilder::new(app, MAIN_WINDOW, WebviewUrl::default());
    if let Some(folder) = profile::webview_data() {
        builder = builder.data_directory(folder);
    }
    let window = builder
        .title("Shouting Iguana")
        .inner_size(1280.0, 800.0)
        .min_inner_size(960.0, 600.0)
        .center()
        .visible(false)
        // Each screen names itself ("Issues · garden… · Shouting Iguana", docs/v2-ui-ia.md): the window, the taskbar
        // and Alt+Tab say the page's title.
        .on_document_title_changed(|window, title| {
            let title = window_title(&title);
            if let Err(error) = window.set_title(&title) {
                log::debug!("Couldn't set the window's title: {error}");
            }
        })
        .on_navigation(move |url| match navigation(url, dev_server.as_ref()) {
            Navigation::Stay => true,
            Navigation::OpenInBrowser => {
                open_in_browser(&opener, url.as_str());
                false
            }
            Navigation::Refuse => {
                log::warn!("Kept the window from leaving the app for a {} URL", url.scheme());
                false
            }
        })
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

/// The page's title as the window's: the app's name when the page has none (as it loads), and never longer than a
/// title bar can use.
pub fn window_title(page: &str) -> String {
    const MAX_CHARS: usize = 200;
    let page = page.trim();
    if page.is_empty() || page.starts_with("http") || page.starts_with("tauri") {
        return crate::tray::text::APP_NAME.to_owned();
    }
    if page.chars().count() <= MAX_CHARS {
        return page.to_owned();
    }
    let mut short: String = page.chars().take(MAX_CHARS - 1).collect();
    short.push('…');
    short
}

/// Where the main window may go.
#[derive(Debug, PartialEq, Eq)]
pub enum Navigation {
    Stay,
    OpenInBrowser,
    Refuse,
}

/// The app's own pages (and the dev server's, in debug builds) load in the window. An http(s) link opens in the
/// browser instead, and anything else goes nowhere: the window never shows someone else's page in place of the app,
/// where the browser keys that could lead back are off.
pub fn navigation(url: &Url, dev_server: Option<&Url>) -> Navigation {
    let app = Url::parse(WEBVIEW_ORIGIN).ok();
    let is_app = app.iter().chain(dev_server).any(|origin| same_origin(url, origin));
    if is_app || url.as_str() == "about:blank" {
        Navigation::Stay
    } else if matches!(url.scheme(), "http" | "https") {
        Navigation::OpenInBrowser
    } else {
        Navigation::Refuse
    }
}

fn same_origin(url: &Url, origin: &Url) -> bool {
    url.scheme() == origin.scheme()
        && url.host_str() == origin.host_str()
        && url.port_or_known_default() == origin.port_or_known_default()
}

/// Off the UI thread: the browser can take a moment to start.
fn open_in_browser(app: &AppHandle, url: &str) {
    let (app, url) = (app.clone(), url.to_owned());
    let spawned = std::thread::Builder::new().name("open-link".to_owned()).spawn(move || {
        if let Err(error) = app.opener().open_url(url, None::<&str>) {
            log::warn!("Couldn't open a link in the browser: {error}");
        }
    });
    if let Err(error) = spawned {
        log::warn!("Couldn't start a thread to open a link: {error}");
    }
}

/// Closing the window hides it to the tray while a crawl or an analysis is live; otherwise it quits the app. Files
/// dragged onto it go to the page.
pub fn on_event(window: &Window, event: &WindowEvent) {
    if let WindowEvent::DragDrop(drag) = event {
        if window.label() == MAIN_WINDOW {
            crate::drop::forward(window.app_handle(), drag);
        }
        return;
    }
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
    let store = match app.store(profile::shell_store(SHELL_STORE)) {
        Ok(store) => store,
        Err(error) => {
            log::warn!("Couldn't open the shell's settings: {error}");
            return;
        }
    };
    if store.get(TRAY_NOTICE_SHOWN).and_then(|shown| shown.as_bool()) == Some(true) {
        return;
    }
    let Some(shell) = app.try_state::<Shell>() else { return };
    let (title, body) = crate::tray::text::tray_notice(lock(&shell.live).kinds(), shell.mode.get());
    notify::show(app, title, body);
    store.set(TRAY_NOTICE_SHOWN, true);
    if let Err(error) = store.save() {
        log::warn!("Couldn't save the shell's settings: {error}");
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn url(text: &str) -> Url {
        Url::parse(text).unwrap()
    }

    /// The review's case (L2): `location.href = 'https://example.com/'` replaced the app in its own window.
    #[test]
    fn the_window_stays_on_the_app_and_sends_web_links_to_the_browser() {
        let app = format!("{WEBVIEW_ORIGIN}/crawls/abc/urls?sort=-issues");
        assert_eq!(navigation(&url(&app), None), Navigation::Stay);
        assert_eq!(navigation(&url("about:blank"), None), Navigation::Stay);
        assert_eq!(navigation(&url("https://example.com/"), None), Navigation::OpenInBrowser);
        assert_eq!(navigation(&url("http://example.com/page"), None), Navigation::OpenInBrowser);
        for elsewhere in ["file:///C:/Windows/win.ini", "data:text/html,<p>x</p>", "javascript:alert(1)", "ftp://x/"] {
            assert_eq!(navigation(&url(elsewhere), None), Navigation::Refuse, "{elsewhere}");
        }
    }

    #[test]
    fn a_look_alike_origin_is_not_the_app() {
        let app = url(WEBVIEW_ORIGIN);
        let other_port = format!("{}://{}:8080/", app.scheme(), app.host_str().unwrap());
        let other_host = format!("{}://{}.example.com/", app.scheme(), app.host_str().unwrap());
        for elsewhere in [other_port, other_host] {
            assert_ne!(navigation(&url(&elsewhere), None), Navigation::Stay, "{elsewhere}");
        }
    }

    #[test]
    fn the_window_takes_the_pages_title() {
        assert_eq!(window_title("Issues · garden… · Shouting Iguana"), "Issues · garden… · Shouting Iguana");
        // WebView2 says the address until the page has a title.
        assert_eq!(window_title("http://tauri.localhost/"), "Shouting Iguana");
        assert_eq!(window_title("  "), "Shouting Iguana");
        let long = "a".repeat(500);
        assert_eq!(window_title(&long).chars().count(), 200);
        assert!(window_title(&long).ends_with('…'));
    }

    #[test]
    fn the_dev_server_is_the_app_only_when_given() {
        let dev = url("http://localhost:51874");
        assert_eq!(navigation(&url("http://localhost:51874/crawls"), Some(&dev)), Navigation::Stay);
        assert_eq!(navigation(&url("http://localhost:51874/crawls"), None), Navigation::OpenInBrowser);
        assert_eq!(navigation(&url("http://localhost:51875/"), Some(&dev)), Navigation::OpenInBrowser);
    }
}
