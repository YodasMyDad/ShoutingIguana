import { useEffect, useRef, useState, type RefObject } from 'react';
import { analyseCrawl, cancelAnalysis } from '../../crawl/actions';
import { announce } from '../../feedback/announce';
import { getMode, type Mode } from '../../mode/mode';

export interface AnalysisControl {
  /** The page's title: where focus goes when the progress banner, which had it, goes. */
  titleRef: RefObject<HTMLHeadingElement | null>;
  /** The banner's Cancel: where focus goes when the button that started the analysis is replaced by the banner. */
  cancelRef: RefObject<HTMLButtonElement | null>;
  analyse: () => void;
  /** Cancels the analysis running now. */
  cancelHere: () => void;
  /** `cancelHere` while an analysis runs, null otherwise: what the banner's Cancel takes. */
  cancel: (() => void) | null;
  /** Cancel was pressed, and the engine is stopping the analysis. */
  cancelling: boolean;
}

/** What a cancel says, in the mode's words: a first analysis cancelled leaves none; a later one, the last. */
function cancelledWords(neverAnalysed: boolean, mode: Mode): string {
  if (mode === 'simple') {
    return neverAnalysed
      ? 'Working out the results was cancelled. The results haven’t been worked out for this check.'
      : 'Working out the results was cancelled. These are the results worked out before.';
  }
  return neverAnalysed
    ? 'Analysis cancelled. This crawl still hasn’t been analysed.'
    : 'Analysis cancelled. These are the issues from the last analysis.';
}

/**
 * Analysing from a screen (Issues, Overview): the button pressed goes when the progress banner comes, so focus goes to
 * its Cancel; when the banner goes, focus that was in it goes to the page's title. Starting and finishing are announced
 * with the crawl's status (live/liveBridge.ts), and a cancel is announced here, as the status alone reads "finished".
 */
export function useAnalysisControl(crawlId: string, analysing: boolean, neverAnalysed: boolean): AnalysisControl {
  const titleRef = useRef<HTMLHeadingElement>(null);
  const cancelRef = useRef<HTMLButtonElement>(null);
  const startedHere = useRef(false);
  const cancelRequested = useRef(false);
  const [cancelling, setCancelling] = useState(false);
  if (cancelling && !analysing) setCancelling(false);
  const analyse = () => {
    startedHere.current = true;
    void analyseCrawl(crawlId);
  };
  const cancelHere = () => {
    cancelRequested.current = true;
    setCancelling(true);
    void cancelAnalysis(crawlId);
  };
  const wasAnalysing = useRef(analysing);
  useEffect(() => {
    if (analysing === wasAnalysing.current) return;
    wasAnalysing.current = analysing;
    const focusLost = document.activeElement === null || document.activeElement === document.body;
    if (analysing) {
      if (startedHere.current && focusLost) cancelRef.current?.focus();
      startedHere.current = false;
      return;
    }
    if (cancelRequested.current) announce(cancelledWords(neverAnalysed, getMode()));
    cancelRequested.current = false;
    if (focusLost) titleRef.current?.focus();
  }, [analysing, neverAnalysed]);
  return { titleRef, cancelRef, analyse, cancelHere, cancel: analysing ? cancelHere : null, cancelling };
}
