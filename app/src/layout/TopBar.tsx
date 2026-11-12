import { useQuery } from '@tanstack/react-query';
import { useLocation, useParams, useRouter } from '@tanstack/react-router';
import clsx from 'clsx';
import { ChevronLeft, ChevronRight, Pause, Play, RefreshCw, Search, Square } from 'lucide-react';
import { useRef, type ReactNode } from 'react';
import { crawlQuery } from '../api/queries';
import { crawlSectionEntry, sectionOf } from '../app/sections';
import { setOverlay } from '../app/overlays';
import { analyseCrawl, continueCrawl, pauseOrResume, stopCrawl } from '../crawl/actions';
import { crawlControls, crawlName, effectiveStatus } from '../crawl/status';
import { useFocusRescue } from '../feedback/useFocusRescue';
import { ariaKeyShortcut, parseChord } from '../keyboard/keys';
import { formatExactTime } from '../lib/format';
import { isMac } from '../lib/platform';
import { useLiveCrawl } from '../live/liveStore';
import { useMode, type Mode } from '../mode/mode';
import { Button } from '../ui/Button';
import { Favicon } from '../ui/Favicon';
import { Icon } from '../ui/Icon';
import { IconButton } from '../ui/IconButton';
import { Kbd } from '../ui/Kbd';
import { StatusPill } from '../ui/StatusPill';
import { TextLink } from '../ui/TextLink';
import { Tooltip } from '../ui/Tooltip';
import { ModeSwitch } from './ModeSwitch';
import { useGiveWay } from './useGiveWay';

/** A crawl control's words: once the bar is short of room (step 6), read but not shown, so the button keeps its name. */
function ControlWords({ children }: { children: string }) {
  return <span className="group-data-give-6/topbar:sr-only">{children}</span>;
}

// The crawl's status pill is the design system's now (ui/StatusPill.tsx); it's still exported from here, where it began.
export { StatusPill };

/** The crumbs' separator: drawn, not read (the breadcrumb's list says where each part ends). */
function Separator({ className }: { className?: string }) {
  return (
    <li aria-hidden="true" className={clsx('shrink-0 text-text-muted', className)}>
      <Icon icon={ChevronRight} size="sm" />
    </li>
  );
}

/** Where you are outside a crawl: the page's own name, after Home. */
function pageTitle(pathname: string, mode: Mode): string | null {
  if (pathname === '/new') return mode === 'simple' ? 'Check a website' : 'New crawl';
  if (pathname === '/settings' || pathname.startsWith('/settings/')) return 'Settings';
  return null;
}

/**
 * The top bar (docs/v2-ui-ia.md, The shell; docs/v2-ui-simple.md, Top bar): back and forward, where you are, the
 * crawl's status and controls, search, and the switch. The same on every screen, so a running crawl is always visible
 * and controllable.
 *
 * Short of room, its parts give way in a fixed order, measured on the bar's own width (useGiveWay): the keys inside
 * buttons, the crawl's date, the search field (to its icon), Home, the status pill's count, the controls' words (to
 * their icons), the site (shortened, then gone), Forward. Never Back, the section you're on, the status word, the controls, search's icon or
 * the switch. Each part that gives way keeps its whole name for a screen reader, and in a tooltip.
 */
export function TopBar() {
  const router = useRouter();
  const mode = useMode();
  const { crawl: crawlId } = useParams({ strict: false });
  const pathname = useLocation({ select: (location) => location.pathname });
  const crawl = useQuery({ ...crawlQuery(crawlId ?? ''), enabled: crawlId !== undefined }).data;
  const live = useLiveCrawl(crawlId);
  const status = crawlId ? effectiveStatus(crawl, live) : null;
  const controls = crawlControls(status, crawl);
  const sectionId = sectionOf(pathname);
  const section = sectionId ? crawlSectionEntry(sectionId) : null;
  const here = section ? ((mode === 'simple' ? section.simpleTitle : undefined) ?? section.title) : null;
  const page = crawlId ? null : pageTitle(pathname, mode);
  const onHome = pathname === '/';
  const barRef = useRef<HTMLElement>(null);
  useGiveWay(barRef);
  // A control pressed goes once it has worked (Pause becomes Resume, Stop goes while the crawl stops): focus goes to
  // the control that took its place, or to the view when none did, never to the page.
  const controlsRef = useRef<HTMLDivElement>(null);
  useFocusRescue(
    controlsRef,
    () => controlsRef.current?.querySelector<HTMLElement>('button') ?? document.getElementById('main'),
  );

  const date = crawl?.startedAt ? formatExactTime(crawl.startedAt) : null;
  const site = crawlId ? crawlName(crawl) : null;
  // The whole trail, for the tooltip, whatever has given way.
  const trail = ['Home', site, date, here ?? page].filter(Boolean).join(' › ');

  return (
    <header
      ref={barRef}
      data-mode={mode}
      className="group/topbar flex h-(--topbar-height) shrink-0 items-center gap-2 overflow-hidden border-b border-border-subtle bg-canvas pr-4 pl-3 *:shrink-0"
    >
      <div className="flex gap-0.5">
        <IconButton
          label="Back"
          icon={ChevronLeft}
          size="sm"
          command="nav.back"
          onClick={() => router.history.back()}
        />
        <span className="flex group-data-give-9/topbar:hidden">
          <IconButton
            label="Forward"
            icon={ChevronRight}
            size="sm"
            command="nav.forward"
            onClick={() => router.history.forward()}
          />
        </span>
      </div>

      {/* Narrowed (step 7), it clips what doesn't fit, and the measure counts that as not fitting. The inset keeps
          Home's focus ring inside it. */}
      <nav
        aria-label="Breadcrumb"
        data-give-fit=""
        className="-ml-1 overflow-hidden py-1 pl-1 group-data-give-7/topbar:min-w-0 group-data-give-7/topbar:shrink"
        title={trail}
      >
        <ol className="flex min-w-0 items-center gap-1 text-14 text-text-secondary *:shrink-0">
          {onHome ? (
            <li aria-current="page" className="px-1.5 font-medium text-text">
              Home
            </li>
          ) : (
            <li className="group-data-give-4/topbar:hidden">
              <TextLink variant="subtle" to="/" search={{}} className="rounded-sm px-1.5 py-1 hover:bg-surface-hover">
                Home
              </TextLink>
            </li>
          )}
          {crawlId ? (
            <>
              <Separator className="group-data-give-4/topbar:hidden" />
              <li
                className="flex max-w-(--topbar-site-max) items-center gap-2 px-1.5 font-medium text-text group-data-give-7/topbar:min-w-(--topbar-site-min) group-data-give-7/topbar:shrink group-data-give-8/topbar:sr-only"
                title={crawl?.startUrl ?? site ?? undefined}
              >
                <Favicon site={crawl?.site ?? site ?? ''} size="md" />
                <span className="min-w-0 truncate">{site}</span>
              </li>
              {date ? (
                <>
                  <Separator className="group-data-give-2/topbar:hidden" />
                  <li className="px-1.5 group-data-give-2/topbar:sr-only">{date}</li>
                </>
              ) : null}
              {here ? (
                <>
                  <Separator className="group-data-give-8/topbar:hidden" />
                  <li aria-current="page" className="px-1.5 font-medium text-text">
                    {here}
                  </li>
                </>
              ) : null}
            </>
          ) : page ? (
            <>
              <Separator className="group-data-give-4/topbar:hidden" />
              <li aria-current="page" className="px-1.5 font-medium text-text">
                {page}
              </li>
            </>
          ) : null}
        </ol>
      </nav>

      {crawlId && status ? (
        <div
          ref={controlsRef}
          className="flex items-center gap-2 group-data-give-5/topbar:[&_.si-status-pill\_\_count]:sr-only group-data-give-1/topbar:[&_kbd]:hidden"
        >
          <StatusPill
            status={status}
            fetched={live?.progress?.fetched ?? crawl?.fetchedUrlCount ?? null}
            words={mode}
          />
          {controls.canPause ? (
            <ControlButton
              title="Pause"
              icon={Pause}
              command="crawl.pauseResume"
              keys
              onClick={() => void pauseOrResume(crawlId, status)}
            />
          ) : null}
          {controls.canResume ? (
            <ControlButton
              title="Resume"
              icon={Play}
              command="crawl.pauseResume"
              keys
              onClick={() => void pauseOrResume(crawlId, status)}
            />
          ) : null}
          {controls.canStop ? (
            <ControlButton
              title="Stop"
              icon={Square}
              command="crawl.stop"
              keys
              onClick={() => void stopCrawl(crawlId)}
            />
          ) : null}
          {controls.canContinue ? (
            <ControlButton
              title="Continue"
              icon={Play}
              command="crawl.continue"
              onClick={() => void continueCrawl(crawlId)}
            />
          ) : null}
          {controls.canAnalyse && crawl?.outOfDate ? (
            <ControlButton
              title={mode === 'simple' ? 'Work out the results again' : 'Analyse again'}
              icon={RefreshCw}
              command="crawl.analyse"
              onClick={() => void analyseCrawl(crawlId)}
            />
          ) : null}
        </div>
      ) : null}

      <span aria-hidden="true" className="flex-1" />

      <SearchButton iconOnly={mode === 'simple'} />
      <ModeSwitch />
    </header>
  );
}

function ControlButton({
  title,
  icon,
  command,
  keys = false,
  onClick,
}: {
  title: string;
  icon: typeof Pause;
  command: string;
  /** Shows the command's key inside the button (F7, F8), until the bar is short of room. */
  keys?: boolean;
  onClick: () => void;
}): ReactNode {
  return (
    <Button
      size="sm"
      icon={icon}
      command={command}
      showShortcut={keys}
      title={title}
      className="group-data-give-6/topbar:w-(--control-height-sm) group-data-give-6/topbar:px-0"
      onClick={onClick}
    >
      <ControlWords>{title}</ControlWords>
    </Button>
  );
}

/**
 * Search and the palette (Ctrl+K). The field, with its words and keys; in Simple, and in Advanced once the bar is short
 * of room (step 3), its icon. Either way it's named for its words, with the keys in its tooltip.
 */
function SearchButton({ iconOnly }: { iconOnly: boolean }) {
  return (
    <Tooltip
      content="Search or run a command"
      chord="mod+k"
      triggerName={iconOnly ? 'Search or run a command' : 'Search or run a command…'}
    >
      <button
        type="button"
        onClick={() => setOverlay('palette')}
        // Its words name it; as its icon (Simple), it's named for them.
        aria-label={iconOnly ? 'Search or run a command' : undefined}
        aria-keyshortcuts={ariaKeyShortcut(parseChord('mod+k'), isMac)}
        data-icon-only={iconOnly ? '' : undefined}
        className={clsx(
          'flex h-(--control-height) items-center gap-2 rounded-sm border border-border bg-surface text-13 text-text-muted shadow-control hover:border-border-strong hover:text-text-secondary',
          iconOnly
            ? 'w-(--control-height) justify-center'
            : 'w-(--search-width) pr-1.5 pl-2.5 group-data-give-3/topbar:w-(--control-height) group-data-give-3/topbar:justify-center group-data-give-3/topbar:p-0',
        )}
      >
        <Icon icon={Search} size="sm" />
        {iconOnly ? null : (
          <>
            {/* Its name: short of room (step 3), read but not shown. */}
            <span className="flex-1 truncate text-left group-data-give-3/topbar:sr-only">Search or run a command…</span>
            <span className="flex group-data-give-3/topbar:hidden">
              <Kbd chord="mod+k" size="sm" aria-hidden />
            </span>
          </>
        )}
      </button>
    </Tooltip>
  );
}
