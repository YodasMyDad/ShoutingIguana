use std::sync::{Mutex, MutexGuard, PoisonError};

/// Locks a mutex, carrying on if a thread panicked while holding it: every value guarded here stays consistent
/// between statements, and a shell that stops managing its sidecar because of an unrelated panic would be worse.
pub fn lock<T>(mutex: &Mutex<T>) -> MutexGuard<'_, T> {
    mutex.lock().unwrap_or_else(PoisonError::into_inner)
}
