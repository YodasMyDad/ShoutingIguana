//! The shell's log: `shell.log` in the app's log folder, rotated, and stdout too in debug builds. The token is never
//! logged: states and connections print without it.

use log::LevelFilter;
use tauri::plugin::TauriPlugin;
use tauri::Wry;
use tauri_plugin_log::{RotationStrategy, Target, TargetKind, TimezoneStrategy};

use crate::sidecar::SIDECAR_LOG;

const FILE_NAME: &str = "shell";
const MAX_FILE_BYTES: u128 = 5 * 1024 * 1024;
const KEPT_FILES: usize = 5;

pub fn plugin() -> TauriPlugin<Wry> {
    // The shell and the sidecar's stderr at debug level while developing; the sidecar keeps its own full log.
    let own = if cfg!(debug_assertions) { LevelFilter::Debug } else { LevelFilter::Info };
    let file_name = Some(FILE_NAME.to_owned());
    let file = match crate::profile::log_folder() {
        Some(path) => TargetKind::Folder { path, file_name },
        None => TargetKind::LogDir { file_name },
    };
    let mut builder = tauri_plugin_log::Builder::new()
        .clear_targets()
        .target(Target::new(file))
        .rotation_strategy(RotationStrategy::KeepSome(KEPT_FILES))
        .max_file_size(MAX_FILE_BYTES)
        .timezone_strategy(TimezoneStrategy::UseLocal)
        .level(LevelFilter::Info)
        .level_for(env!("CARGO_CRATE_NAME"), own)
        .level_for(SIDECAR_LOG, own);
    if cfg!(debug_assertions) {
        builder = builder.target(Target::new(TargetKind::Stdout));
    }
    builder.build()
}
