import { useImperativeHandle, useMemo, useRef, type Ref } from 'react';
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

/**
 * The engine's report in a sandboxed frame (docs/v2-sidecar-api.md, "Report (E5)", "Showing it"): its document as
 * `srcdoc`, scripts off. `allow-same-origin` lets the page reach the frame to print it, and `allow-modals` lets the
 * frame open the print dialog; neither lets anything run, and the report has no script. Its links open nothing here
 * (they would need `allow-popups`): in the saved file they work. It is light in both themes, as paper is, and scrolls
 * with the app's own scroll bars (reportScrollBars.ts), added to the frame's copy only.
 */
export function ReportFrame({
  document: text,
  title,
  ref,
}: {
  /** The report's HTML, as the engine wrote it. */
  document: string;
  /** The frame's accessible name: "The report for gardenfurnituresets.co.uk". */
  title: string;
  ref?: Ref<ReportFrameHandle> | undefined;
}) {
  const frameRef = useRef<HTMLIFrameElement>(null);
  const shown = useMemo(() => withAppScrollBars(text, appScrollBarLook()), [text]);
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
      // The paper on the panel: white on white in light, so the card edge draws it (the framed workspace).
      className="block h-full w-full max-w-(--paper-width) rounded-t-xs border border-b-0 border-border-card bg-(--color-paper) scheme-light shadow-2"
    />
  );
}
