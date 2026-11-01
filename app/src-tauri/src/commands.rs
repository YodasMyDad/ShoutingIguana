//! The commands the webview may call. Each has a permission (see build.rs) granted to the main window only.

use serde::Serialize;
use tauri::{AppHandle, Manager, State};

use crate::shell::Shell;
use crate::sidecar::SidecarStatePayload;
use crate::sync::lock;

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

/// The `.sicrawl` files the app was launched with, once: later ones arrive as `shell://open-crawl-files`.
#[tauri::command]
pub fn take_pending_open_files(shell: State<'_, Shell>) -> Vec<String> {
    lock(&shell.open_files).take()
}
