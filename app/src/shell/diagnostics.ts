// The diagnostics the start-up failure screen copies: enough to report a start that went wrong without the log.
import type { Health } from '../api/types';
import { isTauri } from '../lib/platform';
import type { Settings } from '../settings/schema';
import { ipc } from './ipc';

/** How long the diagnostics wait for the shell's own details before going without them. */
const SHELL_INFO_TIMEOUT_MS = 2_000;

export function errorText(error: unknown): string {
  if (error instanceof Error) return `${error.name}: ${error.message}`;
  return typeof error === 'string' ? error : String(error);
}

/** The report: when, which app and where its log is (when the shell can still say), the page, and the error. */
export async function diagnostics(error: unknown): Promise<string> {
  const info = isTauri
    ? await Promise.race([
        ipc.shellInfo().catch(() => null),
        new Promise<null>((resolve) => setTimeout(() => resolve(null), SHELL_INFO_TIMEOUT_MS)),
      ])
    : null;
  const logDir = info?.logDir;
  return [
    'Shouting Iguana couldn’t start',
    `Time: ${new Date().toISOString()}`,
    `App: ${info ? `${info.appVersion} (${info.platform}${info.debug ? ', debug' : ''})` : 'unknown'}`,
    `Log folder: ${logDir !== undefined && logDir !== '' ? logDir : 'unknown'}`,
    `Page: ${window.location.href}`,
    `Browser: ${navigator.userAgent}`,
    `Error: ${errorText(error)}`,
    ...(error instanceof Error && error.stack ? [error.stack] : []),
  ].join('\n');
}

/**
 * What to send with a report of a problem while the app runs (Settings > Diagnostics, and About in Simple): the app,
 * the engine, where their files are, the preferences that change what's shown, and the browser. Nothing from a crawl.
 */
export async function appDiagnostics(
  engine: Health | null,
  settings: Pick<Settings, 'mode' | 'theme' | 'zoom' | 'density'>,
): Promise<string> {
  const info = isTauri
    ? await Promise.race([
        ipc.shellInfo().catch(() => null),
        new Promise<null>((resolve) => setTimeout(() => resolve(null), SHELL_INFO_TIMEOUT_MS)),
      ])
    : null;
  return withoutAccountName(
    [
      'Shouting Iguana diagnostics',
      `Time: ${new Date().toISOString()}`,
      `App: ${info ? `${info.appVersion} (${info.platform}${info.debug ? ', debug' : ''})` : 'running in a browser'}`,
      `Engine: ${engine ? `${engine.engineVersion} (sidecar ${engine.version}, process ${engine.pid})` : 'not connected'}`,
      `Data folder: ${engine?.dataDir ?? 'unknown'}`,
      `State folder: ${engine?.stateDir ?? 'unknown'}`,
      `Log folder: ${info?.logDir ?? 'unknown'}`,
      `Mode: ${settings.mode}, theme: ${settings.theme}, density: ${settings.density}, zoom: ${Math.round(settings.zoom * 100)}%`,
      `Page: ${window.location.pathname}`,
      `Browser: ${navigator.userAgent}`,
    ].join('\n'),
  );
}

/** A home folder in a path, by its system's shape (Windows', macOS's, Linux's), and what stands for it. */
const HOME_FOLDERS: readonly { home: RegExp; stand: string }[] = [
  { home: /[A-Za-z]:\\(?:Users|Documents and Settings)\\([^\\\r\n]+)/i, stand: '%USERPROFILE%' },
  { home: /\/Users\/([^/\r\n]+)/, stand: '~' },
  { home: /\/home\/([^/\r\n]+)/, stand: '~' },
];

const escapeForPattern = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

/**
 * Diagnostics as they can be sent (review finding 17): the user's home folder stands as `%USERPROFILE%` (or `~`), and
 * their account name appears nowhere else either. A report needs where the files are, not whose they are.
 */
export function withoutAccountName(text: string): string {
  let result = text;
  for (const { home, stand } of HOME_FOLDERS) {
    const found = home.exec(result);
    const account = found?.[1];
    if (!found || !account) continue;
    // Every path into that home folder, however it's cased.
    const prefix = found[0].slice(0, found[0].length - account.length);
    result = result.replace(new RegExp(escapeForPattern(prefix + account), 'gi'), stand);
    // The name anywhere else, as a word.
    result = result.replace(new RegExp(`(?<![\\w.-])${escapeForPattern(account)}(?![\\w-])`, 'gi'), '<user>');
  }
  return result;
}
