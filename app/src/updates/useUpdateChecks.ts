// The app's own check for a newer version: a little after it starts, then a few times a day while it stays open
// (people leave it running in the tray). It is quiet: no newer version, no connection or no answer says nothing, and
// Settings › Updates shows what the check found. A newer version is said once, in a toast that leads to Settings ›
// Updates, where what's new and Install and restart are. Nothing is downloaded or installed until the user asks.
// Settings › Updates turns the check off (`checkForUpdates`); Check for updates there still works.
import { useNavigate } from '@tanstack/react-router';
import { useEffect, useEffectEvent } from 'react';
import { notifyError, notifyResult } from '../feedback/notify';
import { useSettings } from '../settings/settingsStore';
import type { ShellInfo } from '../shell/ipc';
import { useShellInfo } from '../shell/useShellInfo';
import { shellUpdates, type UpdateStore } from './updateStore';

/** After the app has started: the window is up and the engine has had its turn at the network. */
export const FIRST_CHECK_MS = 15_000;
/** While the app stays open. */
export const CHECK_EVERY_MS = 6 * 60 * 60 * 1_000;
/** Longer than a result's toast: news of the app itself, not of something the user just did. */
const NOTICE_MS = 20_000;

const AVAILABLE_TOAST = 'update:available';
const FAILED_TOAST = 'update:failed';

/** The versions already said in this run of the app: each is said once. */
const told = new Set<string>();

/** For tests: forget what has been said. */
export function forgetUpdateNotices(): void {
  told.clear();
}

/** The words of the notice: a copy the app can't replace is told where the new version is, not that it installs. */
export function updateNotice(version: string, arrival: ShellInfo['updates']): { title: string; description: string } {
  return arrival === 'download'
    ? {
        title: `Version ${version} is out`,
        description: 'Settings › Updates says what’s new and where to download it.',
      }
    : {
        title: `Version ${version} is available`,
        description: 'Settings › Updates says what’s new and installs it when you’re ready.',
      };
}

/**
 * Checks for a newer version by itself, and says so when there is one. `shell` is the shell's word on this copy: null
 * (a plain browser, or the shell hasn't answered yet) checks nothing.
 */
export function useUpdateChecks(store: UpdateStore = shellUpdates, shellOverride?: ShellInfo | null): void {
  const own = useShellInfo();
  const shell = shellOverride === undefined ? own : shellOverride;
  const automatic = useSettings((settings) => settings.checkForUpdates);
  const navigate = useNavigate();
  const allowed = shell?.updateChecks === true;
  const arrival = shell?.updates ?? 'inApp';
  const failed = shell?.updateFailed ?? null;

  const check = useEffectEvent(async () => {
    await store.check({ quiet: true });
    const state = store.getState();
    if (state.step !== 'available' && state.step !== 'ready') return;
    const { version } = state.update;
    if (told.has(version)) return;
    told.add(version);
    notifyResult(updateNotice(version, arrival).title, {
      id: AVAILABLE_TOAST,
      description: updateNotice(version, arrival).description,
      duration: NOTICE_MS,
      action: {
        label: 'See what’s new',
        onClick: () => void navigate({ to: '/settings/{-$section}', params: { section: 'updates' } }),
      },
    });
  });

  useEffect(() => {
    if (!allowed || !automatic) return undefined;
    const first = setTimeout(() => void check(), FIRST_CHECK_MS);
    const later = setInterval(() => void check(), CHECK_EVERY_MS);
    return () => {
      clearTimeout(first);
      clearInterval(later);
    };
  }, [allowed, automatic]);

  // The run before this one tried to install a version and couldn't: the app opened again as it was, and says so.
  useEffect(() => {
    if (!failed || told.has(`failed:${failed}`)) return;
    told.add(`failed:${failed}`);
    notifyError(new Error(`Version ${failed} couldn’t be installed`), {
      id: FAILED_TOAST,
      title: `Version ${failed} couldn’t be installed`,
      description: 'Shouting Iguana opened again as it was. Try again from Settings › Updates.',
    });
  }, [failed]);
}
