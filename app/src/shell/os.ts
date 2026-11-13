// What the app asks of the operating system, in one place: links, folders, file dialogs, zoom, the window's theme
// and quitting. Each works in the shell; a plain browser gets the nearest equivalent, or `false` when there's none.
import { getCurrentWebview } from '@tauri-apps/api/webview';
import { getCurrentWindow } from '@tauri-apps/api/window';
import { open as openDialog, save as saveDialog } from '@tauri-apps/plugin-dialog';
import { openUrl as openerOpenUrl } from '@tauri-apps/plugin-opener';
import { isTauri } from '../lib/platform';
import { ipc } from './ipc';

/** Opens an http(s) URL in the default browser. Anything else is refused. */
export async function openInBrowser(url: string): Promise<boolean> {
  if (!/^https?:\/\//i.test(url)) return false;
  if (isTauri) {
    await openerOpenUrl(url);
    return true;
  }
  return window.open(url, '_blank', 'noopener,noreferrer') !== null;
}

/**
 * Shows a library crawl's file in Explorer or Finder. It takes the crawl's id, not a path: the shell finds the file
 * from the engine's library, so the page can't reveal anything else. Not possible from a plain browser.
 */
export async function showInFolder(crawlId: string): Promise<boolean> {
  if (!isTauri) return false;
  await ipc.revealCrawlFile(crawlId);
  return true;
}

/**
 * Shows the file a finished export wrote (a report, a spreadsheet) in Explorer or Finder. It takes the export's id,
 * not a path: the shell asks the engine which file it wrote. Not possible from a plain browser.
 */
export async function showExportInFolder(exportId: string): Promise<boolean> {
  if (!isTauri) return false;
  await ipc.revealExportFile(exportId);
  return true;
}

/** The native open dialog for crawl files. Null when cancelled, or outside the shell. */
export async function pickCrawlFiles(): Promise<string[] | null> {
  if (!isTauri) return null;
  const picked = await openDialog({
    title: 'Open a crawl',
    multiple: true,
    directory: false,
    filters: [{ name: 'Shouting Iguana crawl', extensions: ['sicrawl'] }],
  });
  if (picked === null) return null;
  return Array.isArray(picked) ? picked : [picked];
}

/** The native save dialog, for exports. Null when cancelled, or outside the shell. */
export async function pickSavePath(options: {
  title: string;
  defaultPath: string;
  filters: { name: string; extensions: string[] }[];
}): Promise<string | null> {
  if (!isTauri) return null;
  return saveDialog(options);
}

export const ZOOM_LEVELS = [0.5, 0.67, 0.75, 0.8, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2] as const;

/** Zooms the whole app. WebView2's own zoom keys are off, so the keyboard registry owns them. */
export async function setZoom(level: number): Promise<void> {
  if (isTauri) await getCurrentWebview().setZoom(level);
  else document.documentElement.style.setProperty('zoom', String(level));
}

/** Keeps the native title bar in step with the app's theme. Null follows the OS. */
export async function setWindowTheme(theme: 'light' | 'dark' | null): Promise<void> {
  if (isTauri) await getCurrentWindow().setTheme(theme);
}

/** Quits through the shell, which asks first if a crawl is running. */
export async function quitApp(): Promise<boolean> {
  if (!isTauri) return false;
  await ipc.requestQuit();
  return true;
}
