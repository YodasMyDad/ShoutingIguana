// What the app adds to the report in its frame, and what a click in it does (the UX audit's S-11 and A-12). The report
// is the engine's HTML, the same document a user saves; in the app's preview:
//
//  - each problem's heading gains a link into that problem in the app ("Open in What to fix", "Open in Issues"),
//    where the engine left a comment for it. The saved file and paper have no such control: it would lead nowhere;
//  - a click on a link is the app's to carry out, as the frame is sandboxed and opens nothing itself: an address
//    outside opens in the browser, a part of the report scrolls into view, a problem's link opens the problem;
//  - the sheet is zoomed to fit the pane, on screen only: paper is the report's own size.
//
// Like the scroll bars (reportScrollBars.ts), all of it is added to the frame's copy only.

export const APP_PARTS_STYLE_ID = 'si-app-report';

/** What the engine's comment opens with, before a check's id: `<!--si:open title-too-long-->` (ReportDocument.OPEN_MARK). */
const OPEN_MARK = /<!--si:open ([a-z0-9]+(?:-[a-z0-9]+)*)-->/g;

/** The custom property the frame's zoom is given by, set on its root element by the page (fitZoom). */
export const ZOOM_PROPERTY = '--si-zoom';

/**
 * The app's style for the frame. The link sits at the heading's far side, before the technical name where there is
 * one, and looks as the report's links do. On paper it isn't there. The zoom is the screen's alone.
 */
const STYLE = [
  '.si-open { margin-left: auto; padding-left: 12px; font-size: 8.5pt; font-weight: 500; letter-spacing: 0; white-space: nowrap; }',
  '.si-open + .problem__tech { margin-left: 0; }',
  `@media screen { body { zoom: var(${ZOOM_PROPERTY}, 1); } }`,
  '@media print { .si-open { display: none; } }',
].join('\n');

/**
 * The report's document with the app's parts: a link in each problem's heading, named `openLabel`, and the app's style
 * first in its head. First, ahead of the document's own Content-Security-Policy, which allows its one style sheet
 * alone and governs only what comes after it (as reportScrollBars.ts found).
 */
export function withAppParts(text: string, openLabel: string): string {
  const label = openLabel.replace(/[&<>"]/g, (character) => `&#${character.charCodeAt(0)};`);
  const linked = text.replace(
    OPEN_MARK,
    (_mark, checkId: string) => `<a class="si-open" data-si-open="${checkId}" href="#problem-${checkId}">${label}</a>`,
  );
  const style = `<style id="${APP_PARTS_STYLE_ID}">\n${STYLE}\n</style>`;
  const head = /<head\b[^>]*>/i.exec(linked);
  if (!head) return `${style}${linked}`;
  const at = head.index + head[0].length;
  return `${linked.slice(0, at)}${style}${linked.slice(at)}`;
}

/** What a link in the report is: a problem to open in the app, a part of the report, or an address outside. */
export type ReportLink =
  { kind: 'check'; checkId: string } | { kind: 'part'; id: string } | { kind: 'url'; url: string };

/**
 * The link a click landed on, or in: null for anything else, and for a link that is none of the three (the engine
 * writes only http and https addresses and its own ids; anything else is left to do nothing, as the sandbox has it).
 * The target is the frame's own element, of another window than the page's: it is read by what it can do, not by
 * `instanceof`.
 */
export function reportLinkOf(target: EventTarget | null): ReportLink | null {
  const element = target as Partial<Element> | null;
  const anchor = typeof element?.closest === 'function' ? element.closest('a') : null;
  if (!anchor) return null;
  const checkId = anchor.getAttribute('data-si-open');
  if (checkId) return { kind: 'check', checkId };
  const href = anchor.getAttribute('href') ?? '';
  if (href.startsWith('#') && href.length > 1) return { kind: 'part', id: href.slice(1) };
  if (/^https?:\/\//i.test(href)) return { kind: 'url', url: href };
  return null;
}

export interface ReportLinkHandlers {
  /** An address outside the report: the app opens it in the browser. */
  onOpenUrl: (url: string) => void;
  /** A problem's own link: the app opens that problem (What to fix in Simple, Issues in Advanced). */
  onOpenCheck: (checkId: string) => void;
}

/**
 * Carries out clicks on the report's links, from the page: the frame runs no script, and can open no window. Enter on
 * a focused link is a click, so the keyboard has the same. A middle click opens an outside address too. Returns what
 * stops listening.
 */
export function listenToReportLinks(document: Document, handlers: ReportLinkHandlers): () => void {
  const follow = (event: Event) => {
    const link = reportLinkOf(event.target);
    if (!link) return;
    // The frame's own way with it is to do nothing (no window may open) or to try the address in the frame.
    event.preventDefault();
    if (link.kind === 'url') handlers.onOpenUrl(link.url);
    else if (link.kind === 'check') handlers.onOpenCheck(link.checkId);
    else if (event.type === 'click') document.getElementById(link.id)?.scrollIntoView({ block: 'start' });
  };
  const followMiddle = (event: Event) => {
    if ((event as MouseEvent).button === 1) follow(event);
  };
  document.addEventListener('click', follow);
  document.addEventListener('auxclick', followMiddle);
  return () => {
    document.removeEventListener('click', follow);
    document.removeEventListener('auxclick', followMiddle);
  };
}

/** The most the preview is magnified: half as big again reads well from a desk at 2560 px, and more is a poster. */
export const MAX_ZOOM = 1.5;

const ZOOM_STEP = 0.05;

/**
 * How far to zoom the sheet so its paper fills the pane (the audit's S-11: at 2560 px the sheet was 760 px of 12 px type
 * with 700 px blank either side): the room there is over the paper's width, never under 1 (a narrow pane reflows the
 * sheet, as before), never over `MAX_ZOOM`, and in steps, so dragging the window's edge doesn't redraw it each pixel.
 */
export function fitZoom(available: number, paper: number): number {
  if (!(available > 0) || !(paper > 0)) return 1;
  const fit = Math.floor(available / paper / ZOOM_STEP + 1e-9) * ZOOM_STEP;
  return Math.round(Math.min(MAX_ZOOM, Math.max(1, fit)) * 100) / 100;
}
