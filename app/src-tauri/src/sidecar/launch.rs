//! How the sidecar is started: where its binary is, its arguments, and the environment it reads (see "Process (E1)"
//! in docs/v2-sidecar-api.md).

use std::ffi::OsString;
use std::io;
use std::path::{Path, PathBuf};

/// Tauri copies `externalBin` next to the app's own binary, without the target triple, in dev, build and bundles.
pub const BINARY_NAME: &str = if cfg!(windows) { "si-sidecar.exe" } else { "si-sidecar" };

pub const TOKEN_VARIABLE: &str = "SI_SIDECAR_TOKEN";
pub const ORIGINS_VARIABLE: &str = "SI_SIDECAR_ORIGINS";
pub const PARENT_VARIABLE: &str = "SI_PARENT_PID";

/// Set on the shell, these replace the sidecar's default folders: for tests and development, never in a release run
/// by a user.
pub const DATA_DIR_OVERRIDE: &str = "SI_APP_DATA_DIR";
pub const STATE_DIR_OVERRIDE: &str = "SI_APP_STATE_DIR";

/// Where a single-file .NET app unpacks the native libraries bundled in it (e_sqlite3): %TEMP%\.net by default.
pub const EXTRACT_DIR_VARIABLE: &str = "DOTNET_BUNDLE_EXTRACT_BASE_DIR";

/// The folder, in the app's local data folder, that holds one such folder per app version.
pub const EXTRACT_FOLDER: &str = "engine";

/// The origin this build's webview sends: Tauri's custom protocol as WebView2 and WebKit name it.
pub const WEBVIEW_ORIGIN: &str = if cfg!(windows) { "http://tauri.localhost" } else { "tauri://localhost" };

pub fn binary_path(shell_binary: &Path) -> Option<PathBuf> {
    shell_binary.parent().map(|folder| folder.join(BINARY_NAME))
}

/// 32 bytes from the operating system's random generator, as 64 hex characters: a new bearer token for each launch.
pub fn new_token() -> Result<String, getrandom::Error> {
    const HEX: &[u8; 16] = b"0123456789abcdef";
    let mut bytes = [0u8; 32];
    getrandom::fill(&mut bytes)?;
    Ok(bytes
        .iter()
        .flat_map(|byte| [HEX[usize::from(byte >> 4)], HEX[usize::from(byte & 0x0f)]])
        .map(char::from)
        .collect())
}

/// Exactly this build's webview origin, plus the dev server's when there is one (debug builds only).
pub fn allowed_origins(dev_server: Option<&tauri::Url>) -> String {
    let dev_origin = if cfg!(debug_assertions) { dev_server.and_then(origin_of) } else { None };
    join_origins(WEBVIEW_ORIGIN, dev_origin)
}

fn origin_of(url: &tauri::Url) -> Option<String> {
    let origin = url.origin();
    origin.is_tuple().then(|| origin.ascii_serialization())
}

fn join_origins(webview: &str, dev_server: Option<String>) -> String {
    match dev_server {
        Some(dev_server) if dev_server != webview => format!("{webview},{dev_server}"),
        _ => webview.to_owned(),
    }
}

/// `--stdin-lifeline` always: the shell holds stdin, so the sidecar stops when the shell goes, however it goes.
pub fn arguments(data_dir: Option<OsString>, state_dir: Option<OsString>) -> Vec<OsString> {
    let mut arguments = vec![OsString::from("--stdin-lifeline")];
    for (option, folder) in [("--data-dir", data_dir), ("--state-dir", state_dir)] {
        if let Some(folder) = folder {
            arguments.push(option.into());
            arguments.push(folder);
        }
    }
    arguments
}

/// A folder override from the shell's environment; an empty value counts as unset.
pub fn folder_override(variable: &str) -> Option<OsString> {
    std::env::var_os(variable).filter(|value| !value.is_empty())
}

/// Makes `root/<version>` for this version's engine to unpack into, and removes the folders other versions left in
/// `root`: only one copy of the app runs (single instance), so no engine is using them.
pub fn prepare_extraction_folder(root: &Path, version: &str) -> io::Result<PathBuf> {
    let folder = root.join(version);
    std::fs::create_dir_all(&folder)?;
    for entry in std::fs::read_dir(root)?.flatten() {
        let stale = entry.file_name() != version && entry.file_type().is_ok_and(|kind| kind.is_dir());
        if stale {
            match std::fs::remove_dir_all(entry.path()) {
                Ok(()) => log::info!("Removed the engine files of another version: {}", entry.path().display()),
                Err(error) => log::debug!("Couldn't remove {}: {error}", entry.path().display()),
            }
        }
    }
    Ok(folder)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn tokens_are_64_hex_characters_and_never_repeat() {
        let first = new_token().unwrap();
        let second = new_token().unwrap();
        assert_eq!(first.len(), 64);
        assert!(first.bytes().all(|byte| byte.is_ascii_hexdigit() && !byte.is_ascii_uppercase()), "{first}");
        assert_ne!(first, second);
    }

    #[test]
    fn the_dev_server_origin_is_scheme_host_and_port() {
        let url = tauri::Url::parse("http://localhost:51874/index.html?x=1").unwrap();
        assert_eq!(origin_of(&url).as_deref(), Some("http://localhost:51874"));
        let default_port = tauri::Url::parse("http://localhost/").unwrap();
        assert_eq!(origin_of(&default_port).as_deref(), Some("http://localhost"));
    }

    #[test]
    fn origins_are_the_webview_then_the_dev_server() {
        assert_eq!(join_origins("http://tauri.localhost", None), "http://tauri.localhost");
        assert_eq!(
            join_origins("http://tauri.localhost", Some("http://localhost:51874".into())),
            "http://tauri.localhost,http://localhost:51874"
        );
        assert_eq!(join_origins("tauri://localhost", Some("tauri://localhost".into())), "tauri://localhost");
    }

    #[test]
    fn this_build_names_its_own_webview_origin() {
        let expected = if cfg!(windows) { "http://tauri.localhost" } else { "tauri://localhost" };
        assert!(allowed_origins(None).starts_with(expected));
    }

    #[test]
    fn arguments_always_ask_for_the_lifeline() {
        assert_eq!(arguments(None, None), ["--stdin-lifeline"]);
        assert_eq!(
            arguments(Some("C:\\data dir".into()), Some("C:\\state".into())),
            ["--stdin-lifeline", "--data-dir", "C:\\data dir", "--state-dir", "C:\\state"]
        );
        assert_eq!(arguments(None, Some("/tmp/state".into())), ["--stdin-lifeline", "--state-dir", "/tmp/state"]);
    }

    /// The review's case (L9): every build used to leave its own unpacked libraries in %TEMP%, forever.
    #[test]
    fn the_engine_unpacks_into_a_folder_for_this_version_and_other_versions_go() {
        let root = std::env::temp_dir().join(format!("si-extract-test-{}", std::process::id()));
        let old = root.join("2.0.0-alpha.0").join("si-sidecar").join("ABC123");
        std::fs::create_dir_all(&old).unwrap();
        std::fs::write(old.join("e_sqlite3.dll"), b"old").unwrap();
        std::fs::write(root.join("note.txt"), b"not a folder").unwrap();

        let folder = prepare_extraction_folder(&root, "2.0.0-alpha.1").unwrap();
        assert_eq!(folder, root.join("2.0.0-alpha.1"));
        assert!(folder.is_dir());
        assert!(!root.join("2.0.0-alpha.0").exists());
        assert!(root.join("note.txt").is_file(), "only folders are removed");

        // Again for the same version: its folder and what is in it stay.
        std::fs::write(folder.join("kept"), b"x").unwrap();
        assert_eq!(prepare_extraction_folder(&root, "2.0.0-alpha.1").unwrap(), folder);
        assert!(folder.join("kept").is_file());
        std::fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn the_binary_sits_next_to_the_shell() {
        let folder = Path::new("apps").join("Shouting Iguana");
        assert_eq!(binary_path(&folder.join("shouting-iguana")), Some(folder.join(BINARY_NAME)));
    }
}
