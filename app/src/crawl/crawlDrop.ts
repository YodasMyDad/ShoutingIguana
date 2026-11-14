// A crawl file dropped on the window opens it, from any screen, in both modes (docs/v2-ui-ia.md, Home: "Ctrl+O and
// drag-and-drop open a .sicrawl from anywhere"). The shell's webview hands the page the dropped files' paths as Tauri's
// drag-and-drop events (`getCurrentWebview().onDragDropEvent`), which a page may listen to with the `core:event`
// permission it already has: nothing in the shell forwards them. A plain browser never sees a dropped file's path, so
// there the window ignores a drop. What a drag over the window shows, and what a drop opens or says, is decided here.
import type { Mode } from '../settings/schema';

/** A drag over the window, as the shell reports it: the paths come with `enter` and `drop` only. */
export type CrawlDropEvent =
  | { type: 'enter'; paths: readonly string[] }
  | { type: 'over' }
  | { type: 'drop'; paths: readonly string[] }
  | { type: 'leave' };

/** Where drops come from: the webview's events in the app, a fake in tests and stories. */
export interface CrawlDropSource {
  /** Calls `handler` for each drag event; resolves with the function that stops listening. */
  listen: (handler: (event: CrawlDropEvent) => void) => Promise<() => void>;
}

/** The extension of a crawl file, which the engine opens. */
export const CRAWL_EXTENSION = '.sicrawl';

/** A path's last part: "garden.sicrawl" from "C:\Crawls\garden.sicrawl". */
export function fileName(path: string): string {
  return path.split(/[\\/]/).filter(Boolean).pop() ?? path;
}

/** Whether a path is a crawl file by its name: the engine says what's wrong with one that's damaged. */
export function isCrawlPath(path: string): boolean {
  return path.toLowerCase().endsWith(CRAWL_EXTENSION);
}

/** The dropped paths: the crawl files to open, and the rest, which aren't, to say so. */
export function splitDropped(paths: readonly string[]): { crawls: string[]; others: string[] } {
  return { crawls: paths.filter(isCrawlPath), others: paths.filter((path) => !isCrawlPath(path)) };
}

/** What a drag over the window is: nothing, crawl files to open, or files that aren't any. */
export type DragState = { kind: 'none' } | { kind: 'crawls'; count: number } | { kind: 'others' };

/** The drag's state after an event: what the window shows while files are over it. */
export function nextDragState(event: CrawlDropEvent, current: DragState): DragState {
  switch (event.type) {
    case 'enter': {
      const { crawls } = splitDropped(event.paths);
      return crawls.length > 0 ? { kind: 'crawls', count: crawls.length } : { kind: 'others' };
    }
    case 'over':
      return current;
    default:
      return { kind: 'none' };
  }
}

/** What each mode calls a crawl file: Simple's words where Advanced's are technical. */
const FILE_WORD: Record<Mode, { one: string; many: string }> = {
  advanced: { one: 'crawl file', many: 'crawl files' },
  simple: { one: 'site check file', many: 'site check files' },
};

/** The words over the window while files are dragged over it. */
export function dragWords(state: DragState, mode: Mode): { title: string; detail: string } | null {
  const word = FILE_WORD[mode];
  switch (state.kind) {
    case 'crawls':
      return {
        title: state.count === 1 ? `Drop to open the ${word.one}` : `Drop to open the ${state.count} ${word.many}`,
        detail:
          mode === 'simple'
            ? 'It joins Your sites, and its results open.'
            : 'It joins the library, and opens where you left it.',
      };
    case 'others':
      return {
        title: `That isn’t a ${word.one}`,
        detail: `Shouting Iguana opens ${word.many}, whose names end in ${CRAWL_EXTENSION}.`,
      };
    default:
      return null;
  }
}

/** The toast for dropped files that aren't crawl files: which, and what would open. */
export function notCrawlFilesWords(others: readonly string[], mode: Mode): { title: string; description: string } {
  const word = FILE_WORD[mode];
  const names = others.map(fileName);
  const title =
    names.length === 1
      ? `${names[0]} isn’t a ${word.one}, so it wasn’t opened.`
      : `${names.length} files aren’t ${word.many}, so they weren’t opened.`;
  const listed = names.length > 1 ? ` Not opened: ${names.slice(0, 3).join(', ')}${names.length > 3 ? '…' : ''}.` : '';
  return {
    title,
    description: `Shouting Iguana opens ${word.many}, whose names end in ${CRAWL_EXTENSION}.${listed}`,
  };
}

/**
 * The webview's own drag-and-drop events, which carry the dropped files' paths: the app's source of drops. Loaded on
 * first use, so a browser never fetches the Tauri module.
 */
export const webviewDropSource: CrawlDropSource = {
  listen: async (handler) => {
    const { getCurrentWebview } = await import('@tauri-apps/api/webview');
    return getCurrentWebview().onDragDropEvent((event) => {
      const payload = event.payload;
      handler(
        payload.type === 'enter' || payload.type === 'drop'
          ? { type: payload.type, paths: payload.paths }
          : { type: payload.type },
      );
    });
  },
};

let sourceForTests: CrawlDropSource | null = null;

/** Where the window's drops come from: the webview's in the app, none in a plain browser, a test's or story's own. */
export function appDropSource(inTheApp: boolean): CrawlDropSource | null {
  return sourceForTests ?? (inTheApp ? webviewDropSource : null);
}

/** Tests and stories only: the window's drops come from this source (null goes back to the truth). */
export function setDropSourceForTests(source: CrawlDropSource | null): void {
  sourceForTests = source;
}
