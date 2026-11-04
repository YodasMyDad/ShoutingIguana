//! Shouting Iguana's desktop shell. It runs the engine as a sidecar behind a token-protected localhost API, hosts the
//! UI in one window, and keeps crawls going, and visible, in the tray while the window is hidden.

// No console window in release builds on Windows.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod commands;
mod context_menu;
mod live;
mod logging;
mod open_files;
mod profile;
mod quit;
mod settings;
mod shell;
mod sidecar;
mod startup;
mod sync;
mod tray;
#[cfg(windows)]
mod webview2;
mod window;

use tauri::{AppHandle, Manager, RunEvent};

use shell::Shell;

fn main() {
    let arguments: Vec<String> = std::env::args_os().map(|argument| argument.to_string_lossy().into_owned()).collect();
    // Started by a copy that was quitting: once that one has gone, single instance lets this one through.
    open_files::wait_for_earlier_copy(&arguments);

    let context = tauri::generate_context!();
    let identifier = context.config().identifier.clone();
    let mut builder = tauri::Builder::default();
    // First, so a second launch hands over to this one before anything else starts. A test run of the built app
    // stays out of it, so it runs beside the user's own.
    if profile::test_profile().is_none() {
        builder = builder.plugin(tauri_plugin_single_instance::init(open_files::on_second_launch));
    }
    let built = builder
        .plugin(logging::plugin())
        .plugin(
            tauri_plugin_window_state::Builder::new()
                .with_state_flags(window::state_flags())
                .with_filename(profile::window_state_file())
                .build(),
        )
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_notification::init())
        .plugin(tauri_plugin_opener::init())
        // For the shell's own settings (shell.json) only: the webview isn't granted the store.
        .plugin(tauri_plugin_store::Builder::new().build())
        .invoke_handler(tauri::generate_handler![
            commands::sidecar_state,
            commands::restart_sidecar,
            commands::request_quit,
            commands::shell_info,
            commands::take_pending_open_files,
            commands::settings_read,
            commands::settings_write,
            commands::reveal_crawl_file,
        ])
        .on_window_event(window::on_event)
        .setup({
            let identifier = identifier.clone();
            move |app| {
                let handle = app.handle();
                let launched_with =
                    std::env::current_dir().map(|cwd| open_files::crawl_paths(&arguments, &cwd)).unwrap_or_default();
                let shell = Shell::new(handle, launched_with);
                let sidecar = shell.sidecar.clone();
                live::start(handle.clone(), &shell);
                app.manage(shell);
                // The engine starts on a thread of its own while the window and the page load.
                sidecar.start();
                log::info!("Shouting Iguana {} started", handle.package_info().version);
                // A webview that can't be made (a broken WebView2 runtime, a profile it can't write) ends the app
                // here, saying so: an error returned from setup, Tauri turns into a panic, and the app just vanishes.
                if let Err(error) = window::create(handle) {
                    startup::failed(&identifier, &error);
                }
                Ok(())
            }
        })
        .build(context);
    match built {
        Ok(app) => app.run(on_run_event),
        // A plugin that couldn't start (the log's folder can't be made, say): the same.
        Err(error) => startup::failed(&identifier, &error),
    }
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
