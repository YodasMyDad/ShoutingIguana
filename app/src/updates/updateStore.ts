// The app's own update, as the whole app knows it: checked for when the app starts and now and then after
// (useUpdateChecks), and shown, with what's new and Install and restart, in Settings › Updates (UpdatePanel). One
// store, so a check made in the background is what Settings shows, and a download goes on while the user is elsewhere.
// The shell does the work (src-tauri/src/updates.rs): it reads the release feed, downloads the installer, checks its
// signature and runs it.
import { useSyncExternalStore } from 'react';
import { announce } from '../feedback/announce';
import {
  ipc,
  parseUpdateCheckFailure,
  type UpdateCheck,
  type UpdateCheckFailure,
  type UpdateCheckProblem,
  type UpdateInstallOutcome,
  type UpdateProgress,
} from '../shell/ipc';

/** What the store needs of the shell (shell/ipc.ts): stories and tests give their own. */
export interface Updater {
  check: () => Promise<UpdateCheck>;
  /** Installed, the app closes and this never answers. `waiting`: downloaded, and held while something runs. */
  install: (version: string) => Promise<UpdateInstallOutcome>;
  onProgress: (handler: (progress: UpdateProgress) => void) => Promise<() => void>;
}

export type Available = NonNullable<UpdateCheck['available']>;

export type UpdateState =
  | { step: 'idle' }
  | { step: 'checking' }
  | { step: 'upToDate'; version: string; checkedAt: string }
  | { step: 'available'; update: Available }
  | { step: 'downloading'; update: Available; progress: UpdateProgress | null }
  /** Downloaded, its signature good. `held`: Install was asked for, and something running kept it waiting. */
  | { step: 'ready'; update: Available; held: boolean }
  | { step: 'installing'; update: Available }
  | { step: 'failed'; message: string; update: Available | null; failure?: UpdateCheckFailure | undefined };

type Action =
  | { type: 'check' }
  | { type: 'checked'; result: UpdateCheck; at: string }
  | { type: 'install' }
  | { type: 'progress'; progress: UpdateProgress }
  | { type: 'waiting' }
  | { type: 'installing' }
  | { type: 'failed'; message: string; failure?: UpdateCheckFailure | undefined };

export function reduce(state: UpdateState, action: Action): UpdateState {
  const update = 'update' in state ? state.update : null;
  switch (action.type) {
    case 'check':
      return { step: 'checking' };
    case 'checked': {
      const found = action.result.available;
      if (!found) return { step: 'upToDate', version: action.result.currentVersion, checkedAt: action.at };
      return found.downloaded ? { step: 'ready', update: found, held: false } : { step: 'available', update: found };
    }
    case 'install':
      if (!update) return state;
      // Already downloaded: there's nothing to watch arrive.
      return state.step === 'ready' ? { step: 'installing', update } : { step: 'downloading', update, progress: null };
    case 'progress':
      return state.step === 'downloading' ? { ...state, progress: action.progress } : state;
    case 'waiting':
      return update ? { step: 'ready', update: { ...update, downloaded: true }, held: true } : state;
    case 'installing':
      return update ? { step: 'installing', update } : state;
    case 'failed':
      return { step: 'failed', message: action.message, update, failure: action.failure };
  }
}

const messageOf = (error: unknown) =>
  typeof error === 'string'
    ? error
    : error instanceof Error
      ? error.message
      : (parseUpdateCheckFailure(error)?.message ?? 'Something went wrong.');

/**
 * A check that showed no update, case by case, in plain words (round 5's polish: GitHub answering that there's
 * no update information yet read as "check the internet connection"). No update information yet isn't a fault: it's
 * said as information, with the version running.
 */
export const CHECK_WORDS: Record<
  UpdateCheckProblem,
  { tone: 'info' | 'error'; title: string; text: (version: string | null) => string }
> = {
  noConnection: {
    tone: 'info',
    title: 'Couldn’t reach GitHub',
    text: () => 'New versions come from GitHub, and it didn’t answer. Check the internet connection, then try again.',
  },
  noUpdateInformation: {
    tone: 'info',
    title: 'No update information yet',
    text: (version) =>
      `GitHub answered, but no new version has been published there yet${version ? `: you have ${version}` : ''}. Try again later.`,
  },
  unreadable: {
    tone: 'error',
    title: 'The update information couldn’t be read',
    text: () => 'GitHub answered with update information this version can’t read. Try again later.',
  },
  untrusted: {
    tone: 'error',
    title: 'The update information can’t be trusted',
    text: () => 'Its signature doesn’t match this app’s, so nothing will be installed from it. Try again later.',
  },
  unavailable: {
    tone: 'error',
    title: 'Updates can’t be checked here',
    text: () => 'This copy of the app can’t check for new versions.',
  },
};

export interface UpdateStore {
  getState: () => UpdateState;
  subscribe: (listener: () => void) => () => void;
  /**
   * Asks whether there's a newer version. `quiet` is the app's own check, in the background: it says nothing aloud,
   * and it leaves a download, an install or another check alone.
   */
  check: (options?: { quiet?: boolean }) => Promise<void>;
  /** Downloads the version shown, and installs it unless something is running. Resolves true when it failed. */
  install: () => Promise<boolean>;
}

export function createUpdateStore(updater: Updater, initial: UpdateState = { step: 'idle' }): UpdateStore {
  let state = initial;
  const listeners = new Set<() => void>();
  const dispatch = (action: Action) => {
    const next = reduce(state, action);
    if (next === state) return;
    state = next;
    for (const listener of listeners) listener();
  };
  const working = () => state.step === 'checking' || state.step === 'downloading' || state.step === 'installing';

  return {
    getState: () => state,
    subscribe: (listener) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    check: async ({ quiet = false } = {}) => {
      if (working() || (quiet && state.step === 'ready')) return;
      // The app's own check takes nothing off the page: no "Checking…" in place of what the last check found, and
      // nothing at all once the user has started a check, a download or an install of their own meanwhile.
      if (!quiet) dispatch({ type: 'check' });
      try {
        const result = await updater.check();
        if (quiet && (working() || state.step === 'ready')) return;
        dispatch({ type: 'checked', result, at: new Date().toISOString() });
        if (quiet) return;
        announce(
          result.available
            ? `Version ${result.available.version} is available.`
            : `You have the newest version, ${result.currentVersion}.`,
        );
      } catch (error) {
        // Going offline doesn't take back a version already found, or the newest version already confirmed.
        if (quiet && state.step !== 'idle') return;
        const failure = parseUpdateCheckFailure(error) ?? undefined;
        const words = failure ? CHECK_WORDS[failure.problem] : null;
        const message = words && failure ? words.text(failure.currentVersion) : messageOf(error);
        dispatch({ type: 'failed', message, failure });
        if (!quiet) announce(words ? `${words.title}. ${message}` : message);
      }
    },
    install: async () => {
      if ((state.step !== 'available' && state.step !== 'ready') || working()) return false;
      const { update } = state;
      const downloaded = state.step === 'ready';
      dispatch({ type: 'install' });
      announce(downloaded ? `Installing version ${update.version}.` : `Downloading version ${update.version}.`);
      let stop: (() => void) | null = null;
      try {
        stop = await updater.onProgress((progress) => dispatch({ type: 'progress', progress }));
        const outcome = await updater.install(update.version);
        if (outcome === 'waiting') {
          dispatch({ type: 'waiting' });
          announce(`Version ${update.version} is downloaded. It installs once what is running has finished.`);
        } else {
          // The shell closes the app as the installer starts: this is seen only for a moment, if at all.
          dispatch({ type: 'installing' });
        }
        return false;
      } catch (error) {
        const message = messageOf(error);
        dispatch({ type: 'failed', message });
        announce(message);
        return true;
      } finally {
        stop?.();
      }
    },
  };
}

/** The installed app's updates: the shell's updater. */
export const shellUpdates = createUpdateStore({
  check: ipc.updateCheck,
  install: ipc.updateInstall,
  onProgress: ipc.onUpdateProgress,
});

export function useUpdateState(store: UpdateStore = shellUpdates): UpdateState {
  return useSyncExternalStore(store.subscribe, store.getState);
}
