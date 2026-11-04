import { useQuery } from '@tanstack/react-query';
import { useLocation, useParams, useRouter } from '@tanstack/react-router';
import { ChevronLeft, ChevronRight, Pause, Play, RefreshCw, Search, Square } from 'lucide-react';
import { crawlQuery } from '../api/queries';
import { CRAWL_SECTIONS, sectionOf } from '../app/sections';
import { setOverlay } from '../app/overlays';
import { analyseCrawl, continueCrawl, pauseOrResume, stopCrawl } from '../crawl/actions';
import { crawlControls, crawlName, effectiveStatus } from '../crawl/status';
import { formatExactTime } from '../lib/format';
import { useLiveCrawl } from '../live/liveStore';
import { Button } from '../ui/Button';
import { Favicon } from '../ui/Favicon';
import { Icon } from '../ui/Icon';
import { IconButton } from '../ui/IconButton';
import { Kbd } from '../ui/Kbd';
import { StatusPill } from '../ui/StatusPill';
import { TextLink } from '../ui/TextLink';

// The crawl's status pill is the design system's now (ui/StatusPill.tsx); it's still exported from here, where it began.
export { StatusPill };

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
    <header className="flex h-(--topbar-height) shrink-0 items-center gap-3 border-b border-border-subtle bg-canvas pr-4 pl-3">
      <div className="flex gap-0.5">
        <IconButton
          label="Back"
          icon={ChevronLeft}
          size="sm"
          command="nav.back"
          onClick={() => router.history.back()}
        />
        <IconButton
          label="Forward"
          icon={ChevronRight}
          size="sm"
          command="nav.forward"
          onClick={() => router.history.forward()}
        />
      </div>

      <nav aria-label="Breadcrumb" className="min-w-0">
        <ol className="flex min-w-0 items-center gap-1 text-14 text-text-secondary">
          <li className="shrink-0">
            <TextLink variant="subtle" to="/" search={{}} className="rounded-sm px-1.5 py-1 hover:bg-surface-hover">
              Home
            </TextLink>
          </li>
          {crawlId ? (
            <>
              <li aria-hidden="true" className="shrink-0 text-text-muted">
                <Icon icon={ChevronRight} size="sm" />
              </li>
              <li
                className="flex min-w-0 items-center gap-2 px-1.5 font-medium text-text"
                title={crawl?.startUrl ?? undefined}
              >
                <Favicon site={crawl?.site ?? crawlName(crawl)} size="md" />
                <span className="truncate">{crawlName(crawl)}</span>
              </li>
              {crawl?.startedAt ? (
                <>
                  <li aria-hidden="true" className="shrink-0 text-text-muted">
                    <Icon icon={ChevronRight} size="sm" />
                  </li>
                  <li className="shrink-0 truncate px-1.5">{formatExactTime(crawl.startedAt)}</li>
                </>
              ) : null}
              {section ? (
                <>
                  <li aria-hidden="true" className="shrink-0 text-text-muted">
                    <Icon icon={ChevronRight} size="sm" />
                  </li>
                  <li aria-current="page" className="shrink-0 px-1.5 font-medium text-text">
                    {section.title}
                  </li>
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
            <Button
              size="sm"
              icon={Pause}
              command="crawl.pauseResume"
              showShortcut
              onClick={() => void pauseOrResume(crawlId, status)}
            >
              Pause
            </Button>
          ) : null}
          {controls.canResume ? (
            <Button
              size="sm"
              icon={Play}
              command="crawl.pauseResume"
              showShortcut
              onClick={() => void pauseOrResume(crawlId, status)}
            >
              Resume
            </Button>
          ) : null}
          {controls.canStop ? (
            <Button size="sm" icon={Square} command="crawl.stop" showShortcut onClick={() => void stopCrawl(crawlId)}>
              Stop
            </Button>
          ) : null}
          {controls.canContinue ? (
            <Button size="sm" icon={Play} command="crawl.continue" onClick={() => void continueCrawl(crawlId)}>
              Continue
            </Button>
          ) : null}
          {controls.canAnalyse && crawl?.outOfDate ? (
            <Button size="sm" icon={RefreshCw} command="crawl.analyse" onClick={() => void analyseCrawl(crawlId)}>
              Analyse again
            </Button>
          ) : null}
        </div>
      ) : null}

      <button
        type="button"
        onClick={() => setOverlay('palette')}
        className="ml-auto flex h-(--control-height) w-(--search-width) shrink-0 items-center gap-2 rounded-sm border border-border bg-surface pr-1.5 pl-2.5 text-13 text-text-muted shadow-control hover:border-border-strong hover:text-text-secondary"
      >
        <Icon icon={Search} size="sm" />
        <span className="flex-1 truncate text-left">Search or run a command…</span>
        <Kbd chord="mod+k" size="sm" />
      </button>
    </header>
  );
}
