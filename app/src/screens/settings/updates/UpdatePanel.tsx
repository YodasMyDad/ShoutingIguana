import { CircleCheck, Download, RefreshCw } from 'lucide-react';
import { useEffect, useReducer, useRef } from 'react';
import { announce } from '../../../feedback/announce';
import { formatBytes, formatExactTime } from '../../../lib/format';
import { useLiveIds } from '../../../live/liveStore';
import {
  parseUpdateCheckFailure,
  type UpdateCheck,
  type UpdateCheckFailure,
  type UpdateCheckProblem,
  type UpdateProgress,
} from '../../../shell/ipc';
import { Button } from '../../../ui/Button';
import { Callout } from '../../../ui/Callout';
import { Icon } from '../../../ui/Icon';
import { ProgressBar } from '../../../ui/ProgressBar';

/** What the panel needs of the shell (shell/ipc.ts): stories and tests give their own. */
export interface Updater {
  check: () => Promise<UpdateCheck>;
  install: (version: string) => Promise<void>;
  onProgress: (handler: (progress: UpdateProgress) => void) => Promise<() => void>;
}

type Available = NonNullable<UpdateCheck['available']>;

export type UpdateState =
  | { step: 'idle' }
  | { step: 'checking' }
  | { step: 'upToDate'; version: string; checkedAt: string }
  | { step: 'available'; update: Available }
  | { step: 'downloading'; update: Available; progress: UpdateProgress | null }
  | { step: 'installing'; update: Available }
  | { step: 'failed'; message: string; update: Available | null; failure?: UpdateCheckFailure | undefined };

type Action =
  | { type: 'check' }
  | { type: 'checked'; result: UpdateCheck; at: string }
  | { type: 'install' }
  | { type: 'progress'; progress: UpdateProgress }
  | { type: 'installing' }
  | { type: 'failed'; message: string; failure?: UpdateCheckFailure | undefined };

function reduce(state: UpdateState, action: Action): UpdateState {
  const update = 'update' in state ? state.update : null;
  switch (action.type) {
    case 'check':
      return { step: 'checking' };
    case 'checked':
      return action.result.available
        ? { step: 'available', update: action.result.available }
        : { step: 'upToDate', version: action.result.currentVersion, checkedAt: action.at };
    case 'install':
      return update ? { step: 'downloading', update, progress: null } : state;
    case 'progress':
      return state.step === 'downloading' ? { ...state, progress: action.progress } : state;
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
const CHECK_WORDS: Record<
  UpdateCheckProblem,
  { tone: 'info' | 'error'; title: string; text: (version: string | null) => string }
> = {
  noConnection: {
    tone: 'error',
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

/** "20 November 2026", from the release's date. */
function publishedOn(iso: string | null): string | null {
  if (!iso) return null;
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return null;
  return date.toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' });
}

/**
 * Settings › Updates' updater (docs/v2-ui-architecture.md, "Updates"): Check for updates; what's new in a newer
 * version; Install and restart, on the user's word only, never while a crawl or an analysis runs (installing closes the
 * app). The shell downloads the installer, checks its signature and runs it.
 */
export function UpdatePanel({
  updater,
  initial = { step: 'idle' },
  busy: busyOverride,
}: {
  updater: Updater;
  /** For stories: the state to start in. */
  initial?: UpdateState;
  /** For stories: whether a crawl or an analysis is running, in place of the live store's word. */
  busy?: boolean;
}) {
  const [state, dispatch] = useReducer(reduce, initial);
  const liveBusy = useLiveIds().length > 0;
  const busy = busyOverride ?? liveBusy;
  const checkRef = useRef<HTMLButtonElement>(null);
  // Install, pressed, goes as the download starts: when it fails, focus goes to Check for updates, enabled again.
  const refocus = useRef(false);
  useEffect(() => {
    if (state.step !== 'failed' || !refocus.current) return;
    refocus.current = false;
    checkRef.current?.focus();
  }, [state.step]);

  useEffect(() => {
    if (state.step !== 'downloading') return undefined;
    let unlisten: (() => void) | null = null;
    let gone = false;
    void updater
      .onProgress((progress) => dispatch({ type: 'progress', progress }))
      .then((stop) => {
        if (gone) stop();
        else unlisten = stop;
      });
    return () => {
      gone = true;
      unlisten?.();
    };
  }, [state.step, updater]);

  const check = async () => {
    dispatch({ type: 'check' });
    try {
      const result = await updater.check();
      dispatch({ type: 'checked', result, at: new Date().toISOString() });
      announce(
        result.available
          ? `Version ${result.available.version} is available.`
          : `You have the newest version, ${result.currentVersion}.`,
      );
    } catch (error) {
      const failure = parseUpdateCheckFailure(error) ?? undefined;
      const words = failure ? CHECK_WORDS[failure.problem] : null;
      const message = words && failure ? words.text(failure.currentVersion) : messageOf(error);
      dispatch({ type: 'failed', message, failure });
      announce(words ? `${words.title}. ${message}` : message);
    }
  };

  const install = async (update: Available) => {
    dispatch({ type: 'install' });
    announce(`Downloading version ${update.version}.`);
    try {
      await updater.install(update.version);
      // The shell closes the app as the installer starts: this is seen only for a moment, if at all.
      dispatch({ type: 'installing' });
    } catch (error) {
      const message = messageOf(error);
      dispatch({ type: 'failed', message });
      refocus.current = true;
      announce(message);
    }
  };

  const working = state.step === 'checking' || state.step === 'downloading' || state.step === 'installing';
  return (
    <div className="flex w-full flex-col items-start gap-3">
      <Button
        ref={checkRef}
        size="sm"
        icon={RefreshCw}
        loading={state.step === 'checking'}
        disabled={working && state.step !== 'checking'}
        onClick={() => void check()}
      >
        {state.step === 'checking' ? 'Checking for updates…' : 'Check for updates'}
      </Button>

      {state.step === 'upToDate' ? (
        <p className="flex items-center gap-2 text-13 text-text-secondary">
          <Icon icon={CircleCheck} size="sm" />
          <span>
            You have the newest version, <span className="font-mono text-12">{state.version}</span>. Checked{' '}
            {formatExactTime(state.checkedAt)}.
          </span>
        </p>
      ) : null}

      {state.step === 'available' || state.step === 'downloading' || state.step === 'installing' ? (
        <Callout tone="info" icon={Download} title={`Version ${state.update.version} is available`} className="w-full">
          <div className="flex flex-col gap-3">
            {publishedOn(state.update.date) ? (
              <p className="text-13 text-text-secondary">Published {publishedOn(state.update.date)}.</p>
            ) : null}
            <section aria-label="What’s new" className="flex flex-col gap-1">
              <h3 className="text-13 font-semibold">What’s new</h3>
              <p className="max-h-(--update-notes-max) overflow-auto text-13 whitespace-pre-line text-text-secondary">
                {state.update.notes ?? 'The release doesn’t say.'}
              </p>
            </section>
            {state.step === 'available' ? (
              <div className="flex flex-col items-start gap-1.5">
                <Button
                  variant="primary"
                  size="sm"
                  disabled={busy}
                  aria-describedby="update-install-help"
                  onClick={() => void install(state.update)}
                >
                  Install and restart
                </Button>
                <p id="update-install-help" className="text-12 text-text-secondary">
                  {busy
                    ? 'A crawl or an analysis is running. Install once it has finished.'
                    : 'Shouting Iguana closes while the installer runs, then opens again. Your crawls and settings stay as they are.'}
                </p>
              </div>
            ) : null}
            {state.step === 'downloading' ? (
              <ProgressBar
                label={`Downloading version ${state.update.version}`}
                value={state.progress?.total ? state.progress.downloaded : null}
                max={state.progress?.total ?? 100}
                valueText={
                  state.progress
                    ? state.progress.total
                      ? `${formatBytes(state.progress.downloaded)} of ${formatBytes(state.progress.total)}`
                      : formatBytes(state.progress.downloaded)
                    : 'Starting'
                }
                className="max-w-(--measure-text)"
              />
            ) : null}
            {state.step === 'installing' ? (
              <p className="text-13 text-text-secondary">
                Installing. Shouting Iguana closes and opens again in a moment.
              </p>
            ) : null}
          </div>
        </Callout>
      ) : null}

      {state.step === 'failed' ? (
        <Callout
          tone={state.failure ? CHECK_WORDS[state.failure.problem].tone : 'error'}
          title={
            state.update
              ? 'The update wasn’t installed'
              : state.failure
                ? CHECK_WORDS[state.failure.problem].title
                : 'Updates couldn’t be checked'
          }
          className="w-full"
        >
          {state.failure && !state.update
            ? CHECK_WORDS[state.failure.problem].text(state.failure.currentVersion)
            : state.message}
        </Callout>
      ) : null}
    </div>
  );
}
