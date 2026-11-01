//! One sidecar process: spawned with piped stdio, watched until it exits, and stopped through its stdin lifeline.

use std::ffi::OsString;
use std::io::{self, Write};
use std::path::Path;
use std::process::{Child, ChildStderr, ChildStdin, ChildStdout, Command, ExitStatus, Stdio};
use std::sync::{Condvar, Mutex};
use std::time::Duration;

use crate::sync::lock;

/// The line that asks a sidecar started with `--stdin-lifeline` to shut down.
const SHUTDOWN: &[u8] = b"shutdown\n";

pub struct SidecarProcess {
    pid: u32,
    child: Mutex<Child>,
    /// Kept open for the process's lifetime: closing it is the lifeline's "the shell has gone".
    stdin: Mutex<Option<ChildStdin>>,
    status: Mutex<Option<ExitStatus>>,
    exited: Condvar,
}

impl SidecarProcess {
    /// Starts the sidecar without a console window and, on Windows, in the shell's kill-on-close job.
    pub fn spawn(
        binary: &Path,
        arguments: &[OsString],
        environment: &[(&str, OsString)],
    ) -> io::Result<(Self, ChildStdout, ChildStderr)> {
        let mut command = Command::new(binary);
        command
            .args(arguments)
            .envs(environment.iter().map(|(name, value)| (name, value)))
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped());
        #[cfg(windows)]
        {
            use std::os::windows::process::CommandExt;
            command.creation_flags(windows_sys::Win32::System::Threading::CREATE_NO_WINDOW);
        }
        let mut child = command.spawn()?;
        #[cfg(windows)]
        if let Err(error) = super::job::assign(&child) {
            // The stdin lifeline and the parent watch still stop it; the job is the backstop for a hung sidecar.
            log::warn!("Couldn't put the engine in the app's job object: {error}");
        }
        let pipes = (child.stdin.take(), child.stdout.take(), child.stderr.take());
        let (Some(stdin), Some(stdout), Some(stderr)) = pipes else {
            let _ = child.kill();
            let _ = child.wait();
            return Err(io::Error::other("the engine's standard streams weren't piped"));
        };
        let process = Self {
            pid: child.id(),
            child: Mutex::new(child),
            stdin: Mutex::new(Some(stdin)),
            status: Mutex::new(None),
            exited: Condvar::new(),
        };
        Ok((process, stdout, stderr))
    }

    pub fn pid(&self) -> u32 {
        self.pid
    }

    /// Blocks until the process exits, records how, and wakes anyone waiting. Run once, on its own thread.
    pub fn watch(&self) -> ExitStatus {
        #[cfg(windows)]
        {
            use std::os::windows::io::AsRawHandle;
            use windows_sys::Win32::System::Threading::{WaitForSingleObject, INFINITE};
            // Waiting on the process handle needs no lock and no polling. The handle stays valid while `self` lives,
            // and the caller holds it for the whole wait.
            let handle = lock(&self.child).as_raw_handle();
            unsafe { WaitForSingleObject(handle, INFINITE) };
        }
        let status = loop {
            match lock(&self.child).try_wait() {
                Ok(Some(status)) => break status,
                Ok(None) => {}
                Err(error) => log::warn!("Couldn't read the engine's exit status: {error}"),
            }
            // Elsewhere than Windows there is nothing to block on without holding the lock.
            std::thread::sleep(Duration::from_millis(50));
        };
        lock(&self.stdin).take();
        *lock(&self.status) = Some(status);
        self.exited.notify_all();
        status
    }

    /// Writes the shutdown line. The sidecar reads stdin once it serves, so a line sent while it starts still counts.
    pub fn request_shutdown(&self) -> io::Result<()> {
        match lock(&self.stdin).as_mut() {
            Some(stdin) => stdin.write_all(SHUTDOWN).and_then(|()| stdin.flush()),
            None => Err(io::Error::new(io::ErrorKind::BrokenPipe, "the engine has exited")),
        }
    }

    /// The exit status, waiting up to `timeout` for it.
    pub fn wait(&self, timeout: Duration) -> Option<ExitStatus> {
        let status = lock(&self.status);
        let (status, _) = self
            .exited
            .wait_timeout_while(status, timeout, |status| status.is_none())
            .unwrap_or_else(std::sync::PoisonError::into_inner);
        *status
    }

    pub fn has_exited(&self) -> bool {
        lock(&self.status).is_some()
    }

    pub fn kill(&self) {
        if let Err(error) = lock(&self.child).kill() {
            log::warn!("Couldn't kill the engine (pid {}): {error}", self.pid);
        }
    }
}
