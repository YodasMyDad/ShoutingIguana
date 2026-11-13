//! The page's preferences: one JSON object in `settings.json`, in the app's config folder. The page reads and writes
//! them through two commands bound to that one file, so it gets no file system access of its own (the store plugin
//! took any path it was given). The file keeps the shape the store plugin wrote, `{"settings": {...}}`.

use std::fmt;
use std::fs;
use std::io;
use std::path::{Path, PathBuf};
use std::sync::Mutex;

use serde_json::{Map, Value};

use crate::sync::lock;

pub const FILE_NAME: &str = "settings.json";

/// Far more than the preferences need (about 1 KB), and small enough that nothing can grow the file without bound.
pub const MAX_BYTES: usize = 64 * 1024;

const KEY: &str = "settings";

/// One read or write at a time: two writes racing could otherwise leave the older one on disk.
static FILE: Mutex<()> = Mutex::new(());

#[derive(Debug)]
pub enum SettingsError {
    TooLarge(usize),
    NotJson(serde_json::Error),
    NotAnObject,
    Io(io::Error),
}

impl fmt::Display for SettingsError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::TooLarge(bytes) => {
                write!(formatter, "the settings are {bytes} bytes, over the {MAX_BYTES}-byte limit")
            }
            Self::NotJson(error) => write!(formatter, "the settings aren't JSON: {error}"),
            Self::NotAnObject => formatter.write_str("the settings aren't a JSON object"),
            Self::Io(error) => write!(formatter, "the settings couldn't be saved: {error}"),
        }
    }
}

/// The stored preferences, or None when there are none yet, or the file can't be used (it is then left alone, and
/// the page starts from its defaults).
pub fn read(path: &Path) -> Option<Value> {
    let _guard = lock(&FILE);
    let size = match fs::metadata(path) {
        Ok(metadata) => metadata.len(),
        Err(error) if error.kind() == io::ErrorKind::NotFound => return None,
        Err(error) => {
            log::warn!("Couldn't read the preferences: {error}");
            return None;
        }
    };
    if size > MAX_BYTES as u64 {
        log::warn!("The preferences file is {size} bytes, over the {MAX_BYTES}-byte limit: using the defaults");
        return None;
    }
    let text = match fs::read(path) {
        Ok(text) => text,
        Err(error) => {
            log::warn!("Couldn't read the preferences: {error}");
            return None;
        }
    };
    match serde_json::from_slice::<Value>(&text) {
        Ok(Value::Object(mut document)) => document.remove(KEY).filter(Value::is_object),
        Ok(_) => None,
        Err(error) => {
            log::warn!("The preferences file isn't JSON ({error}): using the defaults");
            None
        }
    }
}

/// Replaces the stored preferences with `settings`, a JSON object as text. The file is written whole and then moved
/// into place, so a crash mid-write leaves the previous preferences, never half of them. It's written compact, and
/// refused when the file it would make is over the limit `read` holds a file to: a write that's accepted is always
/// read back at the next start.
pub fn write(path: &Path, settings: &str) -> Result<(), SettingsError> {
    if settings.len() > MAX_BYTES {
        return Err(SettingsError::TooLarge(settings.len()));
    }
    let value: Value = serde_json::from_str(settings).map_err(SettingsError::NotJson)?;
    if !value.is_object() {
        return Err(SettingsError::NotAnObject);
    }
    let document = Value::Object(Map::from_iter([(KEY.to_owned(), value)]));
    let bytes = serde_json::to_vec(&document).map_err(SettingsError::NotJson)?;
    if bytes.len() > MAX_BYTES {
        return Err(SettingsError::TooLarge(bytes.len()));
    }
    let _guard = lock(&FILE);
    if let Some(folder) = path.parent() {
        fs::create_dir_all(folder).map_err(SettingsError::Io)?;
    }
    let partial = partial_path(path);
    fs::write(&partial, bytes).map_err(SettingsError::Io)?;
    fs::rename(&partial, path).map_err(SettingsError::Io)
}

fn partial_path(path: &Path) -> PathBuf {
    let mut name = path.file_name().map(|name| name.to_os_string()).unwrap_or_default();
    name.push(".partial");
    path.with_file_name(name)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn temp_file(name: &str) -> PathBuf {
        let folder = std::env::temp_dir().join(format!("si-settings-{name}-{}", std::process::id()));
        let _ = fs::remove_dir_all(&folder);
        folder.join(FILE_NAME)
    }

    #[test]
    fn preferences_round_trip_in_the_store_plugins_shape() {
        let path = temp_file("round-trip");
        assert_eq!(read(&path), None);
        write(&path, r#"{"theme":"dark","density":"compact"}"#).unwrap();
        let stored: Value = serde_json::from_slice(&fs::read(&path).unwrap()).unwrap();
        assert_eq!(stored, serde_json::json!({ "settings": { "theme": "dark", "density": "compact" } }));
        assert_eq!(read(&path), Some(serde_json::json!({ "theme": "dark", "density": "compact" })));
        assert!(!partial_path(&path).exists());
        fs::remove_dir_all(path.parent().unwrap()).unwrap();
    }

    #[test]
    fn a_file_the_store_plugin_wrote_is_read() {
        let path = temp_file("plugin");
        fs::create_dir_all(path.parent().unwrap()).unwrap();
        fs::write(&path, b"{\n  \"settings\": {\n    \"zoom\": 1.25\n  }\n}").unwrap();
        assert_eq!(read(&path), Some(serde_json::json!({ "zoom": 1.25 })));
        fs::remove_dir_all(path.parent().unwrap()).unwrap();
    }

    /// Only a JSON object of bounded size is ever written: the page can't store anything else through it.
    #[test]
    fn writes_must_be_a_json_object_within_the_limit() {
        let path = temp_file("invalid");
        assert!(matches!(write(&path, "not json"), Err(SettingsError::NotJson(_))));
        assert!(matches!(write(&path, "[1,2]"), Err(SettingsError::NotAnObject)));
        assert!(matches!(write(&path, "\"text\""), Err(SettingsError::NotAnObject)));
        let large = format!("{{\"padding\":\"{}\"}}", "x".repeat(MAX_BYTES));
        assert!(matches!(write(&path, &large), Err(SettingsError::TooLarge(_))));
        assert!(!path.exists(), "nothing was written");
    }

    /// Many short preferences are much longer pretty-printed than compact: one that's accepted must still be read
    /// back, and one whose file would be over the limit is refused on writing, never ignored on the next start.
    #[test]
    fn a_write_that_is_accepted_is_read_back() {
        let path = temp_file("accepted");
        let many: Map<String, Value> = (0..5_000).map(|index| (format!("k{index:04}"), Value::from(1))).collect();
        let settings = Value::Object(many).to_string();
        assert!(settings.len() < MAX_BYTES);
        assert!(
            serde_json::to_vec_pretty(&serde_json::json!({ KEY: serde_json::from_str::<Value>(&settings).unwrap() }))
                .unwrap()
                .len()
                > MAX_BYTES
        );
        write(&path, &settings).unwrap();
        assert!(fs::metadata(&path).unwrap().len() <= MAX_BYTES as u64);
        assert_eq!(read(&path), Some(serde_json::from_str::<Value>(&settings).unwrap()));
        // Just under the limit as sent, over it once stored under its key: refused, and the file left as it was.
        let edge = format!("{{\"p\":\"{}\"}}", "x".repeat(MAX_BYTES - 10));
        assert!(edge.len() <= MAX_BYTES);
        assert!(matches!(write(&path, &edge), Err(SettingsError::TooLarge(_))));
        assert_eq!(read(&path), Some(serde_json::from_str::<Value>(&settings).unwrap()));
        fs::remove_dir_all(path.parent().unwrap()).unwrap();
    }

    #[test]
    fn a_file_that_is_too_large_or_not_an_object_reads_as_none_and_stays() {
        let path = temp_file("bad-file");
        fs::create_dir_all(path.parent().unwrap()).unwrap();
        fs::write(&path, vec![b' '; MAX_BYTES + 1]).unwrap();
        assert_eq!(read(&path), None);
        fs::write(&path, br#"{"settings":[1]}"#).unwrap();
        assert_eq!(read(&path), None);
        fs::write(&path, b"{oops").unwrap();
        assert_eq!(read(&path), None);
        assert_eq!(fs::read(&path).unwrap(), b"{oops", "a broken file is left for the user to see");
        fs::remove_dir_all(path.parent().unwrap()).unwrap();
    }
}
