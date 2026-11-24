// Settings › Updates' own words for how a new version arrives, by the copy of the app they are shown in.
import type { ShellInfo } from '../shell/ipc';

export const STAYS = 'Your crawls and settings stay as they are.';

/**
 * How a new version arrives in this copy, in a sentence that is true of it: the Windows installer, the macOS app and
 * the Linux AppImage are replaced by the app itself; a copy installed from the `.deb` package is not.
 */
export function howUpdatesArrive(shell: Pick<ShellInfo, 'platform' | 'updates'>): string {
  if (shell.updates === 'download')
    return `Shouting Iguana tells you when a new version is out. This copy was installed from the .deb package, so it can’t replace itself: you download the new .deb and install it over this one. ${STAYS}`;
  const replaces =
    shell.platform === 'windows'
      ? 'runs its installer'
      : shell.platform === 'macos'
        ? 'replaces the app where it is'
        : 'replaces the AppImage file where it is';
  return `Shouting Iguana tells you when a new version is out. When you say so, it downloads it, checks it was signed by us, ${replaces} and opens again. It never stops a crawl to do it. ${STAYS}`;
}
