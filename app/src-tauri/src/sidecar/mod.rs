//! The engine runs as a sidecar process that only Rust starts and stops: the webview can't spawn anything. This
//! module owns its lifecycle (start, ready line, restarts, graceful stop) and tells the webview through one state.

#[cfg(windows)]
mod job;
mod launch;
mod process;
mod ready;
mod restart;
mod state;
mod stderr;

use std::fmt;
use std::io::{self, BufRead, BufReader, Read};
use std::path::PathBuf;
use std::process::ExitStatus;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Condvar, Mutex, OnceLock, PoisonError};
use std::thread;
use std::time::{Duration, Instant};

use tauri::{AppHandle, Emitter};

pub use launch::WEBVIEW_ORIGIN;
pub use state::{SidecarState, SidecarStatePayload};

use crate::sync::lock;
use crate::window::MAIN_WINDOW;
use launch::{
    DATA_DIR_OVERRIDE, EXTRACT_DIR_VARIABLE, ORIGINS_VARIABLE, PARENT_VARIABLE, STATE_DIR_OVERRIDE, TOKEN_VARIABLE,
};
use process::SidecarProcess;
use ready::ReadyLine;
use restart::{Decision, Exit, RestartPolicy};
use state::{FailureReason, RestartReason};
use stderr::{describe_exit, ExitReport, StderrTail};

pub const STATE_EVENT: &str = "sidecar://state";

/// The log target for the sidecar's own stderr lines.
pub const SIDECAR_LOG: &str = "sidecar";

/// The sidecar bounds its graceful shutdown at 30 s and asks the shell to allow at least that before killing it
/// (docs/v2-sidecar-api.md, "Process (E1)", Shutdown), so: 30 s, plus a margin for the process to exit.
pub const GRACEFUL_SHUTDOWN_TIMEOUT: Duration = Duration::from_secs(30 + 5);

/// A sidecar that hasn't written its ready line by then is killed, and counts as a failed start.
const START_TIMEOUT: Duration = Duration::from_secs(20);

/// A sidecar that isn't serving yet has no crawls to stop, so at quit it gets this long rather than the graceful
/// shutdown time. So does one still running at exit, the last safety net.
const BRIEF_SHUTDOWN_TIMEOUT: Duration = Duration::from_secs(2);

/// How long to wait for a killed sidecar's exit to be seen.
const KILL_TIMEOUT: Duration = Duration::from_secs(2);

/// How long an exit waits for the last lines on stdout and stderr: the reason it stopped is often the last of them.
const STREAMS_TIMEOUT: Duration = Duration::from_secs(1);

/// The longest line read from the sidecar's stdout or stderr. A longer one is split.
const MAX_LINE: u64 = 64 * 1024;

/// What a client needs to reach the sidecar while it is ready.
#[derive(Clone, PartialEq, Eq)]
pub struct Connection {
    pub generation: u32,
    pub port: u16,
    pub token: String,
}

impl fmt::Debug for Connection {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        formatter
            .debug_struct("Connection")
            .field("generation", &self.generation)
            .field("port", &self.port)
            .finish_non_exhaustive()
    }
}

/// The current connection, for threads that follow the sidecar (the tray's event stream). Closed for good when the
/// app quits.
#[derive(Default)]
pub struct Link {
    state: Mutex<LinkState>,
    changed: Condvar,
}

#[derive(Debug, PartialEq, Eq)]
pub enum Next {
    Connected(Connection),
    /// The followed sidecar has gone, and no other is ready yet.
    Gone,
    /// The app is quitting.
    Closed,
}

#[derive(Default)]
struct LinkState {
    connection: Option<Connection>,
    closed: bool,
}

impl Link {
    pub fn current(&self) -> Option<Connection> {
        lock(&self.state).connection.clone()
    }

    pub fn is_current(&self, generation: u32) -> bool {
        lock(&self.state).connection.as_ref().is_some_and(|connection| connection.generation == generation)
    }

    /// Waits for a sidecar to follow. `followed` is the generation followed until now: if it has gone and no other
    /// is ready, that is said once (`Gone`). Until `retry_at`, the followed generation is held off after a failed
    /// attempt, but a new generation is taken at once.
    pub fn next(&self, followed: Option<u32>, retry_at: Option<Instant>) -> Next {
        let mut state = lock(&self.state);
        loop {
            if state.closed {
                return Next::Closed;
            }
            let now = Instant::now();
            let wait = match &state.connection {
                None if followed.is_some() => return Next::Gone,
                None => None,
                Some(connection) if Some(connection.generation) == followed => match retry_at {
                    Some(at) if now < at => Some(at - now),
                    _ => return Next::Connected(connection.clone()),
                },
                Some(connection) => return Next::Connected(connection.clone()),
            };
            state = match wait {
                Some(timeout) => self
                    .changed
                    .wait_timeout(state, timeout)
                    .map_or_else(|poison| poison.into_inner().0, |(state, _)| state),
                None => self.changed.wait(state).unwrap_or_else(PoisonError::into_inner),
            };
        }
    }

    fn publish(&self, connection: Option<Connection>) {
        lock(&self.state).connection = connection;
        self.changed.notify_all();
    }

    fn close(&self) {
        let mut state = lock(&self.state);
        state.closed = true;
        state.connection = None;
        self.changed.notify_all();
    }
}

pub struct Sidecar {
    app: AppHandle,
    link: Arc<Link>,
    inner: Mutex<Inner>,
}

struct Inner {
    seq: u64,
    generation: u32,
    published: SidecarStatePayload,
    /// The current process, from spawn until its exit has been handled.
    launch: Option<Arc<Launch>>,
    restart_pending: bool,
    policy: RestartPolicy,
    quitting: bool,
}

/// One start of the sidecar, shared by the threads that watch it.
struct Launch {
    generation: u32,
    process: SidecarProcess,
    token: String,
    started: Instant,
    /// When it wrote its ready line.
    ready_at: OnceLock<Instant>,
    timed_out: AtomicBool,
    stderr: Mutex<StderrTail>,
    open_streams: Mutex<u8>,
    streams_closed: Condvar,
}

impl Launch {
    fn is_ready(&self) -> bool {
        self.ready_at.get().is_some()
    }

    fn stream_closed(&self) {
        let mut open = lock(&self.open_streams);
        *open = open.saturating_sub(1);
        self.streams_closed.notify_all();
    }

    fn wait_for_streams(&self, timeout: Duration) {
        let open = lock(&self.open_streams);
        let _ = self.streams_closed.wait_timeout_while(open, timeout, |open| *open > 0);
    }
}

impl Sidecar {
    pub fn new(app: AppHandle) -> Self {
        Self {
            app,
            link: Arc::default(),
            inner: Mutex::new(Inner {
                seq: 0,
                generation: 0,
                published: SidecarStatePayload { state: SidecarState::Starting, seq: 0, generation: 0 },
                launch: None,
                restart_pending: false,
                policy: RestartPolicy::default(),
                quitting: false,
            }),
        }
    }

    pub fn link(&self) -> &Arc<Link> {
        &self.link
    }

    pub fn state(&self) -> SidecarStatePayload {
        lock(&self.inner).published.clone()
    }

    /// Starts the first sidecar, off the calling thread: creating a 128 MB process waits for antivirus to scan it,
    /// half a second or more after every install, and the window mustn't wait for that.
    pub fn start(self: &Arc<Self>) {
        let generation = {
            let mut inner = lock(&self.inner);
            let generation = next_generation(&mut inner);
            self.publish(&mut inner, generation, SidecarState::Starting);
            generation
        };
        self.spawn_in_background(generation);
    }

    /// The UI's "Try again": a fresh start with the restart counters cleared. Does nothing unless the engine failed.
    pub fn retry(self: &Arc<Self>) -> bool {
        let generation = {
            let mut inner = lock(&self.inner);
            let failed = matches!(inner.published.state, SidecarState::Failed { .. });
            if !failed || inner.quitting || inner.restart_pending || inner.launch.is_some() {
                return false;
            }
            inner.policy.reset();
            let generation = next_generation(&mut inner);
            self.publish(&mut inner, generation, SidecarState::Starting);
            generation
        };
        log::info!("Starting the engine again, as asked");
        self.spawn_in_background(generation);
        true
    }

    /// Stops the engine and starts it again, as a new generation: the data folder changed. The webview hears
    /// `starting`, then `ready` with the new port and token. False if the app is quitting or a restart is already
    /// due. Blocks while the old engine stops (it finishes what it was writing), so never on the UI thread.
    pub fn relaunch(self: &Arc<Self>) -> bool {
        let (old, generation) = {
            let mut inner = lock(&self.inner);
            if inner.quitting || inner.restart_pending {
                return false;
            }
            // Its exit is no crash: once it isn't the current launch, its watchers let it go without a word.
            let old = inner.launch.take();
            inner.policy.reset();
            let generation = next_generation(&mut inner);
            self.publish(&mut inner, generation, SidecarState::Starting);
            (old, generation)
        };
        self.link.publish(None);
        if let Some(old) = old {
            log::info!("Stopping the engine (generation {}) to start it again", old.generation);
            stop_process(&old.process, GRACEFUL_SHUTDOWN_TIMEOUT);
        }
        self.spawn(generation);
        true
    }

    /// From here on the app is quitting: no restarts, and the event stream closes. With `announce`, the webview
    /// hears `stopping` and says it is stopping crawls.
    pub fn begin_quit(&self, announce: bool) {
        {
            let mut inner = lock(&self.inner);
            inner.quitting = true;
            if announce {
                let generation = inner.generation;
                self.publish(&mut inner, generation, SidecarState::Stopping);
            }
        }
        self.link.close();
    }

    /// Asks the sidecar to shut down and waits for it, killing it after `timeout`. Blocks, so never on the UI thread.
    pub fn stop(&self, timeout: Duration) -> Option<ExitStatus> {
        let launch = {
            let mut inner = lock(&self.inner);
            inner.quitting = true;
            inner.launch.clone()
        };
        self.link.close();
        let launch = launch?;
        let timeout = if launch.is_ready() { timeout } else { timeout.min(BRIEF_SHUTDOWN_TIMEOUT) };
        stop_process(&launch.process, timeout)
    }

    /// The last safety net, as the app exits: a sidecar still running gets a moment to stop, then is killed.
    pub fn stop_at_exit(&self) {
        let launch = {
            let mut inner = lock(&self.inner);
            inner.quitting = true;
            inner.launch.clone()
        };
        self.link.close();
        if let Some(launch) = launch.filter(|launch| !launch.process.has_exited()) {
            log::warn!("The engine was still running at exit: stopping it");
            stop_process(&launch.process, BRIEF_SHUTDOWN_TIMEOUT);
        }
    }

    /// Spawns on a thread of its own; on the calling thread only if no thread can be started.
    fn spawn_in_background(self: &Arc<Self>, generation: u32) {
        let this = Arc::clone(self);
        let started =
            thread::Builder::new().name(format!("sidecar-spawn-{generation}")).spawn(move || this.spawn(generation));
        if let Err(error) = started {
            log::warn!("Couldn't start a thread to start the engine ({error}): starting it here");
            self.spawn(generation);
        }
    }

    fn spawn(self: &Arc<Self>, generation: u32) {
        let binary = std::env::current_exe().ok().and_then(|shell| launch::binary_path(&shell));
        let Some(binary) = binary.filter(|binary| binary.is_file()) else {
            self.fail_now(generation, FailureReason::MissingBinary, &ExitReport::default());
            return;
        };
        let token = match launch::new_token() {
            Ok(token) => token,
            Err(error) => {
                self.spawn_failed(generation, format!("no random token ({error})"));
                return;
            }
        };
        let mut environment = vec![
            (TOKEN_VARIABLE, token.clone().into()),
            (ORIGINS_VARIABLE, launch::allowed_origins(self.app.config().build.dev_url.as_ref()).into()),
            (PARENT_VARIABLE, std::process::id().to_string().into()),
        ];
        if let Some(folder) = self.extraction_folder() {
            environment.push((EXTRACT_DIR_VARIABLE, folder.into_os_string()));
        }
        // The environment's folder (tests, development) first, then the one the user chose (data_folder.rs).
        let data_dir = launch::folder_override(DATA_DIR_OVERRIDE)
            .or_else(|| crate::data_folder::for_engine(&self.app).map(PathBuf::into_os_string));
        let arguments = launch::arguments(data_dir, launch::folder_override(STATE_DIR_OVERRIDE));
        let (process, stdout, stderr) = match SidecarProcess::spawn(&binary, &arguments, &environment) {
            Ok(spawned) => spawned,
            Err(error) if error.kind() == io::ErrorKind::NotFound => {
                self.fail_now(generation, FailureReason::MissingBinary, &ExitReport::default());
                return;
            }
            Err(error) => {
                self.spawn_failed(generation, error.to_string());
                return;
            }
        };
        let launch = Arc::new(Launch {
            generation,
            process,
            token,
            started: Instant::now(),
            ready_at: OnceLock::new(),
            timed_out: AtomicBool::new(false),
            stderr: Mutex::default(),
            open_streams: Mutex::new(2),
            streams_closed: Condvar::new(),
        });
        log::info!("Started the engine (pid {}, generation {generation})", launch.process.pid());
        {
            let mut inner = lock(&self.inner);
            inner.launch = Some(Arc::clone(&launch));
            if inner.quitting {
                // The app began quitting while this one was being started: it stops as soon as it serves.
                let _ = launch.process.request_shutdown();
            }
        }
        self.watch(&launch, stdout, stderr);
    }

    /// Where the single-file sidecar unpacks its native libraries: a folder of the app's own, per user and per app
    /// version, rather than %TEMP%, which application-control policies often block DLLs in and nothing ever cleans.
    /// Other versions' folders are removed the first time. None (so .NET's default) if there's no such folder.
    fn extraction_folder(&self) -> Option<PathBuf> {
        static PREPARED: OnceLock<Option<PathBuf>> = OnceLock::new();
        PREPARED
            .get_or_init(|| {
                let Some(root) =
                    crate::profile::local_data_folder(&self.app).map(|folder| folder.join(launch::EXTRACT_FOLDER))
                else {
                    log::warn!("The app has no local data folder to unpack the engine in");
                    return None;
                };
                let version = self.app.package_info().version.to_string();
                match launch::prepare_extraction_folder(&root, &version) {
                    Ok(folder) => Some(folder),
                    Err(error) => {
                        log::warn!(
                            "Couldn't make {}: the engine unpacks into the temp folder ({error})",
                            root.display()
                        );
                        None
                    }
                }
            })
            .clone()
    }

    /// Four threads per sidecar: the ready line on stdout, the log on stderr, the exit, and the start timeout.
    fn watch(
        self: &Arc<Self>,
        launch: &Arc<Launch>,
        stdout: impl Read + Send + 'static,
        stderr: impl Read + Send + 'static,
    ) {
        let generation = launch.generation;
        let (this, watched) = (Arc::clone(self), Arc::clone(launch));
        spawn_thread(format!("sidecar-stdout-{generation}"), move || {
            read_lines(stdout, |line| match ready::parse(line) {
                Some(ready) => this.on_ready(&watched, ready),
                None => log::info!(target: SIDECAR_LOG, "Ignored a line on the engine's stdout: {line}"),
            });
            watched.stream_closed();
        });
        let watched = Arc::clone(launch);
        spawn_thread(format!("sidecar-stderr-{generation}"), move || {
            read_lines(stderr, |line| {
                log::debug!(target: SIDECAR_LOG, "{line}");
                lock(&watched.stderr).push(line);
            });
            watched.stream_closed();
        });
        let (this, watched) = (Arc::clone(self), Arc::clone(launch));
        spawn_thread(format!("sidecar-exit-{generation}"), move || {
            let status = watched.process.watch();
            watched.wait_for_streams(STREAMS_TIMEOUT);
            this.on_exit(&watched, status);
        });
        let watched = Arc::clone(launch);
        spawn_thread(format!("sidecar-start-timeout-{generation}"), move || {
            if watched.process.wait(START_TIMEOUT).is_none() && !watched.is_ready() {
                watched.timed_out.store(true, Ordering::SeqCst);
                log::warn!("The engine didn't say it was ready within {} s: killing it", START_TIMEOUT.as_secs());
                watched.process.kill();
            }
        });
    }

    fn on_ready(&self, launch: &Launch, ready: ReadyLine) {
        if launch.ready_at.set(Instant::now()).is_err() {
            return;
        }
        let mut inner = lock(&self.inner);
        if !is_current(&inner, launch) || inner.quitting {
            return;
        }
        log::info!(
            "The engine is ready on port {} after {} ms (version {})",
            ready.port,
            launch.started.elapsed().as_millis(),
            ready.version
        );
        let state = SidecarState::Ready { port: ready.port, token: launch.token.clone(), version: ready.version };
        self.publish(&mut inner, launch.generation, state);
        self.link.publish(Some(Connection {
            generation: launch.generation,
            port: ready.port,
            token: launch.token.clone(),
        }));
    }

    fn on_exit(self: &Arc<Self>, launch: &Launch, status: ExitStatus) {
        let code = status.code();
        let mut inner = lock(&self.inner);
        if !is_current(&inner, launch) {
            return;
        }
        inner.launch = None;
        self.link.publish(None);
        if inner.quitting {
            log::info!("The engine stopped ({}) as the app quits", describe_exit(code));
            return;
        }
        let (reason, tail) = {
            let stderr = lock(&launch.stderr);
            (stderr.reason().map(str::to_owned), stderr.lines().collect::<Vec<_>>().join("\n"))
        };
        let report =
            ExitReport { code, timed_out: launch.timed_out.load(Ordering::SeqCst), reason, ..ExitReport::default() };
        log::warn!(
            "The engine stopped unexpectedly ({}){}",
            describe_exit(code),
            report.reason.as_deref().map(|reason| format!(": {reason}")).unwrap_or_default()
        );
        // Its stderr is otherwise kept only while developing, and the cause of a crash is in it.
        if !tail.is_empty() {
            log::warn!(target: SIDECAR_LOG, "The engine's last lines on stderr:\n{tail}");
        }
        let exit = Exit { code, ready_for: launch.ready_at.get().map(Instant::elapsed) };
        self.after_exit(&mut inner, launch.generation, exit, report);
    }

    fn spawn_failed(self: &Arc<Self>, generation: u32, error: String) {
        log::error!("Couldn't start the engine: {error}");
        let mut inner = lock(&self.inner);
        if inner.quitting {
            return;
        }
        let report = ExitReport { spawn_error: Some(error), ..ExitReport::default() };
        self.after_exit(&mut inner, generation, Exit { code: None, ready_for: None }, report);
    }

    fn after_exit(self: &Arc<Self>, inner: &mut Inner, generation: u32, exit: Exit, mut report: ExitReport) {
        match inner.policy.decide(exit) {
            Decision::Fail(reason) => {
                report.crashes = inner.policy.crashes();
                self.fail(inner, generation, reason, &report);
            }
            Decision::Restart { attempt, attempts, delay } => {
                // Every retry is said, and why, so the UI never sits on "starting" while starts fail behind it.
                let reason = restart_reason(exit, &report);
                let state = SidecarState::Restarting { attempt, attempts, reason, exit_code: exit.code };
                self.publish(inner, generation, state);
                log::info!(
                    "Starting the engine again in {} ms (restart {attempt} of {attempts}, {reason:?})",
                    delay.as_millis()
                );
                self.schedule(inner, generation, delay, report);
            }
        }
    }

    fn schedule(self: &Arc<Self>, inner: &mut Inner, generation: u32, delay: Duration, report: ExitReport) {
        inner.restart_pending = true;
        let this = Arc::clone(self);
        let scheduled = spawn_thread("sidecar-restart".to_owned(), move || {
            thread::sleep(delay);
            let generation = {
                let mut inner = lock(&this.inner);
                inner.restart_pending = false;
                if inner.quitting {
                    return;
                }
                next_generation(&mut inner)
            };
            this.spawn(generation);
        });
        if !scheduled {
            inner.restart_pending = false;
            self.fail(inner, generation, FailureReason::StartFailed, &report);
        }
    }

    fn fail_now(&self, generation: u32, reason: FailureReason, report: &ExitReport) {
        let mut inner = lock(&self.inner);
        if !inner.quitting {
            self.fail(&mut inner, generation, reason, report);
        }
    }

    fn fail(&self, inner: &mut Inner, generation: u32, reason: FailureReason, report: &ExitReport) {
        let message = stderr::failure_message(reason, report, START_TIMEOUT);
        log::error!("The engine can't run ({reason:?}): {message}");
        let state = SidecarState::Failed { reason, exit_code: report.code, message };
        self.publish(inner, generation, state);
    }

    /// Every state goes out under the lock, so the webview gets them in order; `seq` covers the command racing them.
    fn publish(&self, inner: &mut Inner, generation: u32, state: SidecarState) {
        inner.seq += 1;
        inner.published = SidecarStatePayload { state, seq: inner.seq, generation };
        log::debug!("Engine state {}: {:?} (generation {generation})", inner.seq, inner.published.state);
        if let Err(error) = self.app.emit_to(MAIN_WINDOW, STATE_EVENT, &inner.published) {
            log::warn!("Couldn't send the engine's state to the window: {error}");
        }
    }
}

fn restart_reason(exit: Exit, report: &ExitReport) -> RestartReason {
    if exit.ready_for.is_some() {
        RestartReason::Crashed
    } else if report.timed_out {
        RestartReason::StartTimedOut
    } else {
        RestartReason::StartFailed
    }
}

/// The data folder is set by the shell's environment (tests, development): the user's choice doesn't apply.
pub fn data_dir_overridden() -> bool {
    launch::folder_override(DATA_DIR_OVERRIDE).is_some()
}

fn next_generation(inner: &mut Inner) -> u32 {
    inner.generation += 1;
    inner.generation
}

fn is_current(inner: &Inner, launch: &Launch) -> bool {
    inner.launch.as_ref().is_some_and(|current| current.generation == launch.generation)
}

fn stop_process(process: &SidecarProcess, timeout: Duration) -> Option<ExitStatus> {
    if let Err(error) = process.request_shutdown() {
        log::debug!("Couldn't write the shutdown line: {error}");
    }
    if let Some(status) = process.wait(timeout) {
        return Some(status);
    }
    log::warn!("The engine didn't stop within {} s: killing it", timeout.as_secs());
    process.kill();
    process.wait(KILL_TIMEOUT)
}

/// Reads lines until the stream ends, lossily as UTF-8. Blank lines are skipped.
fn read_lines(stream: impl Read, mut on_line: impl FnMut(&str)) {
    let mut reader = BufReader::new(stream);
    let mut line = Vec::new();
    loop {
        line.clear();
        match (&mut reader).take(MAX_LINE).read_until(b'\n', &mut line) {
            Ok(0) => return,
            Ok(_) => {
                let text = String::from_utf8_lossy(&line);
                let text = text.trim_end_matches(['\r', '\n']);
                if !text.trim().is_empty() {
                    on_line(text);
                }
            }
            Err(error) if error.kind() == io::ErrorKind::Interrupted => {}
            Err(_) => return,
        }
    }
}

fn spawn_thread(name: String, work: impl FnOnce() + Send + 'static) -> bool {
    match thread::Builder::new().name(name).spawn(work) {
        Ok(_) => true,
        Err(error) => {
            log::error!("Couldn't start a thread to watch the engine: {error}");
            false
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn lines_are_split_on_any_ending_and_blanks_dropped() {
        let mut lines = Vec::new();
        read_lines(&b"first\r\n\nsecond\n   \nthird"[..], |line| lines.push(line.to_owned()));
        assert_eq!(lines, ["first", "second", "third"]);
    }

    #[test]
    fn an_endless_line_is_split_rather_than_buffered() {
        let long = vec![b'x'; MAX_LINE as usize + 10];
        let mut lengths = Vec::new();
        read_lines(&long[..], |line| lengths.push(line.len()));
        assert_eq!(lengths, [MAX_LINE as usize, 10]);
    }

    #[test]
    fn the_link_hands_over_new_generations_at_once_and_holds_off_a_failed_one() {
        let link = Link::default();
        let first = Connection { generation: 1, port: 1, token: "a".into() };
        link.publish(Some(first.clone()));
        assert_eq!(link.next(None, None), Next::Connected(first.clone()));

        let started = Instant::now();
        assert_eq!(link.next(Some(1), Some(started + Duration::from_millis(60))), Next::Connected(first));
        assert!(started.elapsed() >= Duration::from_millis(50));

        let second = Connection { generation: 2, port: 2, token: "b".into() };
        link.publish(Some(second.clone()));
        let started = Instant::now();
        assert_eq!(link.next(Some(1), Some(started + Duration::from_secs(10))), Next::Connected(second));
        assert!(started.elapsed() < Duration::from_secs(1));

        link.publish(None);
        assert_eq!(link.next(Some(2), None), Next::Gone);
        link.close();
        assert_eq!(link.next(None, None), Next::Closed);
    }

    #[test]
    fn a_waiting_follower_wakes_when_the_sidecar_is_ready() {
        let link = Arc::new(Link::default());
        let waiting = Arc::clone(&link);
        let follower = thread::spawn(move || waiting.next(None, None));
        thread::sleep(Duration::from_millis(20));
        link.publish(Some(Connection { generation: 3, port: 3, token: "c".into() }));
        assert!(matches!(follower.join().unwrap(), Next::Connected(connection) if connection.generation == 3));
    }

    #[test]
    fn a_follower_held_off_hears_at_once_that_its_sidecar_went() {
        let link = Arc::new(Link::default());
        link.publish(Some(Connection { generation: 4, port: 4, token: "d".into() }));
        let waiting = Arc::clone(&link);
        let started = Instant::now();
        let follower = thread::spawn(move || waiting.next(Some(4), Some(Instant::now() + Duration::from_secs(10))));
        thread::sleep(Duration::from_millis(20));
        link.publish(None);
        assert_eq!(follower.join().unwrap(), Next::Gone);
        assert!(started.elapsed() < Duration::from_secs(5));
    }

    #[test]
    fn connections_never_print_their_token() {
        let connection = Connection { generation: 1, port: 2, token: "secret-token".into() };
        assert!(!format!("{connection:?}").contains("secret-token"));
    }
}
