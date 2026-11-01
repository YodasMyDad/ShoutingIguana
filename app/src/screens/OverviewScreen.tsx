import { useQuery } from '@tanstack/react-query';
import { useParams } from '@tanstack/react-router';
import { crawlQuery } from '../api/queries';
import { isLive } from '../api/types';
import { effectiveStatus, statusLabel } from '../crawl/status';
import { formatBytes, formatCount, formatDuration, formatRate } from '../lib/format';
import { useLiveCrawl } from '../live/liveStore';
import { ScreenStub } from './ScreenStub';

/**
 * Overview: the live crawl while it runs, then the answer. Foundation version: the live counters, straight from the
 * live store (rendered at most 4 times a second), so the slice starts from working data.
 */
export function OverviewScreen() {
  const { crawl: crawlId } = useParams({ from: '/crawls/$crawl/overview' });
  const crawl = useQuery(crawlQuery(crawlId)).data;
  const live = useLiveCrawl(crawlId);
  const status = effectiveStatus(crawl, live);
  const progress = live?.progress;

  return (
    <ScreenStub
      title="Overview"
      purpose="While the crawl runs: crawled, found and queued, the rate, the status codes and the alerts. Once it’s analysed: the issues by severity, what to fix first, and every number leading to its URLs."
    >
      {progress && status && isLive(status) ? (
        <dl aria-label="Live progress" aria-live="off" className="grid max-w-(--content-max) grid-cols-4 gap-4">
          <Figure label="Status" value={statusLabel(status)} />
          <Figure label="Crawled" value={formatCount(progress.fetched)} />
          <Figure label="Found" value={formatCount(progress.discovered)} />
          <Figure label="Queued" value={formatCount(progress.queued)} />
          <Figure label="URLs a second" value={formatRate(progress.urlsPerSecond)} />
          <Figure label="Pages" value={formatCount(progress.htmlPages)} />
          <Figure label="Elapsed" value={formatDuration(progress.elapsedMs)} />
          <Figure label="Downloaded" value={formatBytes(progress.bytesDownloaded)} />
        </dl>
      ) : crawl ? (
        <dl className="grid max-w-(--content-max) grid-cols-4 gap-4">
          <Figure label="Status" value={status ? statusLabel(status) : '–'} />
          <Figure label="URLs" value={formatCount(crawl.fetchedUrlCount)} />
          <Figure label="Pages" value={formatCount(crawl.pageCount)} />
          <Figure
            label="Checks with issues"
            value={
              crawl.issues
                ? formatCount(crawl.issues.error + crawl.issues.warning + crawl.issues.notice)
                : 'Not analysed'
            }
          />
        </dl>
      ) : null}
    </ScreenStub>
  );
}

function Figure({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1 rounded-lg border border-border bg-surface p-4">
      <dt className="text-12 text-text-muted">{label}</dt>
      <dd className="figures text-21 font-semibold tracking-figure">{value}</dd>
    </div>
  );
}
