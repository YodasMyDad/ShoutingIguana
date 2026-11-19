// The results' word on a Shopify store's refused pages (step 3b, lane shopify): how many weren't checked and why, with
// "Add a Shopify signature", which opens the form for one and then rechecks only those pages through the fix loop. In
// Simple's Summary and problem page, and in Advanced's Overview, beside its not-checked line. Shown only for a store
// that refused something; while its recheck runs, it says so instead, and once it's settled, what it read: in the
// prompt, when some pages are still refused, or in its place, when none are.
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { queryKeys, rechecksQuery, shopifyAccessQuery } from '../api/queries';
import { isRecheckRunning, pendingRecheck, refreshAfterRecheck, useRecheckRun } from '../crawl/recheck';
import { ShopifySteps } from '../crawl/options/ShopifyAccessFields';
import { useLiveCrawl } from '../live/liveStore';
import { Button } from '../ui/Button';
import { Callout } from '../ui/Callout';
import { AddSignatureDialog } from './AddSignatureDialog';
import { shopifyPromptWords, shopifyRecheckWords } from './shopifyWords';

export interface ShopifyPromptProps {
  crawlId: string;
  mode: 'simple' | 'advanced';
  className?: string | undefined;
}

export function ShopifyPrompt({ crawlId, mode, className }: ShopifyPromptProps) {
  const [adding, setAdding] = useState(false);
  const queryClient = useQueryClient();
  const run = useRecheckRun(crawlId);
  // Nothing is said of a crawl still crawling: its alert says it then, and the count is the end's.
  const status = useLiveCrawl(crawlId)?.status;
  const crawling = status === 'running' || status === 'paused' || status === 'stopping';
  const access = useQuery({ ...shopifyAccessQuery(crawlId), enabled: !crawling }).data ?? null;
  const words = access ? shopifyPromptWords(access, mode) : null;

  // The recheck the form started here: running, then settled, when what it read comes with the crawl's rechecks.
  const asked = pendingRecheck(crawlId);
  const ours = asked?.fromShopify === true && run?.recheckId === asked.recheckId;
  const rechecking = ours && isRecheckRunning(run);
  const settled = ours && run.stage === 'settled' ? run.recheckId : null;
  const rechecks = useQuery({ ...rechecksQuery(crawlId), enabled: settled !== null });
  useEffect(() => {
    if (settled === null) return;
    void refreshAfterRecheck(queryClient, crawlId);
    void queryClient.invalidateQueries({ queryKey: queryKeys.shopifyAccess(crawlId) });
  }, [settled, queryClient, crawlId]);
  const recheck = settled === null ? undefined : rechecks.data?.rechecks.find((item) => item.id === settled);
  const read = recheck ? shopifyRecheckWords(recheck.judged, recheck.answers, mode) : null;

  if (crawling) return null;
  if (!access || !words) {
    // None refused now: what the recheck read, where the prompt was.
    return read ? (
      <section aria-label={read.title} className={className} data-shopify-recheck="">
        <Callout tone="info" title={read.title} role="status">
          <p>{read.body}</p>
        </Callout>
      </section>
    ) : null;
  }

  return (
    <>
      <Callout
        tone="warning"
        title={words.title}
        className={className}
        actions={
          rechecking ? null : (
            <Button variant="secondary" size="sm" onClick={() => setAdding(true)}>
              {words.action}
            </Button>
          )
        }
      >
        <div className="flex flex-col gap-2" data-shopify-prompt="">
          {read ? <p role="status">{`${read.title}. ${read.body}`}</p> : null}
          <p>{words.body}</p>
          {rechecking ? (
            <p role="status">{mode === 'simple' ? 'Checking these pages again…' : 'Rechecking these URLs…'}</p>
          ) : words.steps ? (
            <ShopifySteps mode={mode} />
          ) : null}
        </div>
      </Callout>
      {adding ? (
        <AddSignatureDialog crawlId={crawlId} access={access} mode={mode} open onOpenChange={setAdding} />
      ) : null}
    </>
  );
}
