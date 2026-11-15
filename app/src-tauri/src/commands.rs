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
        log_dir: app_log_folder(&app).map(|folder| folder.to_string_lossy().into_owned()).unwrap_or_default(),
    }
}

/// The shell's own log folder: the test run's, or the log plugin's.
fn app_log_folder(app: &AppHandle) -> Option<std::path::PathBuf> {
    profile::log_folder().or_else(|| app.path().app_log_dir().ok())
}

/// Settings › Diagnostics' Open logs folder: `app`, the shell's log, or `engine`, the engine's (its state folder's
/// `logs`, as the engine says). The page names which, never a path.
#[tauri::command]
pub async fn open_logs_folder(app: AppHandle, which: String) -> Result<(), String> {
    let folder = match which.as_str() {
        "app" => app_log_folder(&app).ok_or("The app has no log folder.")?,
        "engine" => {
            const NO_ENGINE: &str = "The engine isn't running.";
            let shell = app.try_state::<Shell>().ok_or(NO_ENGINE)?;
            let connection = shell.sidecar.link().current().ok_or(NO_ENGINE)?;
            let api = Arc::clone(&shell.api);
            let state = tauri::async_runtime::spawn_blocking(move || api.state_dir(&connection))
                .await
                .map_err(|error| error.to_string())?
                .map_err(|error| format!("The engine didn't say where its log is: {error}"))?;
            if state.is_empty() {
                return Err("The engine didn't say where its log is.".to_owned());
            }
            std::path::Path::new(&state).join("logs")
        }
        _ => return Err("There's no such log.".to_owned()),
    };
    // The folder may not exist yet (nothing logged): make it, so Explorer opens it rather than an error.
    if let Err(error) = std::fs::create_dir_all(&folder) {
        log::debug!("Couldn't make the log folder {}: {error}", folder.display());
    }
    app.opener().open_path(folder.to_string_lossy(), None::<&str>).map_err(|error| {
        log::info!("Couldn't open the log folder: {error}");
        format!("The folder couldn't be opened: {error}")
    })
}

/// Settings › Updates: is there a newer version, and what's new in it.
#[tauri::command]
pub async fn update_check(app: AppHandle) -> Result<crate::updates::UpdateCheck, crate::updates::CheckFailed> {
    crate::updates::check(&app).await
}

/// Installs the update the last check found, if it is still `version`: the app closes and the installer runs.
#[tauri::command]
pub async fn update_install(app: AppHandle, version: String) -> Result<(), String> {
    crate::updates::install(&app, &version).await
}

/// The secrets kept for the crawl defaults (`defaults`) or a crawl (`crawl:<id>`), or null.
#[tauri::command]
pub async fn secrets_read(key: String) -> Result<Option<String>, String> {
    tauri::async_runtime::spawn_blocking(move || crate::secrets::read(&key))
        .await
        .map_err(|error| error.to_string())?
        .map_err(|error| {
            log::warn!("Couldn't read secrets from the credential store: {error}");
            error.to_string()
        })
}

/// Keeps secrets (a JSON object, as text) for `key` in the credential store; null forgets them.
#[tauri::command]
pub async fn secrets_write(key: String, secrets: Option<String>) -> Result<(), String> {
    tauri::async_runtime::spawn_blocking(move || crate::secrets::write(&key, secrets.as_deref()))
        .await
        .map_err(|error| error.to_string())?
        .map_err(|error| {
            log::warn!("Couldn't keep secrets in the credential store: {error}");
            error.to_string()
        })
}

/// The data folder the user chose, and the one before it.
#[tauri::command]
pub fn data_folder_choice(app: AppHandle) -> crate::data_folder::DataFolderChoice {
    crate::data_folder::choice(&app)
}

/// Asks for a folder with the system's own dialog and, if one is chosen, keeps the crawls there from now on. Null when
/// the dialog was cancelled.
#[tauri::command]
pub async fn data_folder_choose(app: AppHandle) -> Result<Option<crate::data_folder::DataFolderChoice>, String> {
    use tauri_plugin_dialog::DialogExt;
    let picker = app.clone();
    let chosen = tauri::async_runtime::spawn_blocking(move || {
        picker.dialog().file().set_title("Where to keep crawls").blocking_pick_folder()
    })
    .await
    .map_err(|error| error.to_string())?;
    let Some(folder) = chosen.and_then(|folder| folder.into_path().ok()) else { return Ok(None) };
    tauri::async_runtime::spawn_blocking(move || crate::data_folder::switch(&app, Some(folder)).map(Some))
        .await
        .map_err(|error| error.to_string())?
}

/// Goes back to the folder in use before the last change (`previous`), or to the default (`default`).
#[tauri::command]
pub async fn data_folder_reset(app: AppHandle, to: String) -> Result<crate::data_folder::DataFolderChoice, String> {
    let folder = match to.as_str() {
        "default" => None,
        "previous" => {
            let choice = crate::data_folder::choice(&app);
            if !choice.has_previous {
                return Err("There's no folder to go back to.".to_owned());
            }
            choice.previous.map(std::path::PathBuf::from)
        }
        _ => return Err("There's no such folder.".to_owned()),
    };
    tauri::async_runtime::spawn_blocking(move || crate::data_folder::switch(&app, folder))
        .await
        .map_err(|error| error.to_string())?
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
    })?;
    // The page tells the shell its mode this way: the tray, the notices and the quit question speak it from now on.
    let stored = serde_json::from_str::<serde_json::Value>(&settings).ok();
    let mode = crate::mode::Mode::of(settings::preferences(stored.as_ref()).simple);
    if let Some(shell) = app.try_state::<Shell>() {
        if shell.mode.set(mode) {
            log::info!("The page is in {mode:?} mode: the shell's words follow");
            shell.tray.changed(true);
        }
    }
    Ok(())
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
