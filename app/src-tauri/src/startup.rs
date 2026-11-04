//! When the app can't start at all (a broken WebView2 runtime, a profile folder it can't write): the error goes to
//! the log, and a native message box says so and where the log is. A release build has no console, so without it the
//! app would just never appear.

use std::fmt::Display;
use std::path::{Path, PathBuf};

const TITLE: &str = "Shouting Iguana";

pub fn failed(identifier: &str, error: &impl Display) -> ! {
    log::error!("Shouting Iguana couldn't start: {error}");
    show(&message(error, log_folder(identifier).as_deref()));
    std::process::exit(1);
}

fn message(error: &impl Display, log_folder: Option<&Path>) -> String {
    let mut text = format!("Shouting Iguana couldn't start.\n\n{error}");
    if let Some(folder) = log_folder {
        text.push_str(&format!("\n\nThe app's log is in {}", folder.display()));
    }
    text
}

/// Where the log plugin writes, resolved as Tauri's `app_log_dir` does: there's no app to ask.
fn log_folder(identifier: &str) -> Option<PathBuf> {
    let home = || std::env::var_os("HOME").filter(|home| !home.is_empty()).map(PathBuf::from);
    if cfg!(target_os = "macos") {
        return home().map(|home| home.join("Library").join("Logs").join(identifier));
    }
    let local_data = if cfg!(windows) {
        std::env::var_os("LOCALAPPDATA").filter(|folder| !folder.is_empty()).map(PathBuf::from)
    } else {
        std::env::var_os("XDG_DATA_HOME")
            .filter(|folder| !folder.is_empty())
            .map(PathBuf::from)
            .or_else(|| home().map(|home| home.join(".local").join("share")))
    };
    local_data.map(|folder| folder.join(identifier).join("logs"))
}

#[cfg(windows)]
fn show(text: &str) {
    use windows_sys::Win32::UI::WindowsAndMessaging::{MessageBoxW, MB_ICONERROR, MB_OK, MB_SETFOREGROUND};
    let wide = |text: &str| text.encode_utf16().chain(std::iter::once(0)).collect::<Vec<u16>>();
    let (text, title) = (wide(text), wide(TITLE));
    // SAFETY: both strings are NUL-terminated and outlive the call; there is no owner window.
    unsafe {
        MessageBoxW(std::ptr::null_mut(), text.as_ptr(), title.as_ptr(), MB_OK | MB_ICONERROR | MB_SETFOREGROUND)
    };
}

#[cfg(not(windows))]
fn show(text: &str) {
    eprintln!("{TITLE}: {text}");
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_message_gives_the_error_and_where_the_log_is() {
        let folder = Path::new("C:\\Users\\me\\AppData\\Local\\uk.co.aptitude.shoutingiguana\\logs");
        assert_eq!(
            message(&"WebView2 couldn't be found", Some(folder)),
            "Shouting Iguana couldn't start.\n\nWebView2 couldn't be found\n\nThe app's log is in C:\\Users\\me\\AppData\\Local\\uk.co.aptitude.shoutingiguana\\logs"
        );
        assert_eq!(message(&"boom", None), "Shouting Iguana couldn't start.\n\nboom");
    }

    /// The same folder as the log plugin's: `app_log_dir`, which is `%LOCALAPPDATA%\<identifier>\logs` on Windows.
    #[cfg(windows)]
    #[test]
    fn the_log_folder_is_the_log_plugins() {
        let local = PathBuf::from(std::env::var_os("LOCALAPPDATA").unwrap());
        assert_eq!(
            log_folder("uk.co.aptitude.shoutingiguana"),
            Some(local.join("uk.co.aptitude.shoutingiguana").join("logs"))
        );
    }
}
