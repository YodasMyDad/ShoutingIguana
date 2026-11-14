//! The shell's managed state: the sidecar, its own view of the live crawls, the tray, and where quitting has got to.

use std::sync::atomic::AtomicBool;
use std::sync::{Arc, Mutex};

use tauri::AppHandle;

use crate::live::api::Api;
use crate::live::model::LiveView;
use crate::mode::{Mode, ShellMode};
use crate::open_files::OpenFiles;
use crate::sidecar::Sidecar;
use crate::sync::lock;
use crate::tray::TrayHandle;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum QuitPhase {
    Running,
    /// Finding out what is live, or asking the user.
    Deciding,
    /// Stopping the sidecar: the app exits once it has.
    Stopping,
}

pub struct Shell {
    pub sidecar: Arc<Sidecar>,
    pub api: Arc<Api>,
    pub live: Arc<Mutex<LiveView>>,
    pub tray: TrayHandle,
    /// The window is hidden in the tray.
    pub window_hidden: AtomicBool,
    pub quit: Mutex<QuitPhase>,
    pub open_files: Mutex<OpenFiles>,
    /// The page's mode, for the shell's own words.
    pub mode: ShellMode,
}

impl Shell {
    pub fn new(app: &AppHandle, open_files: Vec<String>) -> Self {
        Self {
            sidecar: Arc::new(Sidecar::new(app.clone())),
            api: Arc::default(),
            live: Arc::default(),
            tray: TrayHandle::start(app.clone()),
            window_hidden: AtomicBool::new(false),
            quit: Mutex::new(QuitPhase::Running),
            open_files: Mutex::new(OpenFiles::new(open_files)),
            mode: ShellMode::new(stored_mode(app)),
        }
    }

    pub fn is_quitting(&self) -> bool {
        *lock(&self.quit) != QuitPhase::Running
    }
}

/// The mode the page last stored, read as the page reads it (a first start is Simple).
fn stored_mode(app: &AppHandle) -> Mode {
    let stored = crate::profile::settings_folder(app)
        .and_then(|folder| crate::settings::read(&folder.join(crate::settings::FILE_NAME)));
    Mode::of(crate::settings::preferences(stored.as_ref()).simple)
}
