//! The sidecar's stderr, kept for the message when it can't run, and the messages themselves.

use std::collections::VecDeque;
use std::time::Duration;

use super::launch::BINARY_NAME;
use super::state::FailureReason;

/// How many of the last stderr lines are kept.
pub const TAIL_LINES: usize = 50;

#[derive(Debug, Default)]
pub struct StderrTail {
    lines: VecDeque<String>,
}

impl StderrTail {
    pub fn push(&mut self, line: &str) {
        if self.lines.len() == TAIL_LINES {
            self.lines.pop_front();
        }
        self.lines.push_back(line.to_owned());
    }

    /// The sidecar's own sentence about why it stopped: when it can't start, it writes one plain line to stderr
    /// last. Log entries (JSON, or the console logger's "12:01:02 info: …") are skipped.
    pub fn reason(&self) -> Option<&str> {
        self.lines.iter().rev().map(|line| line.trim()).find(|line| !line.is_empty() && !is_log_entry(line))
    }
}

fn is_log_entry(line: &str) -> bool {
    if line.starts_with('{') {
        return true;
    }
    // "HH:mm:ss level: …", the .NET console logger's single-line format.
    let bytes = line.as_bytes();
    let timestamp = bytes.len() > 9
        && bytes[..8].iter().enumerate().all(|(index, byte)| {
            if index == 2 || index == 5 {
                *byte == b':'
            } else {
                byte.is_ascii_digit()
            }
        })
        && bytes[8] == b' ';
    timestamp && ["trce:", "dbug:", "info:", "warn:", "fail:", "crit:"].iter().any(|level| line[9..].starts_with(level))
}

/// What is known about a sidecar that couldn't run, for the failure message.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct ExitReport {
    pub code: Option<i32>,
    /// It never said it was ready, and was killed after the start timeout.
    pub timed_out: bool,
    /// It couldn't be spawned at all.
    pub spawn_error: Option<String>,
    /// Its last plain line on stderr.
    pub reason: Option<String>,
    /// Restarts within the last minute.
    pub restarts: usize,
}

/// A plain sentence for the UI.
pub fn failure_message(reason: FailureReason, report: &ExitReport, start_timeout: Duration) -> String {
    match reason {
        FailureReason::MissingBinary => {
            format!(
                "The engine ({BINARY_NAME}) is missing from the app's folder. Reinstall Shouting Iguana to restore it."
            )
        }
        FailureReason::BadArguments => match &report.reason {
            Some(reason) => sentence(format!("The engine couldn't start with the settings the app gave it: {reason}")),
            None => "The engine couldn't start with the settings the app gave it.".to_owned(),
        },
        FailureReason::StateInUse => match &report.reason {
            Some(reason) => sentence(reason.clone()),
            None => "Another copy of the engine is still using its folder. Close it, then try again.".to_owned(),
        },
        FailureReason::StartFailed => {
            if report.timed_out {
                format!("The engine didn't start within {} seconds.", start_timeout.as_secs())
            } else if let Some(error) = &report.spawn_error {
                sentence(format!("The engine couldn't be started: {error}"))
            } else if let Some(reason) = &report.reason {
                sentence(reason.clone())
            } else {
                format!("The engine stopped before it was ready ({}).", describe_exit(report.code))
            }
        }
        FailureReason::Crashed => format!(
            "The engine stopped unexpectedly {} times in a minute ({}).",
            report.restarts + 1,
            describe_exit(report.code)
        ),
    }
}

/// "exit code 1", or in hex for a Windows status such as an access violation (0xC0000005).
pub fn describe_exit(code: Option<i32>) -> String {
    match code {
        None => "no exit code".to_owned(),
        Some(code) if !(0..=0xffff).contains(&code) => format!("exit code 0x{:08X}", code as u32),
        Some(code) => format!("exit code {code}"),
    }
}

fn sentence(text: String) -> String {
    let text = text.trim_end();
    if text.ends_with(['.', '!', '?']) {
        text.to_owned()
    } else {
        format!("{text}.")
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const TIMEOUT: Duration = Duration::from_secs(20);

    #[test]
    fn the_tail_keeps_the_last_fifty_lines() {
        let mut tail = StderrTail::default();
        for index in 0..60 {
            tail.push(&format!("line {index}"));
        }
        assert_eq!(tail.lines.len(), TAIL_LINES);
        assert_eq!(tail.lines.front().map(String::as_str), Some("line 10"));
        assert_eq!(tail.reason(), Some("line 59"));
    }

    #[test]
    fn the_reason_skips_log_entries() {
        let mut tail = StderrTail::default();
        tail.push("Another Shouting Iguana engine is already using C:\\state. Close it, or give this one another --state-dir.");
        tail.push("12:01:02 info: ShoutingIguana.Sidecar.Hosting.StdinLifeline[0] Stdin ended");
        tail.push(r#"{"level":"info","message":"shutting down"}"#);
        tail.push("   ");
        assert_eq!(
            tail.reason(),
            Some("Another Shouting Iguana engine is already using C:\\state. Close it, or give this one another --state-dir.")
        );
    }

    #[test]
    fn a_timestamp_alone_is_not_a_log_entry() {
        assert!(!is_log_entry("12:01:02 The sidecar couldn't start"));
        assert!(is_log_entry("23:59:59 crit: Something[0] broke"));
        assert!(!is_log_entry("The sidecar couldn't start: Access to the path is denied."));
    }

    #[test]
    fn exit_codes_read_as_numbers_or_windows_statuses() {
        assert_eq!(describe_exit(Some(1)), "exit code 1");
        assert_eq!(describe_exit(Some(-1073741819)), "exit code 0xC0000005");
        assert_eq!(describe_exit(None), "no exit code");
    }

    #[test]
    fn messages_are_sentences() {
        let report = ExitReport {
            code: Some(1),
            reason: Some("The sidecar couldn't start: port in use".into()),
            ..ExitReport::default()
        };
        assert_eq!(
            failure_message(FailureReason::StartFailed, &report, TIMEOUT),
            "The sidecar couldn't start: port in use."
        );

        let timed_out = ExitReport { timed_out: true, ..report.clone() };
        assert_eq!(
            failure_message(FailureReason::StartFailed, &timed_out, TIMEOUT),
            "The engine didn't start within 20 seconds."
        );

        let spawn = ExitReport { spawn_error: Some("Access is denied. (os error 5)".into()), ..ExitReport::default() };
        assert_eq!(
            failure_message(FailureReason::StartFailed, &spawn, TIMEOUT),
            "The engine couldn't be started: Access is denied. (os error 5)."
        );

        let silent = ExitReport { code: Some(-1073741819), ..ExitReport::default() };
        assert_eq!(
            failure_message(FailureReason::StartFailed, &silent, TIMEOUT),
            "The engine stopped before it was ready (exit code 0xC0000005)."
        );

        let crashed = ExitReport { code: Some(-532462766), restarts: 5, ..ExitReport::default() };
        assert_eq!(
            failure_message(FailureReason::Crashed, &crashed, TIMEOUT),
            "The engine stopped unexpectedly 6 times in a minute (exit code 0xE0434352)."
        );

        let busy = ExitReport { code: Some(3), ..ExitReport::default() };
        assert!(failure_message(FailureReason::StateInUse, &busy, TIMEOUT).starts_with("Another copy of the engine"));
        assert!(failure_message(FailureReason::MissingBinary, &busy, TIMEOUT).contains(BINARY_NAME));

        let arguments =
            ExitReport { code: Some(2), reason: Some("Unknown option '--x'.".into()), ..ExitReport::default() };
        assert_eq!(
            failure_message(FailureReason::BadArguments, &arguments, TIMEOUT),
            "The engine couldn't start with the settings the app gave it: Unknown option '--x'."
        );
    }
}
