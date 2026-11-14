//! Where the crawls are kept: the engine's data folder, which the user can change (docs/v2-ui-architecture.md, "The
//! data folder"). Changing it switches the library to the folder chosen, with the crawls already in it; the crawls in
//! the old folder stay where they are, untouched, and the old folder is remembered so the user can go back to it.
//! Nothing is moved: moving gigabytes of crawl files can fail half way (a full disk, a file another program holds), and
//! a switch can't lose anything. The engine is started again on the new folder; nothing may be running while it is.
//!
//! The choice is the shell's own setting (`shell.json`), since the shell starts the engine. `SI_APP_DATA_DIR`, for
//! tests and development, still wins over it.

use std::fs;
use std::path::{Path, PathBuf};

use serde::Serialize;
use serde_json::Value;
use tauri::{AppHandle, Manager};
use tauri_plugin_store::StoreExt;

use crate::live::model::LiveView;
use crate::profile;
use crate::shell::Shell;
use crate::sync::lock;

const SHELL_STORE: &str = "shell.json";
const CURRENT: &str = "dataFolder";
const PREVIOUS: &str = "previousDataFolder";

/// What the Data folder setting shows beyond the folder in use (which the engine says: `GET /api/health`).
#[derive(Clone, Debug, Default, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DataFolderChoice {
    /// The folder the user chose, if they chose one: None is the engine's default (Documents\Shouting Iguana).
    pub chosen: Option<String>,
    /// The folder in use before the last change, for "Go back": None is the default, `has_previous` says if there is
    /// one at all.
    pub previous: Option<String>,
    pub has_previous: bool,
    /// The folder is set for this run by the environment (a test or development run): it can't be changed here.
    pub fixed: bool,
}

fn read_store(app: &AppHandle) -> Option<(Option<String>, Option<Option<String>>)> {
    let store = app.store(profile::shell_store(SHELL_STORE)).ok()?;
    let text = |value: Option<Value>| value.and_then(|value| value.as_str().map(str::to_owned));
    let previous = store.get(PREVIOUS).map(|value| value.as_str().map(str::to_owned));
    Some((text(store.get(CURRENT)), previous))
}

/// The user's choice, as stored.
pub fn choice(app: &AppHandle) -> DataFolderChoice {
    let (chosen, previous) = read_store(app).unwrap_or_default();
    DataFolderChoice {
        chosen,
        has_previous: previous.is_some(),
        previous: previous.flatten(),
        fixed: crate::sidecar::data_dir_overridden(),
    }
}

/// The folder the engine is to start on: the user's choice, while it is still a folder. A chosen folder that has gone
/// (a drive unplugged) falls back to the default for this run, and stays chosen for the next.
pub fn for_engine(app: &AppHandle) -> Option<PathBuf> {
    let chosen = choice(app).chosen?;
    let folder = PathBuf::from(&chosen);
    if folder.is_dir() {
        Some(folder)
    } else {
        log::warn!("The chosen data folder isn't there ({chosen}): the engine uses its default this time");
        None
    }
}

/// Whether the engine can keep crawls in `folder`: it is a folder, and a file can be made and removed in it.
pub fn writable(folder: &Path) -> Result<(), String> {
    if !folder.is_dir() {
        return Err("That isn't a folder.".to_owned());
    }
    let probe = folder.join(format!(".shouting-iguana-{}.probe", std::process::id()));
    fs::write(&probe, b"").map_err(|error| format!("Crawls can't be saved there: {error}."))?;
    if let Err(error) = fs::remove_file(&probe) {
        log::debug!("Couldn't remove the data folder's probe: {error}");
    }
    Ok(())
}

/// Why the folder can't be changed now: starting the engine again would cut off what runs in it, an export among it
/// (round 5's review, R2-10: a big workbook failed half written).
pub fn refusal(view: &LiveView) -> Option<&'static str> {
    if view.kinds().any() {
        Some("Something is running. Change the folder once it has finished, or after stopping it.")
    } else if view.exporting() {
        Some("A file is being exported. Change the folder once it has been saved, or after cancelling it.")
    } else {
        None
    }
}

/// Stores `folder` (None: the default) as the data folder, the one before it as previous, and starts the engine again
/// on it. Refused while a crawl or an analysis is live, or when the folder can't hold crawls. Blocks: never on the UI
/// thread.
pub fn switch(app: &AppHandle, folder: Option<PathBuf>) -> Result<DataFolderChoice, String> {
    let shell = app.try_state::<Shell>().ok_or("The app isn't ready.")?;
    if crate::sidecar::data_dir_overridden() {
        return Err("This run's data folder is set by its environment, so it can't be changed here.".to_owned());
    }
    if let Some(words) = refusal(&lock(&shell.live)) {
        return Err(words.to_owned());
    }
    if let Some(folder) = &folder {
        writable(folder)?;
    }
    let before = choice(app);
    let new = folder.map(|folder| folder.to_string_lossy().into_owned());
    if new == before.chosen {
        return Ok(before);
    }
    let store = app.store(profile::shell_store(SHELL_STORE)).map_err(|error| error.to_string())?;
    store.set(PREVIOUS, before.chosen.clone().map_or(Value::Null, Value::String));
    match &new {
        Some(path) => store.set(CURRENT, Value::String(path.clone())),
        None => {
            store.delete(CURRENT);
        }
    }
    store.save().map_err(|error| format!("The choice couldn't be saved: {error}."))?;
    log::info!("The data folder is now {}: starting the engine again", new.as_deref().unwrap_or("the default"));
    if !shell.sidecar.relaunch() {
        return Err("The engine couldn't be started again: quit and start the app.".to_owned());
    }
    Ok(choice(app))
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Round 5's review (R2-10): an export running was no reason to wait, so a big workbook failed when the engine
    /// started again on the new folder.
    #[test]
    fn the_folder_isnt_changed_under_an_export() {
        let mut view = LiveView::default();
        assert_eq!(refusal(&view), None);
        view.exports_running(&["e1".into()]);
        assert_eq!(
            refusal(&view),
            Some("A file is being exported. Change the folder once it has been saved, or after cancelling it.")
        );
    }

    #[test]
    fn a_folder_that_takes_a_file_is_writable() {
        let folder = std::env::temp_dir().join(format!("si-data-folder-test-{}", std::process::id()));
        fs::create_dir_all(&folder).unwrap();
        assert_eq!(writable(&folder), Ok(()));
        assert_eq!(fs::read_dir(&folder).unwrap().count(), 0, "the probe is gone");
        let file = folder.join("a.txt");
        fs::write(&file, b"x").unwrap();
        assert_eq!(writable(&file), Err("That isn't a folder.".to_owned()));
        assert!(writable(&folder.join("missing")).is_err());
        fs::remove_dir_all(&folder).unwrap();
    }
}
