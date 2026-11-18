// The results' word on a Shopify store's refused pages (step 3b, lane shopify): how many weren't checked and why, with
// "Add a Shopify signature", which opens the form for one and then rechecks only those pages through the fix loop. In
// Simple's Summary and problem page, and in Advanced's Overview, beside its not-checked line. Shown only for a store
// that refused something; while its recheck runs, it says so instead.
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { shopifyAccessQuery } from '../api/queries';
import { isRecheckRunning, pendingRecheck, useRecheckRun } from '../crawl/recheck';
import { ShopifySteps } from '../crawl/options/ShopifyAccessFields';
import { useLiveCrawl } from '../live/liveStore';
import { Button } from '../ui/Button';
import { Callout } from '../ui/Callout';
import { AddSignatureDialog } from './AddSignatureDialog';
import { shopifyPromptWords } from './shopifyWords';

export interface ShopifyPromptProps {
  crawlId: string;
  mode: 'simple' | 'advanced';
  className?: string | undefined;
}

export function ShopifyPrompt({ crawlId, mode, className }: ShopifyPromptProps) {
  const [adding, setAdding] = useState(false);
  const run = useRecheckRun(crawlId);
  // Nothing is said of a crawl still crawling: its alert says it then, and the count is the end's.
  const status = useLiveCrawl(crawlId)?.status;
  const crawling = status === 'running' || status === 'paused' || status === 'stopping';
  const access = useQuery({ ...shopifyAccessQuery(crawlId), enabled: !crawling }).data ?? null;
  const words = access ? shopifyPromptWords(access, mode) : null;
  if (crawling || !access || !words) return null;

  const ours = pendingRecheck(crawlId)?.ask.urlIds !== undefined;
  const rechecking = ours && isRecheckRunning(run);
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
