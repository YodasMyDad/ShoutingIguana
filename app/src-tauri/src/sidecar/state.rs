//! What the webview hears about the engine: one state at a time, numbered so the newest wins whichever way it
//! arrives (the `sidecar://state` event or the `sidecar_state` command).

use std::fmt;

use serde::Serialize;

/// Why the engine can't run. The UI keys its text and actions on it.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize)]
#[serde(rename_all = "kebab-case")]
pub enum FailureReason {
    /// The sidecar binary isn't next to the app.
    MissingBinary,
    /// The sidecar refused its arguments (exit code 2). That is the shell's bug, so retrying can't help.
    BadArguments,
    /// Another sidecar kept the state folder for longer than a previous instance takes to shut down (exit code 3).
    StateInUse,
    /// It never became ready, however often it was started.
    StartFailed,
    /// It became ready, then kept stopping.
    Crashed,
}

/// Why the engine is being started again. The UI says it in plain words.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize)]
#[serde(rename_all = "kebab-case")]
pub enum RestartReason {
    /// It was serving, then stopped.
    Crashed,
    /// It stopped before it was ready.
    StartFailed,
    /// It didn't say it was ready within the start timeout, so it was stopped.
    StartTimedOut,
}

#[derive(Clone, PartialEq, Serialize)]
#[serde(tag = "state", rename_all = "camelCase", rename_all_fields = "camelCase")]
pub enum SidecarState {
    Starting,
    Ready {
        port: u16,
        token: String,
        version: String,
    },
    /// The sidecar was lost, or didn't start, and a new one is on its way: restart `attempt` of at most `attempts`.
    Restarting {
        attempt: u32,
        attempts: u32,
        reason: RestartReason,
        exit_code: Option<i32>,
    },
    Failed {
        reason: FailureReason,
        exit_code: Option<i32>,
        /// A plain sentence for the UI.
        message: String,
    },
    /// The app is quitting: crawls are being stopped.
    Stopping,
}

/// Never shows the token, so a state can go in a log line.
impl fmt::Debug for SidecarState {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Starting => formatter.write_str("Starting"),
            Self::Ready { port, version, .. } => {
                formatter.debug_struct("Ready").field("port", port).field("version", version).finish_non_exhaustive()
            }
            Self::Restarting { attempt, attempts, reason, exit_code } => formatter
                .debug_struct("Restarting")
                .field("attempt", attempt)
                .field("attempts", attempts)
                .field("reason", reason)
                .field("exit_code", exit_code)
                .finish(),
            Self::Failed { reason, exit_code, message } => formatter
                .debug_struct("Failed")
                .field("reason", reason)
                .field("exit_code", exit_code)
                .field("message", message)
                .finish(),
            Self::Stopping => formatter.write_str("Stopping"),
        }
    }
}

/// The payload of `sidecar://state` and the result of `sidecar_state`.
#[derive(Clone, Debug, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SidecarStatePayload {
    #[serde(flatten)]
    pub state: SidecarState,
    /// Strictly increasing for the shell's lifetime: the webview keeps the highest it has seen.
    pub seq: u64,
    /// Which sidecar process the state is about. A new generation means a new port and token.
    pub generation: u32,
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    fn payload(state: SidecarState) -> serde_json::Value {
        serde_json::to_value(SidecarStatePayload { state, seq: 7, generation: 2 }).unwrap()
    }

    #[test]
    fn starting_and_stopping_carry_only_the_numbers() {
        assert_eq!(payload(SidecarState::Starting), json!({ "state": "starting", "seq": 7, "generation": 2 }));
        assert_eq!(payload(SidecarState::Stopping), json!({ "state": "stopping", "seq": 7, "generation": 2 }));
    }

    #[test]
    fn ready_is_flat_and_camel_case() {
        let ready = SidecarState::Ready { port: 53123, token: "ab".repeat(32), version: "2.0.0-alpha.1".into() };
        assert_eq!(
            payload(ready),
            json!({ "state": "ready", "port": 53123, "token": "ab".repeat(32), "version": "2.0.0-alpha.1", "seq": 7, "generation": 2 })
        );
    }

    #[test]
    fn restarting_says_which_attempt_and_why_with_a_missing_exit_code_as_null() {
        let restarting =
            SidecarState::Restarting { attempt: 2, attempts: 5, reason: RestartReason::Crashed, exit_code: None };
        assert_eq!(
            payload(restarting),
            json!({ "state": "restarting", "attempt": 2, "attempts": 5, "reason": "crashed", "exitCode": null, "seq": 7, "generation": 2 })
        );
        let reasons = [
            (RestartReason::Crashed, "crashed"),
            (RestartReason::StartFailed, "start-failed"),
            (RestartReason::StartTimedOut, "start-timed-out"),
        ];
        for (reason, name) in reasons {
            assert_eq!(serde_json::to_value(reason).unwrap(), json!(name));
        }
    }

    #[test]
    fn failed_names_its_reason_in_kebab_case() {
        let failed = SidecarState::Failed {
            reason: FailureReason::StateInUse,
            exit_code: Some(3),
            message: "Another copy is running.".into(),
        };
        assert_eq!(
            payload(failed),
            json!({ "state": "failed", "reason": "state-in-use", "exitCode": 3, "message": "Another copy is running.", "seq": 7, "generation": 2 })
        );
        let reasons = [
            (FailureReason::MissingBinary, "missing-binary"),
            (FailureReason::BadArguments, "bad-arguments"),
            (FailureReason::StartFailed, "start-failed"),
            (FailureReason::Crashed, "crashed"),
        ];
        for (reason, name) in reasons {
            assert_eq!(serde_json::to_value(reason).unwrap(), json!(name));
        }
    }

    #[test]
    fn debug_output_never_shows_the_token() {
        let ready = SidecarState::Ready { port: 1, token: "secret-token".into(), version: "v".into() };
        let text = format!("{ready:?}");
        assert!(!text.contains("secret-token"), "{text}");
        assert!(text.contains("port: 1"), "{text}");
    }
}
