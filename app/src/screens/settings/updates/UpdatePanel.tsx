import { CircleCheck, Download, ExternalLink, RefreshCw } from 'lucide-react';
import { useEffect, useId, useRef, useState } from 'react';
import { formatBytes, formatExactTime } from '../../../lib/format';
import { useLiveIds } from '../../../live/liveStore';
import { Button } from '../../../ui/Button';
import { Callout } from '../../../ui/Callout';
import { Icon } from '../../../ui/Icon';
import { ProgressBar } from '../../../ui/ProgressBar';
import {
  CHECK_WORDS,
  createUpdateStore,
  shellUpdates,
  useUpdateState,
  type UpdateState,
  type UpdateStore,
  type Updater,
} from '../../../updates/updateStore';
import { STAYS } from '../../../updates/updateWords';

export type { UpdateState, Updater };

/** How a new version arrives in this copy of the app (shell/ipc.ts, `ShellInfo.updates`). */
export type Arrival = 'inApp' | 'download';

const CLOSES = `Shouting Iguana closes while it updates, then opens again. ${STAYS}`;
const RUNNING = 'A crawl or an analysis is running, and an update never stops one.';

/** "20 November 2026", from the release's date. */
function publishedOn(iso: string | null): string | null {
  if (!iso) return null;
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return null;
  return date.toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' });
}

/**
 * Settings › Updates' updater (docs/v2-ui-architecture.md, "Updates"): Check for updates; what's new in a newer
 * version; Install and restart, on the user's word only. An update never stops a crawl or an analysis: while one runs
 * the update can be downloaded, and it installs once nothing runs. The shell downloads the installer, checks its
 * signature and runs it. A copy the app can't replace (`arrival` `download`: a Linux `.deb` install) is told of the
 * newer version and where to download it.
 */
export function UpdatePanel({
  store: given,
  updater,
  initial = { step: 'idle' },
  busy: busyOverride,
  arrival = 'inApp',
  onDownload,
}: {
  /** The app's own updates, unless stories and tests give an `updater` of their own. */
  store?: UpdateStore;
  /** For stories and tests: an updater in place of the shell's. */
  updater?: Updater;
  /** For stories: the state to start in. */
  initial?: UpdateState;
  /** For stories: whether a crawl or an analysis is running, in place of the live store's word. */
  busy?: boolean;
  arrival?: Arrival;
  /** Opens where the new version is downloaded from, for a copy that can't install it itself. */
  onDownload?: () => void;
}) {
  const [own] = useState(() => (updater ? createUpdateStore(updater, initial) : null));
  const store = own ?? given ?? shellUpdates;
  const state = useUpdateState(store);
  const liveBusy = useLiveIds().length > 0;
  const busy = busyOverride ?? liveBusy;
  const checkRef = useRef<HTMLButtonElement>(null);
  const helpId = useId();
  // Install, pressed, goes as the download starts: when it fails, focus goes to Check for updates, enabled again.
  const refocus = useRef(false);
  useEffect(() => {
    if (state.step !== 'failed' || !refocus.current) return;
    refocus.current = false;
    checkRef.current?.focus();
  }, [state.step]);

  const install = () => {
    refocus.current = true;
    void store.install();
  };

  const working = state.step === 'checking' || state.step === 'downloading' || state.step === 'installing';
  const shown =
    state.step === 'available' || state.step === 'downloading' || state.step === 'ready' || state.step === 'installing'
      ? state
      : null;
  return (
    <div className="flex w-full flex-col items-start gap-3">
      <Button
        ref={checkRef}
        size="sm"
        icon={RefreshCw}
        loading={state.step === 'checking'}
        disabled={working && state.step !== 'checking'}
        onClick={() => {
          refocus.current = false;
          void store.check();
        }}
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

      {shown ? (
        <Callout
          tone="info"
          icon={Download}
          title={
            shown.step === 'ready'
              ? `Version ${shown.update.version} is downloaded`
              : arrival === 'download'
                ? `Version ${shown.update.version} is out`
                : `Version ${shown.update.version} is available`
          }
          className="w-full"
        >
          <div className="flex flex-col gap-3">
            {publishedOn(shown.update.date) ? (
              <p className="text-13 text-text-secondary">Published {publishedOn(shown.update.date)}.</p>
            ) : null}
            {shown.update.notes ? (
              <section aria-label="What’s new" className="flex flex-col gap-1">
                <h3 className="text-13 font-semibold">What’s new</h3>
                <p className="max-h-(--update-notes-max) overflow-auto text-13 whitespace-pre-line text-text-secondary">
                  {shown.update.notes}
                </p>
              </section>
            ) : null}
            {shown.step === 'available' && arrival === 'download' ? (
              <div className="flex flex-col items-start gap-1.5">
                <Button variant="primary" size="sm" icon={ExternalLink} aria-describedby={helpId} onClick={onDownload}>
                  Open the download page
                </Button>
                <p id={helpId} className="text-12 text-text-secondary">
                  This copy was installed from the .deb package, which Shouting Iguana can’t replace itself. Download
                  the new .deb and install it over this one. {STAYS}
                </p>
              </div>
            ) : null}
            {shown.step === 'available' && arrival === 'inApp' ? (
              <div className="flex flex-col items-start gap-1.5">
                <Button variant="primary" size="sm" aria-describedby={helpId} onClick={install}>
                  {busy ? 'Download the update' : 'Install and restart'}
                </Button>
                <p id={helpId} className="text-12 text-text-secondary">
                  {busy ? `${RUNNING} Download it now, and install it once that has finished.` : CLOSES}
                </p>
              </div>
            ) : null}
            {shown.step === 'downloading' ? (
              <ProgressBar
                label={`Downloading version ${shown.update.version}`}
                value={shown.progress?.total ? shown.progress.downloaded : null}
                max={shown.progress?.total ?? 100}
                valueText={
                  shown.progress
                    ? shown.progress.total
                      ? `${formatBytes(shown.progress.downloaded)} of ${formatBytes(shown.progress.total)}`
                      : formatBytes(shown.progress.downloaded)
                    : 'Starting'
                }
                className="max-w-(--measure-text)"
              />
            ) : null}
            {shown.step === 'ready' ? (
              <div className="flex flex-col items-start gap-1.5">
                <Button variant="primary" size="sm" disabled={busy} aria-describedby={helpId} onClick={install}>
                  Restart and install
                </Button>
                <p id={helpId} className="text-12 text-text-secondary">
                  {busy
                    ? `Its signature is good. ${RUNNING} Install it once that has finished.`
                    : shown.held
                      ? 'Something was still running, a file being saved perhaps. Install it once that has finished.'
                      : CLOSES}
                </p>
              </div>
            ) : null}
            {shown.step === 'installing' ? (
              <p className="text-13 text-text-secondary">
                Installing. Shouting Iguana closes and opens again in a moment.
              </p>
            ) : null}
          </div>
        </Callout>
      ) : null}

      {state.step === 'failed' ? (
        <Callout
          tone={!state.update && state.failure ? CHECK_WORDS[state.failure.problem].tone : 'error'}
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
