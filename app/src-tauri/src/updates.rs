//! Updates (docs/v2-ui-architecture.md, "Updates"): Settings › Updates asks whether there's a newer version, says
//! what's new in it, and installs it when the user says so. The shell does the work with tauri-plugin-updater: it reads
//! `latest.json` from the newest GitHub release of this repository, downloads the installer it names, and checks the
//! installer's signature against the public key in tauri.conf.json before running it. A download that doesn't match
//! the key is never run. The webview gets these commands only, not the plugin's own.
//!
//! How a new version arrives depends on how this copy was installed (`Arrival`). The Windows installer, the macOS app
//! and the Linux AppImage are replaced in place. A copy installed from the `.deb` package belongs to the system's
//! package manager, so it is only told a newer version is out: the page links to the download.
//!
//! Nothing running is ever stopped for an update. A download may finish while a crawl runs: it is then kept
//! (`Updates::ready`) and installed when the user asks again, once nothing runs.

use std::path::Path;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Mutex;
use std::time::{Duration, Instant};

use serde::Serialize;
use tauri::utils::config::BundleType;
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

/// The version that couldn't be installed, kept in the settings folder across the restart that follows, so the app
/// that opens again (still the old version) can say so.
const FAILED_FILE: &str = "update-failed.txt";

/// The update the last check found, for Install to install: the user installs what they were shown.
#[derive(Default)]
pub struct Updates {
    found: Mutex<Option<Update>>,
    /// A download that finished, and whose signature was good, while something ran: installed when asked again.
    ready: Mutex<Option<Downloaded>>,
    installing: AtomicBool,
    /// The version the run before this one couldn't install, if it couldn't.
    failed: Mutex<Option<String>>,
}

struct Downloaded {
    version: String,
    bytes: Vec<u8>,
}

/// How a new version arrives in this copy of the app.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub enum Arrival {
    /// The app downloads it, checks its signature, installs it and opens again.
    InApp,
    /// The app says a newer version is out, and the user downloads it: a `.deb` install, which the package manager
    /// owns.
    Download,
}

/// How a new version arrives in this copy: by what it was packaged as, which Tauri's bundler writes into the program.
pub fn arrival() -> Arrival {
    let running_as_appimage = std::env::var_os("APPIMAGE").is_some_and(|path| !path.is_empty());
    arrival_of(cfg!(target_os = "linux"), tauri::utils::platform::bundle_type(), running_as_appimage)
}

/// On Linux only the AppImage is one file the app can replace, and only while it runs as one (the AppImage runtime
/// says where the file is in `APPIMAGE`; the program unpacked from it has no file to replace). Windows' installer
/// and the macOS app update in place.
fn arrival_of(linux: bool, bundle: Option<BundleType>, running_as_appimage: bool) -> Arrival {
    if linux && !(running_as_appimage && matches!(bundle, Some(BundleType::AppImage))) {
        Arrival::Download
    } else {
        Arrival::InApp
    }
}

/// What Install answers when it comes back at all: installed, the app closes and never hears it.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub enum InstallOutcome {
    /// The update is downloaded and its signature is good, but something is running: it installs when asked again.
    Waiting,
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
    /// Already downloaded, its signature good: it only has to be installed.
    pub downloaded: bool,
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

/// Why a check showed no update: each case is its own sentence in Settings › Updates (UpdatePanel's words), in both
/// modes. The updater collapses most of them into one error, so they're told apart by the error's kind, not its words.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub enum CheckProblem {
    /// No answer from GitHub: no connection, a name that didn't resolve, a time-out.
    NoConnection,
    /// GitHub answered, but with no update information: no release has a `latest.json` yet (a 404), or an error status.
    NoUpdateInformation,
    /// The update information was there, but this version can't read it.
    Unreadable,
    /// The update information's signature doesn't match the app's key: nothing from it is installed.
    Untrusted,
    /// This copy of the app can't check (its updater couldn't be set up).
    Unavailable,
}

/// A check that showed no update, as the page gets it: the case, its plain sentence, and the version running.
#[derive(Clone, Debug, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct CheckFailed {
    pub problem: CheckProblem,
    pub message: String,
    pub current_version: String,
}

impl CheckFailed {
    fn new(problem: CheckProblem, current_version: &str) -> Self {
        Self { problem, message: problem_words(problem).to_owned(), current_version: current_version.to_owned() }
    }
}

/// Asks the release feed whether there's a newer version than this one.
pub async fn check(app: &AppHandle) -> Result<UpdateCheck, CheckFailed> {
    let current_version = app.package_info().version.to_string();
    let mut builder = app.updater_builder();
    if let Some(endpoint) = test_endpoint() {
        log::info!("Checking for updates at the test's endpoint {endpoint}");
        builder = builder.endpoints(vec![endpoint]).map_err(|error| {
            log::warn!("The test's update endpoint was refused: {error}");
            CheckFailed::new(CheckProblem::Unavailable, &current_version)
        })?;
    }
    let updater = builder.build().map_err(|error| {
        log::warn!("The updater couldn't be set up: {error}");
        CheckFailed::new(CheckProblem::Unavailable, &current_version)
    })?;
    let found = updater.check().await.map_err(|error| {
        let problem = check_problem(&error);
        log::info!("Couldn't check for updates ({problem:?}): {error}");
        CheckFailed::new(problem, &current_version)
    })?;
    let mut available = found.as_ref().map(|update| AvailableUpdate {
        version: update.version.clone(),
        date: update.raw_json.get("pub_date").and_then(|date| date.as_str()).map(str::to_owned),
        notes: update.body.clone().map(|notes| notes.trim().to_owned()).filter(|notes| !notes.is_empty()),
        downloaded: false,
    });
    match &available {
        Some(update) => log::info!("Version {} is available (this is {current_version})", update.version),
        None => log::info!("No newer version than {current_version}"),
    }
    if let Some(updates) = app.try_state::<Updates>() {
        // A download kept for another version than the one now offered is no use.
        let mut ready = lock(&updates.ready);
        let kept = matches!((&*ready, &available), (Some(kept), Some(update)) if kept.version == update.version);
        if !kept {
            *ready = None;
        }
        drop(ready);
        if let Some(update) = &mut available {
            update.downloaded = kept;
        }
        *lock(&updates.found) = found;
    }
    Ok(UpdateCheck { current_version, available })
}

/// The case of a failed check, by the updater's error. Every endpoint answering with an error status (GitHub's 404 for
/// a release with no `latest.json`) is `ReleaseNotFound`: GitHub answered. No answer at all is a request error.
pub fn check_problem(error: &tauri_plugin_updater::Error) -> CheckProblem {
    use tauri_plugin_updater::Error;
    match error {
        Error::ReleaseNotFound => CheckProblem::NoUpdateInformation,
        Error::Reqwest(error) if error.is_decode() => CheckProblem::Unreadable,
        Error::Reqwest(error) if error.is_status() => CheckProblem::NoUpdateInformation,
        Error::Reqwest(_) | Error::Network(_) | Error::Io(_) => CheckProblem::NoConnection,
        Error::Minisign(_)
        | Error::Base64(_)
        | Error::SignatureUtf8(_)
        | Error::SignedVersionMismatch { .. }
        | Error::MissingSignedVersion => CheckProblem::Untrusted,
        Error::Serialization(_)
        | Error::Semver(_)
        | Error::UrlParse(_)
        | Error::TargetNotFound(_)
        | Error::TargetsNotFound(_) => CheckProblem::Unreadable,
        other => problem_of_words(&other.to_string()),
    }
}

/// The case, from an error's words alone: for an error of a kind `check_problem` doesn't name.
fn problem_of_words(error: &str) -> CheckProblem {
    let lower = error.to_ascii_lowercase();
    if lower.contains("valid release json") || lower.contains("404") || lower.contains("not found") {
        CheckProblem::NoUpdateInformation
    } else if lower.contains("signature") || lower.contains("pubkey") || lower.contains("public key") {
        CheckProblem::Untrusted
    } else {
        CheckProblem::NoConnection
    }
}

/// Each case's plain sentence, which the page shows if it has no words of its own for it.
pub fn problem_words(problem: CheckProblem) -> &'static str {
    match problem {
        CheckProblem::NoConnection => {
            "Couldn't reach GitHub to check for updates. Check the internet connection, then try again."
        }
        CheckProblem::NoUpdateInformation => {
            "GitHub answered, but has no update information for Shouting Iguana yet. Try again later."
        }
        CheckProblem::Unreadable => "GitHub's update information couldn't be read. Try again later.",
        CheckProblem::Untrusted => {
            "The update information's signature didn't match this app's, so it can't be trusted. Nothing was installed."
        }
        CheckProblem::Unavailable => "Updates can't be checked in this copy of the app.",
    }
}

/// Downloads the update the last check found, checks its signature, stops the engine and runs the installer, which
/// closes the app and opens it again when it has finished. Nothing running is stopped for it: while a crawl, an
/// analysis or an export runs, the update is downloaded and kept, and the answer is `Waiting`.
pub async fn install(app: &AppHandle, version: &str) -> Result<InstallOutcome, String> {
    let shell = app.try_state::<Shell>().ok_or("The app isn't ready.")?;
    let updates = app.try_state::<Updates>().ok_or("The app isn't ready.")?;
    if arrival() == Arrival::Download {
        return Err("This copy of Shouting Iguana can’t install a new version itself. Download it instead.".to_owned());
    }
    let update = lock(&updates.found)
        .clone()
        .filter(|update| update.version == version)
        .ok_or("Check for updates again: the update shown is no longer the one available.")?;
    if updates.installing.swap(true, Ordering::SeqCst) {
        return Err("The update is already being installed.".to_owned());
    }
    let result = download_and_install(app, &shell, &updates, &update).await;
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
fn begin_stopping(quit: &Mutex<QuitPhase>, live: &Mutex<LiveView>) -> Result<(), Held> {
    let mut phase = lock(quit);
    if *phase != QuitPhase::Running {
        return Err(Held::Quitting);
    }
    if let Some(words) = refusal(&lock(live)) {
        log::info!("Not installing yet: {words}");
        return Err(Held::Running);
    }
    *phase = QuitPhase::Stopping;
    Ok(())
}

/// Why the app didn't start stopping for the installer.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum Held {
    /// The app is already quitting.
    Quitting,
    /// Something runs in the engine: the update waits for it.
    Running,
}

/// The update's installer: the one kept from a download that had to wait, or downloaded now and its signature checked.
async fn installer(app: &AppHandle, updates: &Updates, update: &Update) -> Result<Vec<u8>, String> {
    let kept = lock(&updates.ready).take().filter(|kept| kept.version == update.version);
    if let Some(kept) = kept {
        log::info!("Version {} is already downloaded", kept.version);
        return Ok(kept.bytes);
    }
    let mut downloaded: u64 = 0;
    let mut told = Instant::now() - Duration::from_secs(1);
    let emitter = app.clone();
    update
        .download(
            move |chunk, total| {
                downloaded += chunk as u64;
                // At most four a second, and the last.
                if told.elapsed() >= Duration::from_millis(250) || total == Some(downloaded) {
                    told = Instant::now();
                    let _ = emitter.emit_to(MAIN_WINDOW, PROGRESS_EVENT, Progress { downloaded, total });
                }
            },
            || log::info!("The update has downloaded"),
        )
        .await
        .inspect(|_| log::info!("The update's signature is good"))
        .map_err(|error| {
            log::warn!("The update couldn't be downloaded: {error}");
            download_failure_words(check_problem(&error)).to_owned()
        })
}

/// A download that failed, in plain words: one whose signature doesn't match is refused, and says so. The page shows
/// these as they are, so they are written as its own words are.
fn download_failure_words(problem: CheckProblem) -> &'static str {
    if problem == CheckProblem::Untrusted {
        "The download’s signature didn’t match this app’s, so it was thrown away. Nothing was installed."
    } else {
        "The update couldn’t be downloaded. Check the internet connection, then try again."
    }
}

async fn download_and_install(
    app: &AppHandle,
    shell: &Shell,
    updates: &Updates,
    update: &Update,
) -> Result<InstallOutcome, String> {
    let bytes = installer(app, updates, update).await?;
    // The engine finishes what it was writing before the installer replaces it.
    match begin_stopping(&shell.quit, &shell.live) {
        Ok(()) => {}
        Err(Held::Quitting) => return Err("The app is quitting.".to_owned()),
        Err(Held::Running) => {
            *lock(&updates.ready) = Some(Downloaded { version: update.version.clone(), bytes });
            return Ok(InstallOutcome::Waiting);
        }
    }
    shell.sidecar.begin_quit(false);
    shell.tray.quit();
    let shell_sidecar = std::sync::Arc::clone(&shell.sidecar);
    let stopped = tauri::async_runtime::spawn_blocking(move || shell_sidecar.stop(GRACEFUL_SHUTDOWN_TIMEOUT)).await;
    log::info!("Installing version {}: the engine has stopped ({stopped:?})", update.version);
    // On Windows the installer takes over here and the app exits (the installer can't replace a program that is
    // running, which is why the engine is stopped first); elsewhere the app starts again on the new version.
    if let Err(error) = update.install(bytes) {
        log::error!("The update couldn't be installed: {error}: starting the app again");
        note_failure(profile::settings_folder(app).as_deref(), &update.version);
    }
    app.restart();
}

/// Keeps the version that couldn't be installed for the run that follows.
fn note_failure(folder: Option<&Path>, version: &str) {
    let Some(folder) = folder else { return };
    let kept = std::fs::create_dir_all(folder).and_then(|()| std::fs::write(folder.join(FAILED_FILE), version));
    if let Err(error) = kept {
        log::warn!("Couldn't keep a note of the update that failed: {error}");
    }
}

/// The version the run before this one couldn't install, read once: the note is removed as it is read.
fn take_failure(folder: &Path) -> Option<String> {
    let file = folder.join(FAILED_FILE);
    let noted = std::fs::read_to_string(&file).ok()?;
    if let Err(error) = std::fs::remove_file(&file) {
        log::warn!("Couldn't remove the note of the update that failed: {error}");
    }
    let version = noted.trim();
    let plain = !version.is_empty()
        && version.len() <= 64
        && version.chars().all(|c| c.is_ascii_alphanumeric() || matches!(c, '.' | '-' | '+'));
    plain.then(|| version.to_owned())
}

/// At start: picks up the note of an update the run before couldn't install.
pub fn start(app: &AppHandle) {
    let failed = profile::settings_folder(app).and_then(|folder| take_failure(&folder));
    if let Some(version) = &failed {
        log::warn!("Version {version} couldn't be installed by the run before this one");
    }
    if let Some(updates) = app.try_state::<Updates>() {
        *lock(&updates.failed) = failed;
    }
}

/// The version the run before this one couldn't install, for the page to say once.
pub fn failed(app: &AppHandle) -> Option<String> {
    app.try_state::<Updates>().and_then(|updates| lock(&updates.failed).clone())
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
        assert_eq!(begin_stopping(&quit, &live), Err(Held::Running));
        assert_eq!(*lock(&quit), QuitPhase::Running);

        // Stopped, it's held as being analysed (its results so far come next): still something running.
        lock(&live).crawl(&CrawlChange { crawl_id: "a".into(), status: "stopped".into(), error: None });
        assert!(begin_stopping(&quit, &live).is_err());
        // The library says no analysis followed: nothing runs.
        assert!(lock(&live).settle("a"));
        assert_eq!(begin_stopping(&quit, &live), Ok(()));
        assert_eq!(*lock(&quit), QuitPhase::Stopping);
        assert_eq!(begin_stopping(&quit, &live), Err(Held::Quitting));
    }

    /// The Windows installer, the macOS app and the AppImage are replaced in place. A `.deb` install is the package
    /// manager's: it is told of a newer version, and never offered an install it can't do.
    #[test]
    fn only_a_copy_the_app_can_replace_installs_updates_itself() {
        assert_eq!(arrival_of(false, Some(BundleType::Nsis), false), Arrival::InApp);
        assert_eq!(arrival_of(false, Some(BundleType::App), false), Arrival::InApp);
        assert_eq!(arrival_of(true, Some(BundleType::AppImage), true), Arrival::InApp);
        assert_eq!(arrival_of(true, Some(BundleType::Deb), false), Arrival::Download);
        assert_eq!(arrival_of(true, Some(BundleType::Rpm), false), Arrival::Download);
        // A bare Linux build, not packaged at all, and the AppImage's program run unpacked: no file to replace.
        assert_eq!(arrival_of(true, None, false), Arrival::Download);
        assert_eq!(arrival_of(true, Some(BundleType::AppImage), false), Arrival::Download);
        // A .deb install started from a shell an AppImage set APPIMAGE in is still the package manager's.
        assert_eq!(arrival_of(true, Some(BundleType::Deb), true), Arrival::Download);
        assert_eq!(serde_json::to_value(Arrival::InApp).unwrap(), serde_json::json!("inApp"));
        assert_eq!(serde_json::to_value(Arrival::Download).unwrap(), serde_json::json!("download"));
        assert_eq!(serde_json::to_value(InstallOutcome::Waiting).unwrap(), serde_json::json!("waiting"));
    }

    #[test]
    fn a_download_whose_signature_doesnt_match_is_refused_in_plain_words() {
        use tauri_plugin_updater::Error;
        let tampered = check_problem(&Error::SignatureUtf8("not a signature".into()));
        assert_eq!(tampered, CheckProblem::Untrusted);
        assert!(download_failure_words(tampered).contains("signature didn’t match"));
        assert!(download_failure_words(tampered).contains("Nothing was installed"));
        let missing = check_problem(&Error::Network("Download request failed with status: 404".into()));
        assert!(download_failure_words(missing).contains("couldn’t be downloaded"));
    }

    #[test]
    fn an_update_that_couldnt_be_installed_is_said_once_by_the_next_run() {
        let folder = std::env::temp_dir().join(format!("si-update-failed-test-{}", std::process::id()));
        assert_eq!(take_failure(&folder), None);
        note_failure(Some(&folder), "2.1.2");
        assert_eq!(take_failure(&folder), Some("2.1.2".to_owned()));
        assert_eq!(take_failure(&folder), None, "the note is read once");
        // A note that isn't a version (someone else's file) says nothing.
        std::fs::write(folder.join(FAILED_FILE), "<script>").unwrap();
        assert_eq!(take_failure(&folder), None);
        std::fs::remove_dir_all(&folder).unwrap();
    }

    #[test]
    fn a_failed_check_says_why_in_plain_words() {
        use tauri_plugin_updater::Error;
        // GitHub answered 404 (its latest release has no latest.json yet): the plugin's ReleaseNotFound. Seen in the
        // release shell, where it said to check the internet connection.
        assert_eq!(check_problem(&Error::ReleaseNotFound), CheckProblem::NoUpdateInformation);
        assert!(!problem_words(CheckProblem::NoUpdateInformation).contains("internet connection"));
        assert!(problem_words(CheckProblem::NoUpdateInformation).starts_with("GitHub answered"));
        assert_eq!(check_problem(&Error::Network("connection refused".into())), CheckProblem::NoConnection);
        assert!(problem_words(CheckProblem::NoConnection).contains("Check the internet connection"));
        assert_eq!(check_problem(&Error::MissingSignedVersion), CheckProblem::Untrusted);
        assert_eq!(
            check_problem(&Error::SignedVersionMismatch { signed: "2.1.0".into(), announced: "2.2.0".into() }),
            CheckProblem::Untrusted
        );
        assert!(problem_words(CheckProblem::Untrusted).contains("can't be trusted"));
        let unreadable = serde_json::from_str::<serde_json::Value>("{").unwrap_err();
        assert_eq!(check_problem(&Error::Serialization(unreadable)), CheckProblem::Unreadable);
        assert_eq!(check_problem(&Error::TargetNotFound("windows-x86_64".into())), CheckProblem::Unreadable);

        // From the words alone, for a kind not named.
        let no_information = CheckProblem::NoUpdateInformation;
        assert_eq!(problem_of_words("Could not fetch a valid release JSON from the remote"), no_information);
        assert_eq!(problem_of_words("HTTP 404 Not Found"), no_information);
        assert_eq!(problem_of_words("error sending request for url"), CheckProblem::NoConnection);
        assert_eq!(problem_of_words("signature verification failed"), CheckProblem::Untrusted);
    }

    #[test]
    fn a_failed_check_reaches_the_page_as_its_case_its_words_and_the_version() {
        assert_eq!(
            serde_json::to_value(CheckFailed::new(CheckProblem::NoUpdateInformation, "2.0.0")).unwrap(),
            serde_json::json!({
                "problem": "noUpdateInformation",
                "message": "GitHub answered, but has no update information for Shouting Iguana yet. Try again later.",
                "currentVersion": "2.0.0"
            })
        );
    }

    #[test]
    fn the_check_says_the_version_and_whats_new() {
        let check = UpdateCheck {
            current_version: "2.0.0".into(),
            available: Some(AvailableUpdate {
                version: "2.1.0".into(),
                date: Some("2026-11-20T10:00:00Z".into()),
                notes: Some("Faster crawls.".into()),
                downloaded: false,
            }),
        };
        assert_eq!(
            serde_json::to_value(check).unwrap(),
            serde_json::json!({
                "currentVersion": "2.0.0",
                "available": {
                    "version": "2.1.0",
                    "date": "2026-11-20T10:00:00Z",
                    "notes": "Faster crawls.",
                    "downloaded": false
                }
            })
        );
    }
}
