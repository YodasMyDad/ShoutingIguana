import { useQuery } from '@tanstack/react-query';
import { useLocation, useParams, useRouter } from '@tanstack/react-router';
import { ChevronLeft, ChevronRight, Pause, Play, RefreshCw, Search, Square } from 'lucide-react';
import { useRef } from 'react';
import { crawlQuery } from '../api/queries';
import { CRAWL_SECTIONS, sectionOf } from '../app/sections';
import { setOverlay } from '../app/overlays';
import { analyseCrawl, continueCrawl, pauseOrResume, stopCrawl } from '../crawl/actions';
import { crawlControls, crawlName, effectiveStatus } from '../crawl/status';
import { useFocusRescue } from '../feedback/useFocusRescue';
import { ariaKeyShortcut, parseChord } from '../keyboard/keys';
import { formatExactTime } from '../lib/format';
import { isMac } from '../lib/platform';
import { useLiveCrawl } from '../live/liveStore';
import { Button } from '../ui/Button';
import { Favicon } from '../ui/Favicon';
import { Icon } from '../ui/Icon';
import { IconButton } from '../ui/IconButton';
import { Kbd } from '../ui/Kbd';
import { StatusPill } from '../ui/StatusPill';
import { TextLink } from '../ui/TextLink';

/** A crawl control's words: in a top bar short of room, read but not shown, so the button keeps its name. */
function ControlWords({ children }: { children: string }) {
  return <span className="@max-topbar-words/topbar:sr-only">{children}</span>;
}

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
  // A control pressed goes once it has worked (Pause becomes Resume, Stop goes while the crawl stops): focus goes to
  // the control that took its place, or to the view when none did, never to the page.
  const controlsRef = useRef<HTMLDivElement>(null);
  useFocusRescue(
    controlsRef,
    () => controlsRef.current?.querySelector<HTMLElement>('button') ?? document.getElementById('main'),
  );

  return (
    <header className="@container/topbar flex h-(--topbar-height) shrink-0 items-center gap-3 border-b border-border-subtle bg-canvas pr-4 pl-3">
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

      {/* Short of room (200% zoom), the crumbs are cut at their edge rather than run under the palette button; the
          inset keeps Home's focus ring inside that edge. */}
      <nav aria-label="Breadcrumb" className="-ml-1 min-w-0 overflow-x-clip pl-1">
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
                  {/* Short of room (200% zoom), the date gives way first (after the palette button's words), then the
                      crawl's name. */}
                  <li className="min-w-0 shrink-100 truncate px-1.5" title={formatExactTime(crawl.startedAt)}>
                    {formatExactTime(crawl.startedAt)}
                  </li>
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
        // Short of room (200% zoom with a running crawl's Pause and Stop), the controls' keys go, then their words.
        <div ref={controlsRef} className="flex shrink-0 items-center gap-2 @max-topbar-keys/topbar:[&_kbd]:hidden">
          <StatusPill status={status} fetched={live?.progress?.fetched ?? crawl?.fetchedUrlCount ?? null} />
          {controls.canPause ? (
            <Button
              size="sm"
              icon={Pause}
              command="crawl.pauseResume"
              showShortcut
              onClick={() => void pauseOrResume(crawlId, status)}
            >
              <ControlWords>Pause</ControlWords>
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
              <ControlWords>Resume</ControlWords>
            </Button>
          ) : null}
          {controls.canStop ? (
            <Button size="sm" icon={Square} command="crawl.stop" showShortcut onClick={() => void stopCrawl(crawlId)}>
              <ControlWords>Stop</ControlWords>
            </Button>
          ) : null}
          {controls.canContinue ? (
            <Button size="sm" icon={Play} command="crawl.continue" onClick={() => void continueCrawl(crawlId)}>
              <ControlWords>Continue</ControlWords>
            </Button>
          ) : null}
          {controls.canAnalyse && crawl?.outOfDate ? (
            <Button size="sm" icon={RefreshCw} command="crawl.analyse" onClick={() => void analyseCrawl(crawlId)}>
              <ControlWords>Analyse again</ControlWords>
            </Button>
          ) : null}
        </div>
      ) : null}

      <button
        type="button"
        onClick={() => setOverlay('palette')}
        aria-keyshortcuts={ariaKeyShortcut(parseChord('mod+k'), isMac)}
        // Narrowed (200% zoom), its words give way before the crumbs do, down to the magnifier and the keys.
        className="ml-auto flex h-(--control-height) w-(--search-width) shrink-100 items-center gap-2 rounded-sm border border-border bg-surface pr-1.5 pl-2.5 text-13 text-text-muted shadow-control hover:border-border-strong hover:text-text-secondary"
      >
        <Icon icon={Search} size="sm" />
        {/* No width of its own (w-0), so the button can narrow to the magnifier and the keys. */}
        <span className="w-0 flex-1 truncate text-left">Search or run a command…</span>
        <Kbd chord="mod+k" size="sm" aria-hidden />
      </button>
    </header>
  );
}
