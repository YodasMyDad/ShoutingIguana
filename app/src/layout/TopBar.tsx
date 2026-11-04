import { useQuery } from '@tanstack/react-query';
import { Link, useLocation, useParams, useRouter } from '@tanstack/react-router';
import { crawlQuery } from '../api/queries';
import type { CrawlStatus } from '../api/types';
import { CRAWL_SECTIONS, sectionOf } from '../app/sections';
import { setOverlay } from '../app/overlays';
import { analyseCrawl, continueCrawl, pauseOrResume, stopCrawl } from '../crawl/actions';
import { crawlControls, crawlName, effectiveStatus, statusLabel } from '../crawl/status';
import { formatCountOf, formatExactTime } from '../lib/format';
import { useLiveCrawl } from '../live/liveStore';
import { Button } from '../ui/Button';
import { Kbd } from '../ui/Kbd';

/**
 * The top bar (docs/v2-ui-ia.md, The shell): back and forward, where you are, the crawl's status and controls, and
 * the palette. The same on every screen, so a running crawl is always visible and controllable.
 */
export function TopBar() {
  const router = useRouter();
  const { crawl: crawlId } = useParams({ strict: false });
  const pathname = useLocation({ select: (location) => location.pathname });
  const crawl = useQuery({ ...crawlQuery(crawlId ?? ''), enabled: crawlId !== undefined }).data;
  const live = useLiveCrawl(crawlId);
  const status = crawlId ? effectiveStatus(crawl, live) : null;
  const controls = crawlControls(status, crawl);
  const section = CRAWL_SECTIONS.find((candidate) => candidate.id === sectionOf(pathname));

  return (
    <header className="flex h-(--topbar-height) shrink-0 items-center gap-3 border-b border-border bg-surface px-4">
      <div className="flex gap-1">
        <Button
          variant="ghost"
          size="sm"
          command="nav.back"
          title="Back"
          aria-label="Back"
          onClick={() => router.history.back()}
        >
          ←
        </Button>
        <Button
          variant="ghost"
          size="sm"
          command="nav.forward"
          title="Forward"
          aria-label="Forward"
          onClick={() => router.history.forward()}
        >
          →
        </Button>
      </div>

      <nav aria-label="Breadcrumb" className="min-w-0">
        <ol className="flex items-center gap-1.5 text-13 text-text-secondary">
          <li>
            <Link to="/" search={{}} className="hover:text-text">
              Home
            </Link>
          </li>
          {crawlId ? (
            <>
              <li aria-hidden="true">›</li>
              <li className="truncate font-medium text-text" title={crawl?.startUrl ?? undefined}>
                {crawlName(crawl)}
              </li>
              {crawl?.startedAt ? (
                <>
                  <li aria-hidden="true">›</li>
                  <li className="truncate">{formatExactTime(crawl.startedAt)}</li>
                </>
              ) : null}
              {section ? (
                <>
                  <li aria-hidden="true">›</li>
                  <li aria-current="page">{section.title}</li>
                </>
              ) : null}
            </>
          ) : null}
        </ol>
      </nav>

      {crawlId && status ? (
        <div className="flex items-center gap-2">
          <StatusPill status={status} fetched={live?.progress?.fetched ?? crawl?.fetchedUrlCount ?? null} />
          {controls.canPause ? (
            <Button size="sm" command="crawl.pauseResume" onClick={() => void pauseOrResume(crawlId, status)}>
              Pause
            </Button>
          ) : null}
          {controls.canResume ? (
            <Button size="sm" command="crawl.pauseResume" onClick={() => void pauseOrResume(crawlId, status)}>
              Resume
            </Button>
          ) : null}
          {controls.canStop ? (
            <Button size="sm" command="crawl.stop" onClick={() => void stopCrawl(crawlId)}>
              Stop
            </Button>
          ) : null}
          {controls.canContinue ? (
            <Button size="sm" command="crawl.continue" onClick={() => void continueCrawl(crawlId)}>
              Continue
            </Button>
          ) : null}
          {controls.canAnalyse && crawl?.outOfDate ? (
            <Button size="sm" command="crawl.analyse" onClick={() => void analyseCrawl(crawlId)}>
              Analyse again
            </Button>
          ) : null}
        </div>
      ) : null}

      <button
        type="button"
        onClick={() => setOverlay('palette')}
        className="ml-auto flex h-(--control-height-sm) w-(--search-width) items-center gap-2 rounded-sm border border-border bg-surface-sunken px-2.5 text-13 text-text-muted hover:bg-surface-hover"
      >
        <span className="truncate">Search or run a command…</span>
        <Kbd chord="mod+k" className="ml-auto inline-flex gap-0.5" />
      </button>
    </header>
  );
}

/** The crawl's status: always a word, never colour alone. Announced through the live region by the live bridge. */
export function StatusPill({ status, fetched }: { status: CrawlStatus; fetched: number | null }) {
  const busy = status === 'running' || status === 'analyzing' || status === 'stopping';
  return (
    <span className="inline-flex h-(--control-height-xs) items-center gap-1.5 rounded-full border border-border px-2.5 text-12 font-medium">
      <span
        aria-hidden="true"
        className={
          busy
            ? 'size-(--mark-size) rounded-full bg-accent motion-safe:animate-pulse'
            : 'size-(--mark-size) rounded-full bg-text-muted'
        }
      />
      {statusLabel(status)}
      {fetched !== null && (status === 'running' || status === 'paused' || status === 'stopping') ? (
        <span className="figures text-text-muted">{formatCountOf(fetched, 'URL')}</span>
      ) : null}
    </span>
  );
}
