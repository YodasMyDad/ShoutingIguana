//! Crawl files the app is asked to open: `.sicrawl` paths on the command line of the first launch (kept until the
//! webview asks), and of later launches (forwarded by the single-instance plugin).

use std::path::Path;

use serde::Serialize;
use tauri::{AppHandle, Emitter, Manager};

use crate::shell::Shell;
use crate::sync::lock;
use crate::window::{self, MAIN_WINDOW};

pub const OPEN_EVENT: &str = "shell://open-crawl-files";

const EXTENSION: &str = "sicrawl";

#[derive(Clone, Debug, Serialize)]
pub struct OpenCrawlFiles {
    pub paths: Vec<String>,
}

/// Paths wait here until the webview first takes them; after that, it listens, so later ones are sent at once.
#[derive(Debug, Default)]
pub struct OpenFiles {
    pending: Vec<String>,
    taken: bool,
}

impl OpenFiles {
    pub fn new(pending: Vec<String>) -> Self {
        Self { pending, taken: false }
    }

    pub fn take(&mut self) -> Vec<String> {
        self.taken = true;
        std::mem::take(&mut self.pending)
    }

    /// Paths from a later launch. Returns those to send now; the rest wait for `take`.
    pub fn arrived(&mut self, paths: Vec<String>) -> Vec<String> {
        if self.taken {
            return paths;
        }
        for path in paths {
            if !self.pending.contains(&path) {
                self.pending.push(path);
            }
        }
        Vec::new()
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

/// A second launch: bring the window forward, from the tray if it was hidden, and pass on its crawl files.
pub fn on_second_launch(app: &AppHandle, arguments: Vec<String>, cwd: String) {
    window::show(app);
    forward(app, crawl_paths(&arguments, Path::new(&cwd)));
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

fn forward(app: &AppHandle, paths: Vec<String>) {
    if paths.is_empty() {
        return;
    }
    let Some(shell) = app.try_state::<Shell>() else { return };
    let send = lock(&shell.open_files).arrived(paths);
    if !send.is_empty() {
        log::info!("Opening {} crawl file(s) from another launch", send.len());
        if let Err(error) = app.emit_to(MAIN_WINDOW, OPEN_EVENT, OpenCrawlFiles { paths: send }) {
            log::warn!("Couldn't pass crawl files to the window: {error}");
        }
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

    #[test]
    fn later_files_wait_until_the_webview_takes_the_first_then_go_straight_through() {
        let mut files = OpenFiles::new(vec!["a".into()]);
        assert!(files.arrived(vec!["b".into(), "a".into()]).is_empty());
        assert_eq!(files.take(), ["a", "b"]);
        assert!(files.take().is_empty());
        assert_eq!(files.arrived(vec!["c".into()]), ["c"]);
    }
}
