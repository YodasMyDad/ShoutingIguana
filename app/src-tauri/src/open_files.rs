//! Crawl files the app is asked to open: `.sicrawl` paths on the command line of the first launch, and of later
//! launches (forwarded by the single-instance plugin). They wait here until the webview takes them, which it does
//! once the engine can open them, so none is lost to a slow start. A launch made while the app is quitting starts a
//! new copy of the app once this one has gone, rather than bringing back a window that is about to close.

use std::path::Path;
use std::process::{Command, Stdio};
use std::time::Duration;

use tauri::{AppHandle, Emitter, Manager};

use crate::shell::Shell;
use crate::sync::lock;
use crate::window::{self, MAIN_WINDOW};

/// Says that crawl files are waiting: the webview takes them with `take_pending_open_files`.
pub const OPEN_EVENT: &str = "shell://open-crawl-files";

/// The option a copy of the app started by a quitting one carries, with the quitting one's process id.
pub const RELAUNCH_AFTER: &str = "--relaunch-after";

/// How long a relaunched copy waits for the one that started it to be gone. It exits as soon as it has started it.
const RELAUNCH_WAIT: Duration = Duration::from_secs(10);

pub const EXTENSION: &str = "sicrawl";

#[derive(Debug, Default)]
pub struct OpenFiles {
    /// Waiting for the webview, oldest first.
    pending: Vec<String>,
    /// Launches made while the app was quitting, with their crawl files.
    while_quitting: Option<Vec<String>>,
    /// The new copy of the app has been started: a launch after that starts one of its own.
    relaunched: bool,
}

impl OpenFiles {
    pub fn new(pending: Vec<String>) -> Self {
        Self { pending, ..Self::default() }
    }

    /// Everything waiting, once.
    pub fn take(&mut self) -> Vec<String> {
        std::mem::take(&mut self.pending)
    }

    /// Paths from a later launch. True if any is new.
    pub fn arrived(&mut self, paths: Vec<String>) -> bool {
        let mut added = false;
        for path in paths {
            if !self.pending.contains(&path) {
                self.pending.push(path);
                added = true;
            }
        }
        added
    }

    /// A launch while the app quits. Its paths wait for the new copy, unless that has started already: then they
    /// are returned, for a copy of their own.
    pub fn launched_while_quitting(&mut self, paths: Vec<String>) -> Option<Vec<String>> {
        if self.relaunched {
            return Some(paths);
        }
        let queued = self.while_quitting.get_or_insert_with(Vec::new);
        for path in paths {
            if !queued.contains(&path) {
                queued.push(path);
            }
        }
        None
    }

    /// The engine has stopped and the app is about to exit: the launches made meanwhile, if any, for a new copy.
    pub fn relaunch_after_quit(&mut self) -> Option<Vec<String>> {
        self.relaunched = true;
        self.while_quitting.take()
    }

    /// Quitting was cancelled: the launches made meanwhile are this copy's after all.
    pub fn quit_cancelled(&mut self) -> Option<Vec<String>> {
        self.while_quitting.take()
    }
}

/// The `.sicrawl` paths in a command line (the program first), made absolute against `cwd`, without repeats.
pub fn crawl_paths(arguments: &[String], cwd: &Path) -> Vec<String> {
    let mut paths: Vec<String> = Vec::new();
    for argument in arguments.iter().skip(1) {
        let path = Path::new(argument);
        let is_crawl = path.extension().is_some_and(|extension| extension.eq_ignore_ascii_case(EXTENSION));
        if argument.starts_with('-') || !is_crawl {
            continue;
        }
        let joined = cwd.join(path);
        let full = std::path::absolute(&joined).unwrap_or(joined).to_string_lossy().into_owned();
        if !paths.contains(&full) {
            paths.push(full);
        }
    }
    paths
}

/// A second launch: bring the window forward, from the tray if it was hidden, and pass on its crawl files. While the
/// app quits, they wait for a new copy instead.
pub fn on_second_launch(app: &AppHandle, arguments: Vec<String>, cwd: String) {
    let paths = crawl_paths(&arguments, Path::new(&cwd));
    let Some(shell) = app.try_state::<Shell>() else { return };
    {
        // The same lock order as cancelling a quit (files, then the quit phase), so a launch is never stranded.
        let mut files = lock(&shell.open_files);
        if shell.is_quitting() {
            log::info!("A launch while quitting: it starts a new copy of the app once this one has gone");
            if let Some(paths) = files.launched_while_quitting(paths) {
                drop(files);
                relaunch(&paths);
            }
            return;
        }
    }
    window::show(app);
    forward(app, paths);
}

/// macOS hands files to the running app (and to a first launch) as "open" events rather than arguments.
#[cfg(target_os = "macos")]
pub fn on_opened(app: &AppHandle, urls: &[tauri::Url]) {
    let paths = urls
        .iter()
        .filter_map(|url| url.to_file_path().ok())
        .filter(|path| path.extension().is_some_and(|extension| extension.eq_ignore_ascii_case(EXTENSION)))
        .map(|path| path.to_string_lossy().into_owned())
        .collect();
    forward(app, paths);
}

/// Adds paths to those waiting, and tells the webview to take them.
pub fn forward(app: &AppHandle, paths: Vec<String>) {
    if paths.is_empty() {
        return;
    }
    let Some(shell) = app.try_state::<Shell>() else { return };
    let count = paths.len();
    if lock(&shell.open_files).arrived(paths) {
        log::info!("{count} crawl file(s) from another launch are waiting to be opened");
        if let Err(error) = app.emit_to(MAIN_WINDOW, OPEN_EVENT, ()) {
            log::warn!("Couldn't tell the window crawl files are waiting: {error}");
        }
    }
}

/// Starts a new copy of the app with these crawl files. It waits for this one to be gone (`RELAUNCH_AFTER`), so
/// single instance doesn't hand it straight back.
pub fn relaunch(paths: &[String]) {
    let program = match std::env::current_exe() {
        Ok(program) => program,
        Err(error) => {
            log::warn!("Couldn't find the app to start it again: {error}");
            return;
        }
    };
    let started = Command::new(program)
        .arg(RELAUNCH_AFTER)
        .arg(std::process::id().to_string())
        .args(paths)
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn();
    match started {
        Ok(child) => {
            log::info!("Started a new copy of the app (pid {}) with {} crawl file(s)", child.id(), paths.len())
        }
        Err(error) => log::warn!("Couldn't start a new copy of the app: {error}"),
    }
}

/// A copy started by a quitting one waits until that one has exited. Called first thing, before single instance.
pub fn wait_for_earlier_copy(arguments: &[String]) {
    if let Some(pid) = relaunched_after(arguments) {
        if !wait_for_exit(pid, RELAUNCH_WAIT) {
            log::warn!("The copy of the app that started this one (pid {pid}) is still running");
        }
    }
}

fn relaunched_after(arguments: &[String]) -> Option<u32> {
    arguments.windows(2).find(|pair| pair[0] == RELAUNCH_AFTER).and_then(|pair| pair[1].parse().ok())
}

/// True once the process has gone (or there is none by that id), false if it is still there after `timeout`.
#[cfg(windows)]
fn wait_for_exit(pid: u32, timeout: Duration) -> bool {
    use windows_sys::Win32::Foundation::{CloseHandle, WAIT_OBJECT_0};
    use windows_sys::Win32::System::Threading::{OpenProcess, WaitForSingleObject, PROCESS_SYNCHRONIZE};
    // SAFETY: the handle is checked before use and closed once; the wait is bounded.
    unsafe {
        let process = OpenProcess(PROCESS_SYNCHRONIZE, 0, pid);
        if process.is_null() {
            return true;
        }
        let waited = WaitForSingleObject(process, u32::try_from(timeout.as_millis()).unwrap_or(u32::MAX));
        CloseHandle(process);
        waited == WAIT_OBJECT_0
    }
}

/// Elsewhere, `kill -0` says whether the process is still there.
#[cfg(not(windows))]
fn wait_for_exit(pid: u32, timeout: Duration) -> bool {
    let started = std::time::Instant::now();
    loop {
        let alive = Command::new("kill")
            .args(["-0", &pid.to_string()])
            .stdout(Stdio::null())
            .stderr(Stdio::null())
            .status()
            .is_ok_and(|status| status.success());
        if !alive {
            return true;
        }
        if started.elapsed() >= timeout {
            return false;
        }
        std::thread::sleep(Duration::from_millis(50));
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn arguments(list: &[&str]) -> Vec<String> {
        list.iter().map(|argument| (*argument).to_owned()).collect()
    }

    #[test]
    fn only_crawl_files_are_taken_made_absolute_once_each() {
        let cwd = std::env::temp_dir();
        let absolute = cwd.join("elsewhere").join("b.SICRAWL").to_string_lossy().into_owned();
        let found = crawl_paths(
            &arguments(&[
                "shouting-iguana",
                "a.sicrawl",
                "--flag.sicrawl",
                "notes.txt",
                &absolute,
                "a.sicrawl",
                "sicrawl",
            ]),
            &cwd,
        );
        assert_eq!(found, [cwd.join("a.sicrawl").to_string_lossy().into_owned(), absolute]);
    }

    #[test]
    fn the_program_itself_is_never_a_crawl() {
        assert!(crawl_paths(&arguments(&["odd.sicrawl"]), &std::env::temp_dir()).is_empty());
    }

    /// Windows resolves dots when it makes a path absolute; elsewhere they are kept, which is still a valid path.
    #[cfg(windows)]
    #[test]
    fn dots_in_relative_paths_are_resolved() {
        let cwd = std::env::temp_dir().join("one");
        let found = crawl_paths(&arguments(&["app", "../two/c.sicrawl"]), &cwd);
        assert_eq!(found, [std::env::temp_dir().join("two").join("c.sicrawl").to_string_lossy().into_owned()]);
    }

    /// The front-end review's case (M1): the first launch's files used to be handed over once, at mount, and lost if
    /// the engine wasn't ready to open them. Now every file waits until the webview takes it, however late.
    #[test]
    fn files_wait_until_the_webview_takes_them_each_once() {
        let mut files = OpenFiles::new(vec!["a".into()]);
        assert!(files.arrived(vec!["b".into(), "a".into()]));
        assert!(!files.arrived(vec!["b".into()]), "nothing new");
        assert_eq!(files.take(), ["a", "b"]);
        assert!(files.take().is_empty());
        assert!(files.arrived(vec!["c".into()]));
        assert_eq!(files.take(), ["c"]);
    }

    /// The review's case (L6): a launch while quitting used to bring back the dying window and lose its files.
    #[test]
    fn launches_while_quitting_wait_for_a_new_copy_and_later_ones_start_their_own() {
        let mut files = OpenFiles::new(Vec::new());
        assert_eq!(files.launched_while_quitting(vec!["a".into()]), None);
        assert_eq!(files.launched_while_quitting(vec!["b".into(), "a".into()]), None);
        assert_eq!(files.launched_while_quitting(Vec::new()), None);
        assert!(files.take().is_empty(), "never handed to the quitting window");
        assert_eq!(files.relaunch_after_quit(), Some(vec!["a".to_owned(), "b".to_owned()]));
        assert_eq!(files.launched_while_quitting(vec!["c".into()]), Some(vec!["c".to_owned()]));
        assert_eq!(files.relaunch_after_quit(), None);
    }

    #[test]
    fn a_plain_launch_while_quitting_still_relaunches() {
        let mut files = OpenFiles::new(Vec::new());
        assert_eq!(files.launched_while_quitting(Vec::new()), None);
        assert_eq!(files.relaunch_after_quit(), Some(Vec::new()));
        let mut quiet = OpenFiles::new(Vec::new());
        assert_eq!(quiet.relaunch_after_quit(), None, "no launch, no new copy");
    }

    #[test]
    fn a_cancelled_quit_keeps_the_launches_made_meanwhile() {
        let mut files = OpenFiles::new(Vec::new());
        files.launched_while_quitting(vec!["a".into()]);
        assert_eq!(files.quit_cancelled(), Some(vec!["a".to_owned()]));
        assert_eq!(files.quit_cancelled(), None);
        assert_eq!(files.relaunch_after_quit(), None);
    }

    #[test]
    fn a_relaunch_names_the_copy_to_wait_for_and_is_no_crawl() {
        let relaunch = arguments(&["app", RELAUNCH_AFTER, "4242", "a.sicrawl"]);
        assert_eq!(relaunched_after(&relaunch), Some(4242));
        assert_eq!(relaunched_after(&arguments(&["app", "a.sicrawl"])), None);
        assert_eq!(relaunched_after(&arguments(&["app", RELAUNCH_AFTER, "x"])), None);
        let cwd = std::env::temp_dir();
        assert_eq!(crawl_paths(&relaunch, &cwd), [cwd.join("a.sicrawl").to_string_lossy().into_owned()]);
    }

    #[test]
    fn waiting_for_a_copy_that_is_gone_returns_at_once() {
        let mut child =
            Command::new(std::env::current_exe().unwrap()).arg("--list").stdout(Stdio::null()).spawn().unwrap();
        let pid = child.id();
        child.wait().unwrap();
        let started = std::time::Instant::now();
        assert!(wait_for_exit(pid, Duration::from_secs(5)));
        assert!(started.elapsed() < Duration::from_secs(2));
    }
}
