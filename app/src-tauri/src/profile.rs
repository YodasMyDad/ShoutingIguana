//! Where the shell keeps its per-user files: the app's own folders, or, for a test run of the built app (the smoke
//! test, the orphan check), a folder of its own named by `SI_APP_PROFILE_DIR`. A test run then never touches the user's
//! own copy of the app: its preferences, window position, webview profile, unpacked engine files and log all go there,
//! and it stays out of single instance, so it runs beside the user's open app. Never set for a user.

use std::path::PathBuf;

use tauri::{AppHandle, Manager};

pub const PROFILE_OVERRIDE: &str = "SI_APP_PROFILE_DIR";

/// The test run's folder, if this is one; an empty value counts as unset.
pub fn test_profile() -> Option<PathBuf> {
    std::env::var_os(PROFILE_OVERRIDE).filter(|value| !value.is_empty()).map(PathBuf::from)
}

/// The folder for the page's preferences (`settings.json`).
pub fn settings_folder(app: &AppHandle) -> Option<PathBuf> {
    test_profile().or_else(|| app.path().app_config_dir().ok())
}

/// The folder the engine unpacks its native libraries in, one subfolder per app version.
pub fn local_data_folder(app: &AppHandle) -> Option<PathBuf> {
    test_profile().or_else(|| app.path().app_local_data_dir().ok())
}

/// The shell's own settings, for the store plugin: a name in the app's data folder, or a path in the test's.
pub fn shell_store(name: &str) -> PathBuf {
    test_profile().map_or_else(|| PathBuf::from(name), |profile| profile.join(name))
}

/// The window-state plugin's file: its own default name in the app's config folder, or a path in the test's.
pub fn window_state_file() -> String {
    let name = tauri_plugin_window_state::DEFAULT_FILENAME;
    test_profile().map_or_else(|| name.to_owned(), |profile| profile.join(name).to_string_lossy().into_owned())
}

/// The webview's profile folder, for a test run only: otherwise Tauri's default, in the app's local data folder.
pub fn webview_data() -> Option<PathBuf> {
    test_profile().map(|profile| profile.join("webview"))
}

/// The shell's log folder, for a test run only: otherwise the log plugin's default, the app's log folder.
pub fn log_folder() -> Option<PathBuf> {
    test_profile().map(|profile| profile.join("logs"))
}
