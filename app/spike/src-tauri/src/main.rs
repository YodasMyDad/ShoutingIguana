// Throwaway Tauri shell for the step 3 stack spike: spawns the dummy sidecar, hands its port and token to the
// webview, restarts it if it dies, stops it on exit, and logs timings as JSON lines to $SPIKE_LOG.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use std::fs::{File, OpenOptions};
use std::io::Write;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Mutex, OnceLock};
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

use serde::Serialize;
use serde_json::{json, Value};
use tauri::{AppHandle, Emitter, Manager, RunEvent, State};
use tauri_plugin_shell::process::{CommandChild, CommandEvent};
use tauri_plugin_shell::ShellExt;

static LOG: OnceLock<Mutex<Option<File>>> = OnceLock::new();

fn epoch_ms() -> f64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs_f64() * 1000.0)
        .unwrap_or(0.0)
}

fn log_event(event: &str, mut fields: Value) {
    fields["event"] = json!(event);
    fields["epochMs"] = json!(epoch_ms());
    let line = fields.to_string();
    if let Some(lock) = LOG.get() {
        if let Ok(mut guard) = lock.lock() {
            if let Some(file) = guard.as_mut() {
                let _ = writeln!(file, "{line}");
                let _ = file.flush();
            }
        }
    }
}

#[derive(Clone, Serialize)]
struct Connection {
    port: u16,
    token: String,
    generation: u32,
}

#[derive(Default)]
struct Slot {
    connection: Option<Connection>,
    child: Option<CommandChild>,
    generation: u32,
    restarts: u32,
}

struct Spike {
    slot: Mutex<Slot>,
    exiting: AtomicBool,
    terminated: AtomicBool,
    /// Comma list of "ppid", "stdin", "job", or "none".
    orphan_fix: String,
    /// "graceful" (stdin shutdown, then kill), "kill", or "none" (leave it to the plugin).
    exit_mode: String,
}

fn new_token() -> String {
    let mut bytes = [0u8; 32];
    getrandom::fill(&mut bytes).expect("the OS random generator failed");
    bytes.iter().map(|b| format!("{b:02x}")).collect()
}

fn allowed_origins(app: &AppHandle) -> String {
    let mut origins = vec![
        "http://tauri.localhost".to_string(),
        "https://tauri.localhost".to_string(),
        "tauri://localhost".to_string(),
    ];
    if cfg!(debug_assertions) {
        if let Some(url) = &app.config().build.dev_url {
            origins.push(url.origin().ascii_serialization());
        }
    }
    origins.join(",")
}

#[tauri::command]
fn sidecar_connection(spike: State<'_, Spike>) -> Option<Connection> {
    spike.slot.lock().unwrap().connection.clone()
}

#[tauri::command]
fn report(name: String, value: Value) {
    log_event("webview", json!({ "name": name, "value": value }));
}

fn spawn_sidecar(app: &AppHandle) -> Result<(), String> {
    let spike = app.state::<Spike>();
    let token = new_token();
    let fix = spike.orphan_fix.clone();
    let mut command = app
        .shell()
        .sidecar("spike-sidecar")
        .map_err(|e| e.to_string())?
        .env("SI_TOKEN", &token)
        .env("SI_ALLOWED_ORIGINS", allowed_origins(app))
        .env("SI_WATCH_STDIN", if fix.contains("stdin") { "1" } else { "0" });
    if fix.contains("ppid") {
        command = command.env("SI_PARENT_PID", std::process::id().to_string());
    }
    let spawned_at = Instant::now();
    let (mut events, child) = command.spawn().map_err(|e| e.to_string())?;
    let pid = child.pid();
    #[cfg(windows)]
    let in_job = fix.contains("job") && job::assign(pid);
    #[cfg(not(windows))]
    let in_job = false;
    let generation = {
        let mut slot = spike.slot.lock().unwrap();
        slot.generation += 1;
        slot.child = Some(child);
        slot.connection = None;
        slot.generation
    };
    spike.terminated.store(false, Ordering::SeqCst);
    log_event(
        "sidecar-spawned",
        json!({ "pid": pid, "generation": generation, "orphanFix": fix, "inJob": in_job }),
    );

    let app = app.clone();
    tauri::async_runtime::spawn(async move {
        while let Some(event) = events.recv().await {
            match event {
                CommandEvent::Stdout(bytes) => {
                    for line in String::from_utf8_lossy(&bytes).lines() {
                        let line = line.trim();
                        if !line.is_empty() {
                            on_stdout_line(&app, line, generation, &token, spawned_at);
                        }
                    }
                }
                CommandEvent::Stderr(bytes) => {
                    for line in String::from_utf8_lossy(&bytes).lines() {
                        let line = line.trim();
                        if !line.is_empty() {
                            let entry: Value = serde_json::from_str(line).unwrap_or_else(|_| json!({ "text": line }));
                            log_event("sidecar-log", json!({ "generation": generation, "entry": entry }));
                        }
                    }
                }
                CommandEvent::Terminated(payload) => {
                    log_event(
                        "sidecar-terminated",
                        json!({ "generation": generation, "code": payload.code }),
                    );
                    on_terminated(&app, generation);
                    break;
                }
                CommandEvent::Error(error) => {
                    log_event("sidecar-error", json!({ "generation": generation, "error": error }))
                }
                _ => {}
            }
        }
    });
    Ok(())
}

fn on_stdout_line(app: &AppHandle, line: &str, generation: u32, token: &str, spawned_at: Instant) {
    let Ok(message) = serde_json::from_str::<Value>(line) else {
        log_event("sidecar-stdout-ignored", json!({ "line": line }));
        return;
    };
    if message["event"] != "ready" {
        return;
    }
    let Some(port) = message["port"].as_u64().and_then(|p| u16::try_from(p).ok()) else {
        log_event("sidecar-ready-invalid", json!({ "line": line }));
        return;
    };
    let connection = Connection { port, token: token.to_string(), generation };
    {
        let spike = app.state::<Spike>();
        let mut slot = spike.slot.lock().unwrap();
        if slot.generation != generation {
            return;
        }
        slot.connection = Some(connection.clone());
    }
    log_event(
        "sidecar-ready",
        json!({ "generation": generation, "port": port, "spawnToReadyMs": spawned_at.elapsed().as_secs_f64() * 1000.0 }),
    );
    let _ = app.emit_to("main", "sidecar://ready", connection);
}

fn on_terminated(app: &AppHandle, generation: u32) {
    let spike = app.state::<Spike>();
    spike.terminated.store(true, Ordering::SeqCst);
    let restart = {
        let mut slot = spike.slot.lock().unwrap();
        if slot.generation != generation {
            return;
        }
        slot.child = None;
        slot.connection = None;
        let restart = !spike.exiting.load(Ordering::SeqCst) && slot.restarts < 5;
        if restart {
            slot.restarts += 1;
        }
        restart
    };
    let _ = app.emit_to("main", "sidecar://lost", generation);
    if restart {
        let app = app.clone();
        tauri::async_runtime::spawn_blocking(move || {
            std::thread::sleep(Duration::from_millis(250));
            if let Err(error) = spawn_sidecar(&app) {
                log_event("sidecar-restart-failed", json!({ "error": error }));
            }
        });
    }
}

fn stop_sidecar(app: &AppHandle) {
    let spike = app.state::<Spike>();
    spike.exiting.store(true, Ordering::SeqCst);
    let child = spike.slot.lock().unwrap().child.take();
    let Some(mut child) = child else {
        return;
    };
    let pid = child.pid();
    match spike.exit_mode.as_str() {
        "none" => {
            log_event("exit-sidecar-left", json!({ "pid": pid }));
            std::mem::forget(child); // no kill, and no stdin close: whatever the plugin does by itself
        }
        "kill" => {
            let ok = child.kill().is_ok();
            log_event("exit-sidecar-killed", json!({ "pid": pid, "ok": ok }));
        }
        _ => {
            let wrote = child.write(b"shutdown\n").is_ok();
            let started = Instant::now();
            while !spike.terminated.load(Ordering::SeqCst) && started.elapsed() < Duration::from_secs(2) {
                std::thread::sleep(Duration::from_millis(5));
            }
            let graceful = spike.terminated.load(Ordering::SeqCst);
            if !graceful {
                let _ = child.kill();
            }
            log_event(
                "exit-sidecar-stopped",
                json!({ "pid": pid, "wroteShutdown": wrote, "graceful": graceful, "waitedMs": started.elapsed().as_secs_f64() * 1000.0 }),
            );
        }
    }
}

#[cfg(windows)]
mod job {
    use std::sync::OnceLock;
    use windows_sys::Win32::Foundation::{CloseHandle, HANDLE};
    use windows_sys::Win32::System::JobObjects::{
        AssignProcessToJobObject, CreateJobObjectW, JobObjectExtendedLimitInformation, SetInformationJobObject,
        JOBOBJECT_EXTENDED_LIMIT_INFORMATION, JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
    };
    use windows_sys::Win32::System::Threading::{OpenProcess, PROCESS_SET_QUOTA, PROCESS_TERMINATE};

    // The handle is never closed: the kernel closes it when this process dies, however it dies, and that kills
    // every process in the job.
    static JOB: OnceLock<usize> = OnceLock::new();

    fn job() -> HANDLE {
        *JOB.get_or_init(|| unsafe {
            let handle = CreateJobObjectW(std::ptr::null(), std::ptr::null());
            let mut info: JOBOBJECT_EXTENDED_LIMIT_INFORMATION = std::mem::zeroed();
            info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            SetInformationJobObject(
                handle,
                JobObjectExtendedLimitInformation,
                &info as *const _ as *const core::ffi::c_void,
                std::mem::size_of::<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>() as u32,
            );
            handle as usize
        }) as HANDLE
    }

    pub fn assign(pid: u32) -> bool {
        unsafe {
            let process = OpenProcess(PROCESS_SET_QUOTA | PROCESS_TERMINATE, 0, pid);
            if process.is_null() {
                return false;
            }
            let ok = AssignProcessToJobObject(job(), process) != 0;
            CloseHandle(process);
            ok
        }
    }
}

/// Hides the window as if sent to the tray, optionally asking WebView2 to use less memory ("low") and/or suspending the
/// webview ("suspend"), then shows it again after `ms`. The crawl itself runs in the sidecar and is unaffected.
#[tauri::command]
fn spike_tray(app: AppHandle, mode: String, ms: u64) -> Result<(), String> {
    let window = app.get_webview_window("main").ok_or("no main window")?;
    window.hide().map_err(|e| e.to_string())?;
    log_event("tray-hidden", json!({ "mode": mode }));
    #[cfg(windows)]
    tray::enter(&window, &mode);
    std::thread::spawn(move || {
        std::thread::sleep(Duration::from_millis(ms));
        if let Some(window) = app.get_webview_window("main") {
            #[cfg(windows)]
            tray::leave(&window);
            let _ = window.show();
            log_event("tray-shown", json!({}));
        }
    });
    Ok(())
}

/// WebView2 memory controls Tauri doesn't expose, reached through webview2-com on the UI thread.
#[cfg(windows)]
mod tray {
    use super::log_event;
    use serde_json::json;
    use webview2_com::Microsoft::Web::WebView2::Win32::{
        ICoreWebView2_19, ICoreWebView2_3, COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL_LOW,
        COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL_NORMAL,
    };
    use webview2_com::TrySuspendCompletedHandler;
    use windows_core::Interface;

    pub fn enter(window: &tauri::WebviewWindow, mode: &str) {
        let mode = mode.to_string();
        let _ = window.with_webview(move |webview| unsafe {
            let controller = webview.controller();
            if mode.contains("low") {
                let result = controller
                    .CoreWebView2()
                    .and_then(|core| core.cast::<ICoreWebView2_19>())
                    .and_then(|core| core.SetMemoryUsageTargetLevel(COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL_LOW));
                log_event("tray-memory-target-low", json!({ "result": format!("{result:?}") }));
            }
            if mode.contains("invisible") && !mode.contains("suspend") {
                // Telling WebView2 the webview is not visible makes the page hidden (document.hidden), which stops
                // rendering and throttles timers, without suspending script.
                let visible = controller.SetIsVisible(false);
                log_event("tray-invisible", json!({ "result": format!("{visible:?}") }));
            }
            if mode.contains("suspend") {
                // TrySuspend requires the webview itself to be invisible, not just its window.
                let visible = controller.SetIsVisible(false);
                let handler = TrySuspendCompletedHandler::create(Box::new(|result, suspended| {
                    log_event(
                        "tray-suspend-completed",
                        json!({ "result": format!("{result:?}"), "suspended": format!("{suspended:?}") }),
                    );
                    Ok(())
                }));
                let result = controller
                    .CoreWebView2()
                    .and_then(|core| core.cast::<ICoreWebView2_3>())
                    .and_then(|core| core.TrySuspend(&handler));
                log_event(
                    "tray-suspend-requested",
                    json!({ "setIsVisible": format!("{visible:?}"), "result": format!("{result:?}") }),
                );
            }
        });
    }

    pub fn leave(window: &tauri::WebviewWindow) {
        let _ = window.with_webview(|webview| unsafe {
            let controller = webview.controller();
            if let Ok(core) = controller.CoreWebView2() {
                if let Ok(core3) = core.cast::<ICoreWebView2_3>() {
                    let _ = core3.Resume();
                }
                if let Ok(core19) = core.cast::<ICoreWebView2_19>() {
                    let _ = core19.SetMemoryUsageTargetLevel(COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL_NORMAL);
                }
            }
            let _ = controller.SetIsVisible(true);
        });
    }
}

fn main() {
    if let Ok(path) = std::env::var("SPIKE_LOG") {
        let file = OpenOptions::new().create(true).append(true).open(path).ok();
        let _ = LOG.set(Mutex::new(file));
    }
    log_event("rust-main", json!({ "pid": std::process::id() }));
    let orphan_fix = std::env::var("SPIKE_ORPHAN_FIX").unwrap_or_else(|_| "ppid,stdin,job".into());
    let exit_mode = std::env::var("SPIKE_EXIT_MODE").unwrap_or_else(|_| "graceful".into());

    tauri::Builder::default()
        .plugin(tauri_plugin_shell::init())
        .manage(Spike {
            slot: Mutex::new(Slot::default()),
            exiting: AtomicBool::new(false),
            terminated: AtomicBool::new(false),
            orphan_fix,
            exit_mode,
        })
        .invoke_handler(tauri::generate_handler![sidecar_connection, report, spike_tray])
        .on_page_load(|_webview, payload| {
            log_event(
                "page-load",
                json!({ "url": payload.url().to_string(), "phase": format!("{:?}", payload.event()) }),
            );
        })
        .setup(|app| {
            log_event("setup", json!({}));
            if let Err(error) = spawn_sidecar(app.handle()) {
                log_event("sidecar-spawn-failed", json!({ "error": error }));
            }
            // SPIKE_START_PAGE=hello.html opens an empty page in the same shell: WebView2's fixed memory floor.
            let page = std::env::var("SPIKE_START_PAGE").unwrap_or_else(|_| "index.html".into());
            // SPIKE_QUERY ("pos=top&overscan=4") reaches the page as window.__SPIKE_QUERY__ before any script runs.
            let query = std::env::var("SPIKE_QUERY").unwrap_or_default();
            tauri::WebviewWindowBuilder::new(app, "main", tauri::WebviewUrl::App(page.into()))
                .title("Shouting Iguana spike")
                .inner_size(1536.0, 864.0)
                .initialization_script(format!("window.__SPIKE_QUERY__ = {};", serde_json::to_string(&query)?))
                .build()?;
            Ok(())
        })
        .build(tauri::generate_context!())
        .expect("failed to build the Tauri app")
        .run(|app, event| match event {
            RunEvent::ExitRequested { .. } => log_event("exit-requested", json!({})),
            RunEvent::Exit => {
                stop_sidecar(app);
                log_event("exit", json!({}));
            }
            _ => {}
        });
}
