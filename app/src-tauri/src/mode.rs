//! The mode the page is in, Simple or Advanced, so the shell's own words (the tray, the hide-to-tray notice, the
//! quit question) are the page's (docs/v2-ui-simple.md, "Simple's words"). The page tells the shell by storing its
//! preferences (`settings_write`); the shell reads them once at start and keeps the mode here.

use std::sync::atomic::{AtomicBool, Ordering};

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum Mode {
    Simple,
    #[default]
    Advanced,
}

impl Mode {
    pub fn of(simple: bool) -> Self {
        if simple {
            Self::Simple
        } else {
            Self::Advanced
        }
    }
}

/// The mode as the shell last heard it. Cheap to read from any thread.
#[derive(Debug)]
pub struct ShellMode(AtomicBool);

impl ShellMode {
    pub fn new(mode: Mode) -> Self {
        Self(AtomicBool::new(mode == Mode::Simple))
    }

    pub fn get(&self) -> Mode {
        Mode::of(self.0.load(Ordering::SeqCst))
    }

    /// Sets the mode; true if it changed.
    pub fn set(&self, mode: Mode) -> bool {
        self.0.swap(mode == Mode::Simple, Ordering::SeqCst) != (mode == Mode::Simple)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_change_is_said_once() {
        let mode = ShellMode::new(Mode::Simple);
        assert_eq!(mode.get(), Mode::Simple);
        assert!(!mode.set(Mode::Simple));
        assert!(mode.set(Mode::Advanced));
        assert_eq!(mode.get(), Mode::Advanced);
        assert!(!mode.set(Mode::Advanced));
    }
}
