//! What to do when the sidecar exits while the app isn't quitting: start it again, or give up and say why. Every
//! path ends: a start that never gets going is tried three times, and a crash loop five times, whatever the timing.

use std::time::Duration;

use super::state::FailureReason;

/// The sidecar's exit codes (`si-sidecar --help`). 0 is a clean shutdown and 1 means it couldn't start; both are
/// unexpected while the app runs, so they restart it like any other.
pub const EXIT_BAD_ARGUMENTS: i32 = 2;
pub const EXIT_STATE_IN_USE: i32 = 3;

/// The waits before the restarts after a start that never got ready: two more tries, so three starts in all.
const START_BACKOFF: [Duration; 2] = [Duration::from_millis(250), Duration::from_secs(1)];

/// Starts in a row that may fail before the engine is given up on.
pub const START_ATTEMPTS: u32 = START_BACKOFF.len() as u32 + 1;

/// The waits before the restarts after crashes (it had been ready): a crash loop backs off instead of spinning.
const CRASH_BACKOFF: [Duration; 5] = [
    Duration::from_millis(250),
    Duration::from_millis(500),
    Duration::from_secs(1),
    Duration::from_secs(2),
    Duration::from_secs(4),
];

/// A sidecar that served this long before it stopped had settled: the crashes before it are forgotten.
pub const STABLE_UPTIME: Duration = Duration::from_secs(5 * 60);

/// How a sidecar ended: its exit code (None when a signal ended it, or it never started), and how long it had been
/// ready for (None when it never was).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Exit {
    pub code: Option<i32>,
    pub ready_for: Option<Duration>,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Decision {
    /// Start a new sidecar after `delay`: restart `attempt` of at most `attempts` for this kind of failure, from 1.
    Restart {
        attempt: u32,
        attempts: u32,
        delay: Duration,
    },
    Fail(FailureReason),
}

#[derive(Debug, Default)]
pub struct RestartPolicy {
    /// Crashes since the engine last ran for `STABLE_UPTIME`.
    crashes: u32,
    /// Starts in a row that never got ready.
    failed_starts: u32,
}

impl RestartPolicy {
    /// Forgets every failure: the user asked to try again.
    pub fn reset(&mut self) {
        *self = Self::default();
    }

    pub fn decide(&mut self, exit: Exit) -> Decision {
        match (exit.code, exit.ready_for) {
            // The shell's own mistake: starting again can't help.
            (Some(EXIT_BAD_ARGUMENTS), _) => Decision::Fail(FailureReason::BadArguments),
            // Another copy of the app or the engine holds the state folder. A previous instance of this app can't:
            // single instance and the job object end it with its shell, so waiting would only keep the user waiting.
            (Some(EXIT_STATE_IN_USE), _) => Decision::Fail(FailureReason::StateInUse),
            (_, None) => {
                self.failed_starts += 1;
                if self.failed_starts >= START_ATTEMPTS {
                    return Decision::Fail(FailureReason::StartFailed);
                }
                Decision::Restart {
                    attempt: self.failed_starts,
                    attempts: START_ATTEMPTS - 1,
                    delay: START_BACKOFF[self.failed_starts as usize - 1],
                }
            }
            (_, Some(uptime)) => {
                self.failed_starts = 0;
                if uptime >= STABLE_UPTIME {
                    self.crashes = 0;
                }
                self.crashes += 1;
                if self.crashes as usize > CRASH_BACKOFF.len() {
                    return Decision::Fail(FailureReason::Crashed);
                }
                Decision::Restart {
                    attempt: self.crashes,
                    attempts: CRASH_BACKOFF.len() as u32,
                    delay: CRASH_BACKOFF[self.crashes as usize - 1],
                }
            }
        }
    }

    /// The crashes counted so far, for the message when it gives up.
    pub fn crashes(&self) -> u32 {
        self.crashes
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn ms(value: u64) -> Duration {
        Duration::from_millis(value)
    }

    fn crash_after(uptime: Duration) -> Exit {
        Exit { code: Some(-1), ready_for: Some(uptime) }
    }

    const NEVER_READY: Exit = Exit { code: None, ready_for: None };

    #[test]
    fn bad_arguments_fail_at_once() {
        let mut policy = RestartPolicy::default();
        assert_eq!(policy.decide(Exit { code: Some(2), ready_for: None }), Decision::Fail(FailureReason::BadArguments));
    }

    #[test]
    fn a_busy_state_folder_fails_at_once_without_a_restart() {
        let mut policy = RestartPolicy::default();
        assert_eq!(policy.decide(Exit { code: Some(3), ready_for: None }), Decision::Fail(FailureReason::StateInUse));
        assert_eq!(
            policy.decide(Exit { code: Some(3), ready_for: Some(ms(10)) }),
            Decision::Fail(FailureReason::StateInUse)
        );
    }

    /// The review's case (M1): a start that hangs until the start timeout used to restart forever. However long each
    /// attempt takes, the third start that never gets ready gives up.
    #[test]
    fn three_starts_in_a_row_that_never_get_ready_fail_whatever_their_timing() {
        let mut policy = RestartPolicy::default();
        assert_eq!(policy.decide(NEVER_READY), Decision::Restart { attempt: 1, attempts: 2, delay: ms(250) });
        assert_eq!(
            policy.decide(Exit { code: Some(1), ready_for: None }),
            Decision::Restart { attempt: 2, attempts: 2, delay: ms(1000) }
        );
        assert_eq!(policy.decide(NEVER_READY), Decision::Fail(FailureReason::StartFailed));
    }

    #[test]
    fn a_start_that_gets_ready_ends_the_run_of_failed_starts() {
        let mut policy = RestartPolicy::default();
        policy.decide(NEVER_READY);
        policy.decide(NEVER_READY);
        assert!(matches!(policy.decide(crash_after(ms(100))), Decision::Restart { attempt: 1, .. }));
        assert_eq!(policy.decide(NEVER_READY), Decision::Restart { attempt: 1, attempts: 2, delay: ms(250) });
    }

    #[test]
    fn crashes_back_off_then_give_up_after_five_restarts() {
        let mut policy = RestartPolicy::default();
        let delays: Vec<_> = (1..=5)
            .map(|crash| match policy.decide(crash_after(Duration::from_secs(1))) {
                Decision::Restart { attempt, attempts, delay } => {
                    assert_eq!((attempt, attempts), (crash, 5));
                    delay
                }
                other => panic!("crash {crash}: {other:?}"),
            })
            .collect();
        assert_eq!(delays, [ms(250), ms(500), ms(1000), ms(2000), ms(4000)]);
        assert_eq!(policy.decide(crash_after(Duration::from_secs(1))), Decision::Fail(FailureReason::Crashed));
        assert_eq!(policy.crashes(), 6);
    }

    /// The review's other case: a sidecar that crashes a little while after each start used to slip out of the old
    /// one-minute window. Only a run of five stable minutes forgets the crashes before it.
    #[test]
    fn crashes_spread_out_still_add_up_until_a_stable_run() {
        let mut policy = RestartPolicy::default();
        for _ in 0..5 {
            assert!(matches!(policy.decide(crash_after(Duration::from_secs(90))), Decision::Restart { .. }));
        }
        assert_eq!(policy.decide(crash_after(STABLE_UPTIME - ms(1))), Decision::Fail(FailureReason::Crashed));

        let mut settled = RestartPolicy::default();
        for _ in 0..5 {
            settled.decide(crash_after(Duration::from_secs(90)));
        }
        assert_eq!(
            settled.decide(crash_after(STABLE_UPTIME)),
            Decision::Restart { attempt: 1, attempts: 5, delay: ms(250) }
        );
    }

    #[test]
    fn an_unexpected_clean_exit_or_a_signal_counts_as_a_crash() {
        let mut policy = RestartPolicy::default();
        assert!(matches!(
            policy.decide(Exit { code: Some(0), ready_for: Some(ms(5)) }),
            Decision::Restart { attempt: 1, .. }
        ));
        assert!(matches!(
            policy.decide(Exit { code: None, ready_for: Some(ms(5)) }),
            Decision::Restart { attempt: 2, .. }
        ));
    }

    #[test]
    fn reset_forgets_everything() {
        let mut policy = RestartPolicy::default();
        for _ in 0..5 {
            policy.decide(crash_after(ms(1)));
        }
        policy.decide(NEVER_READY);
        policy.decide(NEVER_READY);
        policy.reset();
        assert_eq!(policy.decide(crash_after(ms(1))), Decision::Restart { attempt: 1, attempts: 5, delay: ms(250) });
        assert_eq!(policy.decide(NEVER_READY), Decision::Restart { attempt: 1, attempts: 2, delay: ms(250) });
    }
}
