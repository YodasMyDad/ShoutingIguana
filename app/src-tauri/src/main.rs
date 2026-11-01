//! Shouting Iguana's desktop shell. It runs the engine as a sidecar behind a token-protected localhost API, hosts the
//! UI in one window, and keeps crawls going, and visible, in the tray while the window is hidden.

// No console window in release builds on Windows.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod commands;
mod context_menu;
mod live;
mod logging;
mod open_files;
mod quit;
mod shell;
mod sidecar;
mod sync;
mod tray;
#[cfg(windows)]
mod webview2;
mod window;

use tauri::{AppHandle, Manager, RunEvent};

use shell::Shell;

fn main() {
    let app = tauri::Builder::default()
        // First, so a second launch hands over to this one before anything else starts.
        .plugin(tauri_plugin_single_instance::init(open_files::on_second_launch))
        .plugin(logging::plugin())
        .plugin(tauri_plugin_window_state::Builder::new().with_state_flags(window::state_flags()).build())
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_notification::init())
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_store::Builder::new().build())
        .invoke_handler(tauri::generate_handler![
            commands::sidecar_state,
            commands::restart_sidecar,
            commands::request_quit,
            commands::shell_info,
            commands::take_pending_open_files,
        ])
        .on_window_event(window::on_event)
        .setup(|app| {
            let handle = app.handle();
            let arguments: Vec<String> =
                std::env::args_os().map(|argument| argument.to_string_lossy().into_owned()).collect();
            let launched_with =
                std::env::current_dir().map(|cwd| open_files::crawl_paths(&arguments, &cwd)).unwrap_or_default();
            let shell = Shell::new(handle, launched_with);
            let sidecar = shell.sidecar.clone();
            live::start(handle.clone(), &shell);
            app.manage(shell);
            // The engine starts while the window and the page load.
            sidecar.start();
            log::info!("Shouting Iguana {} started", handle.package_info().version);
            window::create(handle)?;
            Ok(())
        })
        .build(tauri::generate_context!())
        .expect("Shouting Iguana couldn't start");
    app.run(on_run_event);
}

fn on_run_event(app: &AppHandle, event: RunEvent) {
    match event {
        // macOS's Quit, or the last window gone some other way: the same quit flow as the tray's. The shell's own
        // exit, once the engine has stopped, carries a code and goes through.
        RunEvent::ExitRequested { code: None, api, .. } => {
            api.prevent_exit();
            quit::request(app);
        }
        // The last safety net: an engine still running is stopped, and killed if it won't.
        RunEvent::Exit => {
            if let Some(shell) = app.try_state::<Shell>() {
                shell.sidecar.stop_at_exit();
            }
        }
        #[cfg(target_os = "macos")]
        RunEvent::Reopen { .. } => window::show(app),
        #[cfg(target_os = "macos")]
        RunEvent::Opened { urls } => open_files::on_opened(app, &urls),
        _ => {}
    }
}
