import { useEffect, useImperativeHandle, useLayoutEffect, useMemo, useRef, useState, type Ref } from 'react';
import { ZOOM_PROPERTY, fitZoom, listenToReportLinks, withAppParts, type ReportLinkHandlers } from './reportAppParts';
import { appScrollBarLook, withAppScrollBars } from './reportScrollBars';

export interface ReportFrameHandle {
  /** Opens the system's print dialog on the report alone: how it's printed, and how it becomes a PDF. */
  print: () => boolean;
  /**
   * Round 5: the same, once the report is in the frame: for a print asked for as the screen opens (the Summary's Save
   * as PDF), which would otherwise print the blank page before it.
   */
  printWhenLoaded: () => void;
}

/** The paper's width on the canvas, as the token has it (`--paper-width`); null where there are no styles (tests). */
function paperWidth(): number | null {
  const width = Number.parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--paper-width'));
  return Number.isFinite(width) && width > 0 ? width : null;
}

/**
 * How far the sheet is zoomed to fill the pane it's in (fitZoom): the room inside the frame's parent, over the paper's
 * width. 1 until it's measured, and where nothing is laid out.
 */
function useFitZoom(frameRef: React.RefObject<HTMLIFrameElement | null>): number {
  const [zoom, setZoom] = useState(1);
  useLayoutEffect(() => {
    const pane = frameRef.current?.parentElement;
    const paper = paperWidth();
    if (!pane || paper === null || typeof ResizeObserver === 'undefined') return undefined;
    const measure = () => {
      const style = getComputedStyle(pane);
      const room = pane.clientWidth - Number.parseFloat(style.paddingLeft) - Number.parseFloat(style.paddingRight);
      setZoom(fitZoom(room, paper));
    };
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(pane);
    return () => observer.disconnect();
  }, [frameRef]);
  return zoom;
}

/**
 * The engine's report in a sandboxed frame (docs/v2-sidecar-api.md, "Report (E5)", "Showing it"): its document as
 * `srcdoc`, scripts off. `allow-same-origin` lets the page reach the frame to print it and to carry out its links, and
 * `allow-modals` lets the frame open the print dialog; neither lets anything run, and the report has no script.
 *
 * Its links work (the UX audit's S-11, where all 134 did nothing): the frame may open no window, so the page listens
 * for clicks in it and does what each link means (reportAppParts.ts). An address outside opens in the browser; a part
 * of the report ("Fix these first", the contents) scrolls into view; and each problem's heading has the app's own
 * link into that problem, which the saved file and paper don't.
 *
 * It is light in both themes, as paper is, scrolls with the app's own scroll bars (reportScrollBars.ts), and is zoomed
 * to fill a wide pane, on screen only: it prints at its own size.
 */
export function ReportFrame({
  document: text,
  title,
  openLabel,
  onOpenUrl,
  onOpenCheck,
  ref,
}: {
  /** The report's HTML, as the engine wrote it. */
  document: string;
  /** The frame's accessible name: "The report for gardenfurnituresets.co.uk". */
  title: string;
  /** What the link in each problem's heading says: "Open in What to fix", "Open in Issues". */
  openLabel: string;
  ref?: Ref<ReportFrameHandle> | undefined;
} & ReportLinkHandlers) {
  const frameRef = useRef<HTMLIFrameElement>(null);
  const shown = useMemo(() => withAppParts(withAppScrollBars(text, appScrollBarLook()), openLabel), [text, openLabel]);
  const zoom = useFitZoom(frameRef);

  // The handlers as they are now, for a listener that lives as long as the frame's document does.
  const handlers = useRef<ReportLinkHandlers>({ onOpenUrl, onOpenCheck });
  useEffect(() => {
    handlers.current = { onOpenUrl, onOpenCheck };
  }, [onOpenUrl, onOpenCheck]);

  // Each document the frame loads is listened to, and zoomed: a new cut is a new document.
  const stopListening = useRef<(() => void) | null>(null);
  const zoomNow = useRef(zoom);
  const applyZoom = (inside: Document | null | undefined, to: number) =>
    inside?.documentElement.style.setProperty(ZOOM_PROPERTY, String(to));
  const onLoad = () => {
    stopListening.current?.();
    const inside = frameRef.current?.contentDocument;
    if (!inside) return;
    stopListening.current = listenToReportLinks(inside, {
      onOpenUrl: (url) => handlers.current.onOpenUrl(url),
      onOpenCheck: (checkId) => handlers.current.onOpenCheck(checkId),
    });
    applyZoom(inside, zoomNow.current);
  };
  useEffect(() => {
    zoomNow.current = zoom;
    applyZoom(frameRef.current?.contentDocument, zoom);
  }, [zoom]);
  useEffect(() => () => stopListening.current?.(), []);

  useImperativeHandle(
    ref,
    () => ({
      print: () => {
        const view = frameRef.current?.contentWindow;
        if (!view) return false;
        view.focus();
        view.print();
        return true;
      },
      printWhenLoaded: () => {
        const frame = frameRef.current;
        if (!frame) return;
        const go = () => {
          frame.contentWindow?.focus();
          frame.contentWindow?.print();
        };
        const loaded = frame.contentDocument?.URL === 'about:srcdoc' && frame.contentDocument.readyState === 'complete';
        if (loaded) go();
        else frame.addEventListener('load', go, { once: true });
      },
    }),
    [],
  );
  return (
    <iframe
      ref={frameRef}
      srcDoc={shown}
      title={title}
      sandbox="allow-same-origin allow-modals"
      onLoad={onLoad}
      // The paper as wide as its zoom makes it: a page's width at 1, up to half as wide again in a wide pane.
      style={{ maxWidth: `calc(var(--paper-width) * ${zoom})` }}
      // The paper on the panel: white on white in light, so the card edge draws it (the framed workspace).
      className="block h-full w-full rounded-t-xs border border-b-0 border-border-card bg-(--color-paper) scheme-light shadow-2"
    />
  );
}
