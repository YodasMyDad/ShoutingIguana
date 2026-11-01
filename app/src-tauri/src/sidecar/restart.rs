//! What to do when the sidecar exits while the app isn't quitting: start it again, wait for the state folder, or give
//! up and say why.

use std::collections::VecDeque;
use std::time::{Duration, Instant};

use super::state::FailureReason;

/// The sidecar's exit codes (`si-sidecar --help`). 0 is a clean shutdown and 1 means it couldn't start; both are
/// unexpected while the app runs, so they restart it like any other.
pub const EXIT_BAD_ARGUMENTS: i32 = 2;
pub const EXIT_STATE_IN_USE: i32 = 3;

/// The wait before each restart within the window: a crash loop backs off instead of spinning.
const BACKOFF: [Duration; 5] = [
    Duration::from_millis(250),
    Duration::from_millis(500),
    Duration::from_secs(1),
    Duration::from_secs(2),
    Duration::from_secs(4),
];

/// More restarts than `BACKOFF` has steps within this window, and the engine is given up on.
const RESTART_WINDOW: Duration = Duration::from_secs(60);

/// A previous instance still shutting down holds the state folder for up to its graceful shutdown time (30 s), so
/// it is waited for a little longer than that.
pub const STATE_IN_USE_PATIENCE: Duration = Duration::from_secs(40);

const STATE_IN_USE_BACKOFF: [Duration; 4] =
    [Duration::from_millis(250), Duration::from_millis(500), Duration::from_secs(1), Duration::from_secs(2)];

/// How a sidecar ended: its exit code (None when a signal ended it, or it never started), and whether it had become
/// ready first.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Exit {
    pub code: Option<i32>,
    pub was_ready: bool,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Decision {
    /// Start a new sidecar after `delay`. `attempt` counts the restarts within the window, from 1.
    Restart {
        attempt: u32,
        delay: Duration,
    },
    /// Another sidecar holds the state folder: try again after `delay`.
    WaitForStateFolder {
        delay: Duration,
    },
    Fail(FailureReason),
}

#[derive(Debug, Default)]
pub struct RestartPolicy {
    restarts: VecDeque<Instant>,
    /// When the current run of "state folder in use" exits began, and how many there have been.
    state_in_use: Option<(Instant, usize)>,
}

impl RestartPolicy {
    /// Forgets every restart: the user asked to try again.
    pub fn reset(&mut self) {
        *self = Self::default();
    }

    pub fn decide(&mut self, exit: Exit, now: Instant) -> Decision {
        match exit.code {
            Some(EXIT_BAD_ARGUMENTS) => {
                self.state_in_use = None;
                Decision::Fail(FailureReason::BadArguments)
            }
            Some(EXIT_STATE_IN_USE) => {
                let (since, tries) = *self.state_in_use.get_or_insert((now, 0));
                if now.saturating_duration_since(since) >= STATE_IN_USE_PATIENCE {
                    self.state_in_use = None;
                    return Decision::Fail(FailureReason::StateInUse);
                }
                self.state_in_use = Some((since, tries + 1));
                Decision::WaitForStateFolder { delay: STATE_IN_USE_BACKOFF[tries.min(STATE_IN_USE_BACKOFF.len() - 1)] }
            }
            _ => {
                self.state_in_use = None;
                self.forget_before(now);
                if self.restarts.len() >= BACKOFF.len() {
                    return Decision::Fail(if exit.was_ready {
                        FailureReason::Crashed
                    } else {
                        FailureReason::StartFailed
                    });
                }
                self.restarts.push_back(now);
                let attempt = self.restarts.len();
                Decision::Restart { attempt: attempt as u32, delay: BACKOFF[attempt - 1] }
            }
        }
    }

    /// The restarts within the window, for the message when it gives up.
    pub fn recent_restarts(&self) -> usize {
        self.restarts.len()
    }

    fn forget_before(&mut self, now: Instant) {
        while self.restarts.front().is_some_and(|at| now.saturating_duration_since(*at) >= RESTART_WINDOW) {
            self.restarts.pop_front();
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const CRASH: Exit = Exit { code: Some(-1), was_ready: true };

    fn ms(value: u64) -> Duration {
        Duration::from_millis(value)
    }

    #[test]
    fn bad_arguments_fail_at_once() {
        let mut policy = RestartPolicy::default();
        assert_eq!(
            policy.decide(Exit { code: Some(2), was_ready: false }, Instant::now()),
            Decision::Fail(FailureReason::BadArguments)
        );
    }

    #[test]
    fn crashes_back_off_then_give_up_after_five_restarts_in_a_minute() {
        let mut policy = RestartPolicy::default();
        let start = Instant::now();
        let delays: Vec<_> = (0..5)
            .map(|second| match policy.decide(CRASH, start + Duration::from_secs(second)) {
                Decision::Restart { attempt, delay } => {
                    assert_eq!(attempt as u64, second + 1);
                    delay
                }
                other => panic!("restart {second}: {other:?}"),
            })
            .collect();
        assert_eq!(delays, [ms(250), ms(500), ms(1000), ms(2000), ms(4000)]);
        assert_eq!(policy.decide(CRASH, start + Duration::from_secs(10)), Decision::Fail(FailureReason::Crashed));
    }

    #[test]
    fn a_sidecar_that_never_got_ready_fails_to_start() {
        let mut policy = RestartPolicy::default();
        let now = Instant::now();
        let exit = Exit { code: Some(1), was_ready: false };
        for _ in 0..5 {
            assert!(matches!(policy.decide(exit, now), Decision::Restart { .. }));
        }
        assert_eq!(policy.decide(exit, now), Decision::Fail(FailureReason::StartFailed));
    }

    #[test]
    fn an_unexpected_clean_exit_or_a_signal_restarts() {
        let mut policy = RestartPolicy::default();
        let now = Instant::now();
        assert!(matches!(
            policy.decide(Exit { code: Some(0), was_ready: true }, now),
            Decision::Restart { attempt: 1, .. }
        ));
        assert!(matches!(
            policy.decide(Exit { code: None, was_ready: true }, now),
            Decision::Restart { attempt: 2, .. }
        ));
    }

    #[test]
    fn restarts_older_than_a_minute_are_forgotten() {
        let mut policy = RestartPolicy::default();
        let start = Instant::now();
        for _ in 0..4 {
            policy.decide(CRASH, start);
        }
        assert_eq!(
            policy.decide(CRASH, start + Duration::from_secs(61)),
            Decision::Restart { attempt: 1, delay: ms(250) }
        );
        assert_eq!(policy.recent_restarts(), 1);
    }

    #[test]
    fn a_busy_state_folder_is_waited_for_then_given_up_on() {
        let mut policy = RestartPolicy::default();
        let start = Instant::now();
        let busy = Exit { code: Some(3), was_ready: false };
        let delays: Vec<_> = (0..6)
            .map(|step| match policy.decide(busy, start + Duration::from_secs(step)) {
                Decision::WaitForStateFolder { delay } => delay,
                other => panic!("step {step}: {other:?}"),
            })
            .collect();
        assert_eq!(delays, [ms(250), ms(500), ms(1000), ms(2000), ms(2000), ms(2000)]);
        assert_eq!(policy.decide(busy, start + STATE_IN_USE_PATIENCE), Decision::Fail(FailureReason::StateInUse));
        // Waiting on the folder never counts as a restart.
        assert_eq!(policy.recent_restarts(), 0);
    }

    #[test]
    fn another_exit_ends_a_wait_for_the_state_folder() {
        let mut policy = RestartPolicy::default();
        let start = Instant::now();
        let busy = Exit { code: Some(3), was_ready: false };
        policy.decide(busy, start);
        policy.decide(CRASH, start + Duration::from_secs(39));
        assert_eq!(
            policy.decide(busy, start + Duration::from_secs(41)),
            Decision::WaitForStateFolder { delay: ms(250) }
        );
    }

    #[test]
    fn reset_forgets_everything() {
        let mut policy = RestartPolicy::default();
        let now = Instant::now();
        for _ in 0..5 {
            policy.decide(CRASH, now);
        }
        policy.reset();
        assert_eq!(policy.decide(CRASH, now), Decision::Restart { attempt: 1, delay: ms(250) });
    }
}
