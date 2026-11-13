import { useParams } from '@tanstack/react-router';
import { useEffect, useRef, useState } from 'react';
import type { CrawlStatus } from '../api/types';
import { useCrawlState } from '../crawl/useCrawlState';
import { useFocusRescue } from '../feedback/useFocusRescue';
import { useCommands } from '../keyboard/registry';
import { useAnalysisControl } from '../screens/issues/useAnalysisControl';
import { CheckingScreen } from './checking/CheckingScreen';
import { SummaryScreen } from './SummaryScreen';

const ROUTE = '/crawls/$crawl/overview';

/** How long a crawl that has just ended waits for its analysis to begin before the Summary takes its place. */
export const HAND_OVER_GRACE_MS = 1_500;

/**
 * `/crawls/:crawl/overview` in Simple: checking the site while it runs and while its results are worked out, then the
 * Summary, in place (docs/v2-ui-simple.md, "While it checks the site" and "The report at the end" 1). One route, as
 * Overview's two states are. A crawl that has just ended stays on the checking screen's second step for the moment
 * before its analysis begins, so it never flashes "never analysed" on its way to the Summary.
 */
export function SimpleOverview() {
  const { crawl: crawlId } = useParams({ from: ROUTE });
  const { item, status, crawling, analysing } = useCrawlState(crawlId);
  const handingOver = useHandOver(crawlId, status, crawling, analysing);
  const checking = crawling || analysing || handingOver;
  const control = useAnalysisControl(crawlId, analysing, false);
  // The checking screen goes when the crawl ends: focus that was on it (Resume, Cancel) goes to the title of what
  // replaces it, never to the page.
  const screenRef = useRef<HTMLDivElement>(null);
  useFocusRescue(screenRef, () => screenRef.current?.querySelector<HTMLElement>('h1') ?? null);

  // Registered only while the checking screen shows: the Summary registers its own (Ctrl+E, Save the report).
  useCommands(
    () =>
      checking
        ? [
            {
              id: 'crawl.cancelAnalysis',
              title: 'Stop working out the results',
              group: 'Crawl',
              enabled: () => analysing && !control.cancelling,
              run: control.cancelHere,
            },
          ]
        : [],
    [checking, analysing, control.cancelling, crawlId],
  );

  return (
    <div ref={screenRef} className="@container">
      {checking ? (
        <CheckingScreen
          crawlId={crawlId}
          item={item}
          status={status}
          handingOver={handingOver && !analysing}
          onCancelAnalysis={analysing ? control.cancel : null}
          cancelling={control.cancelling}
          cancelRef={control.cancelRef}
          titleRef={control.titleRef}
        />
      ) : (
        <SummaryScreen />
      )}
    </div>
  );
}

/**
 * Whether a crawl this screen saw crawling has just completed and its analysis hasn't begun yet: the sidecar starts
 * the analysis a moment after it says the crawl completed. True for at most HAND_OVER_GRACE_MS.
 */
function useHandOver(crawlId: string, status: CrawlStatus | null, crawling: boolean, analysing: boolean): boolean {
  const [watched, setWatched] = useState<string | null>(null);
  const [handing, setHanding] = useState<string | null>(null);
  if (crawling && watched !== crawlId) setWatched(crawlId);
  if (!crawling && watched === crawlId) {
    setWatched(null);
    // Only a completed crawl goes on to its analysis by itself; a stopped one waits to be continued.
    if (!analysing && status === 'completed') setHanding(crawlId);
  }
  if (analysing && handing === crawlId) setHanding(null);
  const active = handing === crawlId;
  useEffect(() => {
    if (!active) return undefined;
    const timer = setTimeout(() => setHanding(null), HAND_OVER_GRACE_MS);
    return () => clearTimeout(timer);
  }, [active]);
  return active;
}
