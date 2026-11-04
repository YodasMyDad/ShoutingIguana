// The diagnostics the start-up failure screen copies: enough to report a start that went wrong without the log.
import { isTauri } from '../lib/platform';
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
