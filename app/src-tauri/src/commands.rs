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

/// The extensions an export writes (the engine refuses any other): the only files Open file opens.
const EXPORT_EXTENSIONS: &[&str] = &["csv", "xlsx", "html", "htm"];

/// The file a finished export wrote, as the engine says: the page names the export, never a path, so it can open or
/// reveal only what an export of this engine wrote, and only once it is whole.
async fn exported_file(app: &AppHandle, export_id: String) -> Result<String, String> {
    const NO_ENGINE: &str = "The engine isn't running.";
    let shell = app.try_state::<Shell>().ok_or(NO_ENGINE)?;
    let connection = shell.sidecar.link().current().ok_or(NO_ENGINE)?;
    let api = Arc::clone(&shell.api);
    let job = tauri::async_runtime::spawn_blocking(move || api.export(&connection, &export_id))
        .await
        .map_err(|error| error.to_string())?
        .map_err(|error| format!("The export couldn't be found: {error}"))?;
    exported_path(&job)
}

/// The path of an export that left a file of a kind exports write, or why there's none to open.
fn exported_path(job: &crate::live::model::ExportJob) -> Result<String, String> {
    if job.status != "done" {
        return Err("The export didn't save a file.".to_owned());
    }
    let extension = std::path::Path::new(&job.path)
        .extension()
        .and_then(|extension| extension.to_str())
        .map(str::to_ascii_lowercase)
        .unwrap_or_default();
    if job.path.is_empty() || !EXPORT_EXTENSIONS.contains(&extension.as_str()) {
        return Err("The engine didn't say where the export's file is.".to_owned());
    }
    Ok(job.path.clone())
}

/// Shows a finished export's file in Explorer or Finder. Takes the export's id: the engine says where its file is.
#[tauri::command]
pub async fn reveal_export_file(app: AppHandle, export_id: String) -> Result<(), String> {
    let path = exported_file(&app, export_id).await?;
    app.opener().reveal_item_in_dir(&path).map_err(|error| {
        log::info!("Couldn't show an export's file in its folder: {error}");
        error.to_string()
    })
}

/// Opens a finished export's file in the program the system gives its kind (a spreadsheet, a browser). Takes the
/// export's id: the engine says where its file is, and only a CSV, Excel or HTML file an export wrote is opened.
#[tauri::command]
pub async fn open_export_file(app: AppHandle, export_id: String) -> Result<(), String> {
    let path = exported_file(&app, export_id).await?;
    app.opener().open_path(&path, None::<&str>).map_err(|error| {
        log::info!("Couldn't open an export's file: {error}");
        error.to_string()
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::live::model::ExportJob;

    fn job(status: &str, path: &str) -> ExportJob {
        ExportJob {
            export_id: "3f9a61c2d0b4e87a".into(),
            status: status.into(),
            path: path.into(),
            ..ExportJob::default()
        }
    }

    #[test]
    fn only_a_finished_exports_own_kind_of_file_is_opened() {
        assert_eq!(exported_path(&job("done", r"C:\a\b.XLSX")), Ok(r"C:\a\b.XLSX".to_owned()));
        assert!(exported_path(&job("done", "/home/me/report.htm")).is_ok());
        for status in ["running", "failed", "cancelled"] {
            assert!(exported_path(&job(status, r"C:\a\b.csv")).is_err(), "{status}");
        }
        for path in ["", r"C:\a\b.exe", r"C:\a\b", r"C:\a\b.csv.lnk", r"C:\a\b.bat"] {
            assert!(exported_path(&job("done", path)).is_err(), "{path}");
        }
    }
}
