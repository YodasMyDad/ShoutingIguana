//! The commands the webview may call. Each has a permission (see build.rs) granted to the main window only. None
//! takes a path: the page names a crawl, or the shell's own file, never a place on disk.

use std::sync::Arc;

use serde::Serialize;
use tauri::{AppHandle, Manager, State};
use tauri_plugin_opener::OpenerExt;

use crate::shell::Shell;
use crate::sidecar::SidecarStatePayload;
use crate::sync::lock;
use crate::{profile, settings};

/// The engine's current state. The webview listens to `sidecar://state` first, then asks, and keeps the higher `seq`.
#[tauri::command]
pub fn sidecar_state(shell: State<'_, Shell>) -> SidecarStatePayload {
    shell.sidecar.state()
}

/// "Try again" after the engine failed. Does nothing in any other state.
#[tauri::command]
pub fn restart_sidecar(shell: State<'_, Shell>) {
    shell.sidecar.retry();
}

/// The palette's Quit: the same flow as the tray's.
#[tauri::command]
pub fn request_quit(app: AppHandle) {
    crate::quit::request(&app);
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ShellInfo {
    app_version: String,
    platform: &'static str,
    debug: bool,
    log_dir: String,
}

#[tauri::command]
pub fn shell_info(app: AppHandle) -> ShellInfo {
    let platform = if cfg!(target_os = "windows") {
        "windows"
    } else if cfg!(target_os = "macos") {
        "macos"
    } else {
        "linux"
    };
    ShellInfo {
        app_version: app.package_info().version.to_string(),
        platform,
        debug: cfg!(debug_assertions),
        log_dir: app.path().app_log_dir().map(|folder| folder.to_string_lossy().into_owned()).unwrap_or_default(),
    }
}

/// The `.sicrawl` files waiting to be opened: from the command line, and from every launch since. Each is handed
/// over once. `shell://open-crawl-files` says more have arrived; the page takes them when the engine can open them.
#[tauri::command]
pub fn take_pending_open_files(shell: State<'_, Shell>) -> Vec<String> {
    lock(&shell.open_files).take()
}

/// The page's preferences, or null when there are none yet. Async, so the file is read off the main thread.
#[tauri::command]
pub async fn settings_read(app: AppHandle) -> Option<serde_json::Value> {
    settings::read(&profile::settings_folder(&app)?.join(settings::FILE_NAME))
}

/// Stores the page's preferences: a JSON object, as text, of at most 64 KB.
#[tauri::command]
pub async fn settings_write(app: AppHandle, settings: String) -> Result<(), String> {
    let folder = profile::settings_folder(&app).ok_or("The app has no folder for its preferences.")?;
    settings::write(&folder.join(settings::FILE_NAME), &settings).map_err(|error| {
        log::warn!("Couldn't store the preferences: {error}");
        error.to_string()
    })
}

/// Shows a library crawl's file in Explorer or Finder. The page names the crawl and the engine's library says where
/// its file is, so the page can't reveal, or probe for, anything else on disk.
#[tauri::command]
pub async fn reveal_crawl_file(app: AppHandle, crawl_id: String) -> Result<(), String> {
    const NO_ENGINE: &str = "The engine isn't running.";
    let shell = app.try_state::<Shell>().ok_or(NO_ENGINE)?;
    let connection = shell.sidecar.link().current().ok_or(NO_ENGINE)?;
    let api = Arc::clone(&shell.api);
    let item = tauri::async_runtime::spawn_blocking(move || api.crawl(&connection, &crawl_id))
        .await
        .map_err(|error| error.to_string())?
        .map_err(|error| format!("The crawl couldn't be found: {error}"))?;
    if item.path.is_empty() {
        return Err("The engine didn't say where the crawl's file is.".to_owned());
    }
    app.opener().reveal_item_in_dir(&item.path).map_err(|error| {
        log::info!("Couldn't show crawl {} in its folder: {error}", item.id);
        error.to_string()
    })
}
