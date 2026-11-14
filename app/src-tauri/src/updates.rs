//! Updates (docs/v2-ui-architecture.md, "Updates"): Settings › Updates asks whether there's a newer version, says
//! what's new in it, and installs it when the user says so. The shell does the work with tauri-plugin-updater: it reads
//! `latest.json` from the newest GitHub release of this repository, downloads the installer it names, and checks the
//! installer's signature against the public key in tauri.conf.json before running it. A download that doesn't match
//! the key is never run. The webview gets these commands only, not the plugin's own.

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Mutex;
use std::time::{Duration, Instant};

use serde::Serialize;
use tauri::{AppHandle, Emitter, Manager, Url};
use tauri_plugin_updater::{Update, UpdaterExt};

use crate::live::model::LiveView;
use crate::profile;
use crate::shell::{QuitPhase, Shell};
use crate::sidecar::GRACEFUL_SHUTDOWN_TIMEOUT;
use crate::sync::lock;
use crate::window::MAIN_WINDOW;

/// `{ downloaded, total }` in bytes while an update downloads; `total` is null when the server doesn't say.
pub const PROGRESS_EVENT: &str = "shell://update-progress";

/// A test run of the built app may point the updater at its own server (the smoke test): never a user's run.
pub const ENDPOINT_OVERRIDE: &str = "SI_UPDATER_ENDPOINT";

/// The update the last check found, for Install to install: the user installs what they were shown.
#[derive(Default)]
pub struct Updates {
    found: Mutex<Option<Update>>,
    installing: AtomicBool,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct UpdateCheck {
    pub current_version: String,
    /// The newer version, or None when this is the newest.
    pub available: Option<AvailableUpdate>,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct AvailableUpdate {
    pub version: String,
    /// When it was published, as the release says it (RFC 3339), if it does.
    pub date: Option<String>,
    /// What's new, as the release's notes say it.
    pub notes: Option<String>,
}

#[derive(Clone, Copy, Debug, Serialize)]
pub struct Progress {
    downloaded: u64,
    total: Option<u64>,
}

fn test_endpoint() -> Option<Url> {
    profile::test_profile()?;
    let value = std::env::var(ENDPOINT_OVERRIDE).ok().filter(|value| !value.is_empty())?;
    Url::parse(&value).inspect_err(|error| log::warn!("{ENDPOINT_OVERRIDE} isn't a URL: {error}")).ok()
}

/// Asks the release feed whether there's a newer version than this one.
pub async fn check(app: &AppHandle) -> Result<UpdateCheck, String> {
    let current_version = app.package_info().version.to_string();
    let mut builder = app.updater_builder();
    if let Some(endpoint) = test_endpoint() {
        log::info!("Checking for updates at the test's endpoint {endpoint}");
        builder = builder.endpoints(vec![endpoint]).map_err(|error| error.to_string())?;
    }
    let updater = builder.build().map_err(|error| {
        log::warn!("The updater couldn't be set up: {error}");
        "Updates can't be checked in this copy of the app.".to_owned()
    })?;
    let found = updater.check().await.map_err(|error| {
        log::info!("Couldn't check for updates: {error}");
        check_failed_words(&error.to_string())
    })?;
    let available = found.as_ref().map(|update| AvailableUpdate {
        version: update.version.clone(),
        date: update.raw_json.get("pub_date").and_then(|date| date.as_str()).map(str::to_owned),
        notes: update.body.clone().map(|notes| notes.trim().to_owned()).filter(|notes| !notes.is_empty()),
    });
    match &available {
        Some(update) => log::info!("Version {} is available (this is {current_version})", update.version),
        None => log::info!("No newer version than {current_version}"),
    }
    if let Some(updates) = app.try_state::<Updates>() {
        *lock(&updates.found) = found;
    }
    Ok(UpdateCheck { current_version, available })
}

/// Plain words for a check that failed: the network, or a release feed that isn't there yet.
pub fn check_failed_words(error: &str) -> String {
    let lower = error.to_ascii_lowercase();
    if lower.contains("valid release json") {
        // The plugin says this whichever way every endpoint failed: no answer, or no latest.json in the release.
        "No update information could be read from GitHub. Check the internet connection, or try again later.".to_owned()
    } else if lower.contains("404") || lower.contains("not found") {
        "No update information was found on GitHub. Try again later.".to_owned()
    } else if lower.contains("signature") || lower.contains("pubkey") || lower.contains("public key") {
        "The update information couldn't be trusted, so nothing was installed.".to_owned()
    } else {
        "Couldn't reach GitHub to check for updates. Check the internet connection, then try again.".to_owned()
    }
}

/// Downloads the update the last check found, checks its signature, stops the engine and runs the installer, which
/// closes the app and opens it again when it has finished. Refused while a crawl or an analysis runs: installing
/// would stop it.
pub async fn install(app: &AppHandle, version: &str) -> Result<(), String> {
    let shell = app.try_state::<Shell>().ok_or("The app isn't ready.")?;
    let updates = app.try_state::<Updates>().ok_or("The app isn't ready.")?;
    if let Some(words) = refusal(&lock(&shell.live)) {
        return Err(words.to_owned());
    }
    let update = lock(&updates.found)
        .clone()
        .filter(|update| update.version == version)
        .ok_or("Check for updates again: the update shown is no longer the one available.")?;
    if updates.installing.swap(true, Ordering::SeqCst) {
        return Err("The update is already being installed.".to_owned());
    }
    let result = download_and_install(app, &shell, &update).await;
    updates.installing.store(false, Ordering::SeqCst);
    result
}

/// Why an update can't be installed now: installing stops the engine, and what runs in it with it, an export among it
/// (round 5's review, R2-10).
fn refusal(view: &LiveView) -> Option<&'static str> {
    if view.kinds().any() {
        Some("Something is running. Install the update once it has finished.")
    } else if view.exporting() {
        Some("A file is being exported. Install the update once it has been saved.")
    } else {
        None
    }
}

/// The app starts stopping for the installer. The download may have taken minutes, and whatever started meanwhile
/// would be stopped with the engine (round 5's review, R2-3): asked again here, under the quit lock, so the app is
/// either still running with it or stopping without it.
fn begin_stopping(quit: &Mutex<QuitPhase>, live: &Mutex<LiveView>) -> Result<(), String> {
    let mut phase = lock(quit);
    if *phase != QuitPhase::Running {
        return Err("The app is quitting.".to_owned());
    }
    if refusal(&lock(live)).is_some() {
        log::info!("Not installing: something started while the update downloaded");
        return Err("Something started while the update downloaded. Install it once that has finished.".to_owned());
    }
    *phase = QuitPhase::Stopping;
    Ok(())
}

async fn download_and_install(app: &AppHandle, shell: &Shell, update: &Update) -> Result<(), String> {
    let mut downloaded: u64 = 0;
    let mut told = Instant::now() - Duration::from_secs(1);
    let emitter = app.clone();
    let bytes = update
        .download(
            move |chunk, total| {
                downloaded += chunk as u64;
                // At most four a second, and the last.
                if told.elapsed() >= Duration::from_millis(250) || total == Some(downloaded) {
                    told = Instant::now();
                    let _ = emitter.emit_to(MAIN_WINDOW, PROGRESS_EVENT, Progress { downloaded, total });
                }
            },
            || log::info!("The update has downloaded and its signature is good"),
        )
        .await
        .map_err(|error| {
            log::warn!("The update couldn't be downloaded: {error}");
            if error.to_string().to_ascii_lowercase().contains("signature") {
                "The download's signature didn't match, so it wasn't installed.".to_owned()
            } else {
                "The update couldn't be downloaded. Check the internet connection, then try again.".to_owned()
            }
        })?;
    // The engine finishes what it was writing before the installer replaces it.
    begin_stopping(&shell.quit, &shell.live)?;
    shell.sidecar.begin_quit(false);
    shell.tray.quit();
    let shell_sidecar = std::sync::Arc::clone(&shell.sidecar);
    let stopped = tauri::async_runtime::spawn_blocking(move || shell_sidecar.stop(GRACEFUL_SHUTDOWN_TIMEOUT)).await;
    log::info!("Installing version {}: the engine has stopped ({stopped:?})", update.version);
    // On Windows the installer takes over here and the app exits; elsewhere the app starts again on the new version.
    if let Err(error) = update.install(bytes) {
        log::error!("The update couldn't be installed: {error}: starting the app again");
    }
    app.restart();
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Round 5's review (R2-3, R2-10): installing stops the engine, so it waits for what runs: a crawl or an analysis
    /// before the download, an export too, and anything that started while the update downloaded.
    #[test]
    fn nothing_running_is_stopped_by_an_install() {
        use crate::live::model::{CrawlChange, ExportJob};
        let mut view = LiveView::default();
        assert_eq!(refusal(&view), None);
        view.export(&ExportJob { export_id: "e1".into(), status: "running".into(), ..ExportJob::default() });
        assert_eq!(refusal(&view), Some("A file is being exported. Install the update once it has been saved."));

        // The download done, a crawl started meanwhile: the engine isn't stopped, and the app isn't quitting.
        let (quit, live) = (Mutex::new(QuitPhase::Running), Mutex::new(LiveView::default()));
        lock(&live).crawl(&CrawlChange { crawl_id: "a".into(), status: "running".into(), error: None });
        assert_eq!(
            begin_stopping(&quit, &live),
            Err("Something started while the update downloaded. Install it once that has finished.".to_owned())
        );
        assert_eq!(*lock(&quit), QuitPhase::Running);

        lock(&live).crawl(&CrawlChange { crawl_id: "a".into(), status: "stopped".into(), error: None });
        assert_eq!(begin_stopping(&quit, &live), Ok(()));
        assert_eq!(*lock(&quit), QuitPhase::Stopping);
        assert_eq!(begin_stopping(&quit, &live), Err("The app is quitting.".to_owned()));
    }

    #[test]
    fn a_failed_check_says_why_in_plain_words() {
        // The plugin's own words when no endpoint gave a release (seen in the real shell: GitHub's latest release has
        // no latest.json yet).
        assert_eq!(
            check_failed_words("Could not fetch a valid release JSON from the remote"),
            "No update information could be read from GitHub. Check the internet connection, or try again later."
        );
        assert_eq!(
            check_failed_words("HTTP 404 Not Found"),
            "No update information was found on GitHub. Try again later."
        );
        assert!(check_failed_words("error sending request for url").starts_with("Couldn't reach GitHub"));
        assert!(check_failed_words("signature verification failed").contains("couldn't be trusted"));
    }

    #[test]
    fn the_check_says_the_version_and_whats_new() {
        let check = UpdateCheck {
            current_version: "2.0.0".into(),
            available: Some(AvailableUpdate {
                version: "2.1.0".into(),
                date: Some("2026-11-20T10:00:00Z".into()),
                notes: Some("Faster crawls.".into()),
            }),
        };
        assert_eq!(
            serde_json::to_value(check).unwrap(),
            serde_json::json!({
                "currentVersion": "2.0.0",
                "available": { "version": "2.1.0", "date": "2026-11-20T10:00:00Z", "notes": "Faster crawls." }
            })
        );
    }
}
