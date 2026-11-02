//! Quitting: one question when a crawl or an analysis would be stopped, then a graceful stop of the engine, then the
//! exit. It runs on its own thread, since finding out what is live, the question and the stop all wait.

use std::thread;
use std::time::Instant;

use tauri::{AppHandle, Manager};
use tauri_plugin_dialog::{DialogExt, MessageDialogButtons, MessageDialogKind};

use crate::live::api::QUICK_CALL_TIMEOUT;
use crate::live::model::LiveKinds;
use crate::open_files;
use crate::shell::{QuitPhase, Shell};
use crate::sidecar::GRACEFUL_SHUTDOWN_TIMEOUT;
use crate::sync::lock;
use crate::window::{self, MAIN_WINDOW};

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Question {
    pub title: &'static str,
    pub message: &'static str,
}

const STOP_AND_QUIT: &str = "Stop and quit";
const CANCEL: &str = "Cancel";

/// The question to ask, if quitting would stop something. A pause isn't kept on disk and a stop leaves the crawl
/// resumable, so one question covers running, paused and stopping crawls alike (docs/v2-ui-ia.md, "The tray").
pub fn question(kinds: LiveKinds) -> Option<Question> {
    if kinds.crawling {
        Some(Question { title: "Stop crawling and quit?", message: "You can continue later from the library." })
    } else if kinds.analysing {
        Some(Question { title: "Stop the analysis and quit?", message: "You can analyse again from the library." })
    } else {
        None
    }
}

/// Starts quitting, unless it is already under way.
pub fn request(app: &AppHandle) {
    let Some(shell) = app.try_state::<Shell>() else {
        app.exit(0);
        return;
    };
    {
        let mut phase = lock(&shell.quit);
        if *phase != QuitPhase::Running {
            log::debug!("Already quitting");
            return;
        }
        *phase = QuitPhase::Deciding;
    }
    let quitting = app.clone();
    if let Err(error) = thread::Builder::new().name("quit".to_owned()).spawn(move || run(&quitting)) {
        log::error!("Couldn't start quitting: {error}");
        *lock(&shell.quit) = QuitPhase::Running;
    }
}

fn run(app: &AppHandle) {
    let shell = app.state::<Shell>();
    // Most quits have nothing to stop: the window goes at once, before the engine confirms it.
    let seen = lock(&shell.live).kinds();
    if !seen.any() {
        window::hide(app);
    }
    if let Some(question) = question(live_kinds(&shell, seen)) {
        if !ask(app, question) {
            log::info!("Quitting cancelled");
            if !seen.any() {
                window::show(app);
            }
            // Launches made while the question was up are this copy's after all. The same lock order as a launch.
            let launched = {
                let mut files = lock(&shell.open_files);
                *lock(&shell.quit) = QuitPhase::Running;
                files.quit_cancelled()
            };
            if let Some(paths) = launched {
                window::show(app);
                open_files::forward(app, paths);
            }
            return;
        }
        // The window says it is stopping crawls while the engine stops them.
        window::show(app);
        shell.sidecar.begin_quit(true);
    } else {
        shell.sidecar.begin_quit(false);
    }
    *lock(&shell.quit) = QuitPhase::Stopping;
    shell.tray.quit();
    let started = Instant::now();
    let stopped = shell.sidecar.stop(GRACEFUL_SHUTDOWN_TIMEOUT);
    log::info!(
        "Quitting: the engine stopped in {} ms ({})",
        started.elapsed().as_millis(),
        stopped.map_or_else(|| "it wasn't running".to_owned(), |status| status.to_string())
    );
    // The app was launched again while it quit: that launch gets a copy of its own, now the engine has let go.
    if let Some(paths) = lock(&shell.open_files).relaunch_after_quit() {
        open_files::relaunch(&paths);
    }
    app.exit(0);
}

/// What is live, from the engine itself when it answers quickly, else from the event stream.
fn live_kinds(shell: &Shell, seen: LiveKinds) -> LiveKinds {
    if let Some(connection) = shell.sidecar.link().current() {
        match shell.api.crawls(&connection, Some(QUICK_CALL_TIMEOUT)) {
            Ok(items) => return LiveKinds::of_items(&items),
            Err(error) => log::warn!("Couldn't ask the engine what is running ({error}): going by its events"),
        }
    }
    seen
}

/// Asks with a native dialog: modal on the window when it is on screen, on its own when the window is in the tray.
fn ask(app: &AppHandle, question: Question) -> bool {
    let mut dialog = app
        .dialog()
        .message(question.message)
        .title(question.title)
        .kind(MessageDialogKind::Warning)
        .buttons(MessageDialogButtons::OkCancelCustom(STOP_AND_QUIT.to_owned(), CANCEL.to_owned()));
    let on_screen = app
        .get_webview_window(MAIN_WINDOW)
        .filter(|window| window.is_visible().unwrap_or(false) && !window.is_minimized().unwrap_or(false));
    if let Some(window) = on_screen {
        dialog = dialog.parent(&window);
    }
    dialog.blocking_show()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_crawl_asks_about_crawling_even_beside_an_analysis() {
        let crawling = question(LiveKinds { crawling: true, analysing: true }).unwrap();
        assert_eq!(crawling.title, "Stop crawling and quit?");
        assert_eq!(crawling.message, "You can continue later from the library.");
    }

    #[test]
    fn an_analysis_alone_asks_about_the_analysis() {
        let analysing = question(LiveKinds { crawling: false, analysing: true }).unwrap();
        assert_eq!(analysing.title, "Stop the analysis and quit?");
        assert_eq!(analysing.message, "You can analyse again from the library.");
    }

    #[test]
    fn nothing_live_asks_nothing() {
        assert_eq!(question(LiveKinds::default()), None);
    }
}
