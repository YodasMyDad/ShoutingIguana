import { useQuery } from '@tanstack/react-query';
import { useLocation, useParams, useRouter } from '@tanstack/react-router';
import clsx from 'clsx';
import { ChevronLeft, ChevronRight, Ellipsis, Pause, Play, RefreshCw, Search, Square } from 'lucide-react';
import { useRef, type ReactNode } from 'react';
import { crawlQuery } from '../api/queries';
import { crawlSectionEntry, sectionOf } from '../app/sections';
import { setOverlay } from '../app/overlays';
import { analyseCrawl, continueCrawl, pauseOrResume, stopCrawl } from '../crawl/actions';
import { crawlControls, crawlName, crawledUrls, effectiveStatus, statusLabel } from '../crawl/status';
import { useFocusRescue } from '../feedback/useFocusRescue';
import { ariaKeyShortcut, parseChord } from '../keyboard/keys';
import { formatCount, formatCountOf, formatExactTime } from '../lib/format';
import { isMac } from '../lib/platform';
import { useLiveCrawl } from '../live/liveStore';
import { useMode, type Mode } from '../mode/mode';
import { Button } from '../ui/Button';
import { CrawlFavicon } from '../crawl/CrawlFavicon';
import { Icon } from '../ui/Icon';
import { IconButton } from '../ui/IconButton';
import { Kbd } from '../ui/Kbd';
import { DropdownMenu, MenuItem } from '../ui/Menu';
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
  const { crawl: crawlId, check: checkId } = useParams({ strict: false });
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
  // At the bar's narrowest (step 10) the controls are in More: focus goes to More.
  useFocusRescue(controlsRef, () => {
    const inMore = barRef.current?.hasAttribute('data-give-10') === true;
    return (
      controlsRef.current?.querySelector<HTMLElement>(inMore ? '[data-more] button' : '[data-controls] button') ??
      document.getElementById('main')
    );
  });

  const date = crawl?.startedAt ? formatExactTime(crawl.startedAt) : null;
  const site = crawlId ? crawlName(crawl) : null;
  // The whole trail, for the tooltip, whatever has given way.
  const trail = ['Home', site, date, here ?? page].filter(Boolean).join(' › ');
  const fetched = crawledUrls(crawl, live?.progress);
  // The pill's words, for its tooltip once it's only its dot (step 11).
  const pillWords = status
    ? `${statusLabel(status, mode)}${fetched !== null && (status === 'running' || status === 'paused' || status === 'stopping') ? `, ${mode === 'simple' ? `${formatCount(fetched)} checked` : formatCountOf(fetched, 'URL')}` : ''}`
    : '';
  const crawlButtons = crawlId && status ? crawlControlList(controls, crawl?.outOfDate === true, mode) : [];
  const run = (id: ControlId) => {
    if (!crawlId || !status) return;
    if (id === 'pause' || id === 'resume') void pauseOrResume(crawlId, status);
    else if (id === 'stop') void stopCrawl(crawlId);
    else if (id === 'continue') void continueCrawl(crawlId);
    else void analyseCrawl(crawlId);
  };

  return (
    <header
      ref={barRef}
      data-mode={mode}
      className="group/topbar flex h-(--topbar-height) shrink-0 items-center gap-2 overflow-hidden border-b border-border pr-4 pl-3 *:shrink-0"
    >
      <div className="flex gap-0.5">
        <IconButton
          label="Back"
          icon={ChevronLeft}
          size="sm"
          command="nav.back"
          onClick={() => router.history.back()}
        />
        <span className="flex group-data-give-9/topbar:sr-only group-data-give-9/topbar:focus-within:not-sr-only">
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
            <li className="group-data-give-4/topbar:sr-only group-data-give-4/topbar:focus-within:not-sr-only">
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
                <CrawlFavicon item={crawl} site={site ?? ''} size="md" />
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
                  {/* Simple's problem is a page under What to fix: the crumb is the way back to the list. */}
                  <Here
                    name={here}
                    listOf={mode === 'simple' && sectionId === 'issues' && checkId !== undefined ? crawlId : null}
                  />
                </>
              ) : null}
            </>
          ) : page ? (
            <>
              <Separator className="group-data-give-4/topbar:hidden" />
              <Here name={page} />
            </>
          ) : null}
        </ol>
      </nav>

      {crawlId && status ? (
        <div
          ref={controlsRef}
          className="flex items-center gap-2 group-data-give-5/topbar:[&_.si-status-pill\_\_count]:sr-only group-data-give-11/topbar:[&_.si-status-pill\_\_word]:sr-only group-data-give-1/topbar:[&_kbd]:hidden"
        >
          <span className="flex" title={pillWords}>
            <StatusPill status={status} fetched={fetched} words={mode} />
          </span>
          <span data-controls="" className="flex items-center gap-2 group-data-give-10/topbar:hidden">
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
          </span>
          {crawlButtons.length > 0 ? <MoreMenu controls={crawlButtons} onRun={run} /> : null}
        </div>
      ) : null}

      <span aria-hidden="true" className="flex-1" />

      {/* With the crawl's controls, search joins them in More (step 10); without, its icon stays. */}
      <span className={clsx('flex', crawlButtons.length > 0 && 'group-data-give-10/topbar:hidden')}>
        <SearchButton iconOnly={mode === 'simple'} />
      </span>
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

/**
 * Where you are: the section, or the page. Short of room it's shortened (step 12), never to a letter, its whole name
 * in its tooltip and read in full; then it goes (step 13), still read.
 */
function Here({
  name,
  listOf = null,
}: {
  name: string;
  /** The crawl whose list of problems it leads to. */ listOf?: string | null;
}) {
  const className =
    'flex px-1.5 font-medium text-text group-data-give-12/topbar:min-w-(--topbar-section-min) group-data-give-12/topbar:shrink group-data-give-13/topbar:sr-only';
  if (listOf) {
    return (
      <li title={name} className={className}>
        <TextLink
          variant="subtle"
          to="/crawls/$crawl/issues/{-$check}"
          params={{ crawl: listOf, check: undefined }}
          className="min-w-0 truncate rounded-sm font-medium text-text hover:bg-surface-hover"
        >
          {name}
        </TextLink>
      </li>
    );
  }
  return (
    <li aria-current="page" title={name} className={className}>
      <span className="min-w-0 truncate">{name}</span>
    </li>
  );
}

type ControlId = 'pause' | 'resume' | 'stop' | 'continue' | 'analyse';

interface ControlEntry {
  id: ControlId;
  title: string;
  command: string;
}

/** The crawl's controls that show now, in the bar's order. */
function crawlControlList(controls: ReturnType<typeof crawlControls>, outOfDate: boolean, mode: Mode): ControlEntry[] {
  return [
    ...(controls.canPause ? [{ id: 'pause', title: 'Pause', command: 'crawl.pauseResume' } as const] : []),
    ...(controls.canResume ? [{ id: 'resume', title: 'Resume', command: 'crawl.pauseResume' } as const] : []),
    ...(controls.canStop ? [{ id: 'stop', title: 'Stop', command: 'crawl.stop' } as const] : []),
    ...(controls.canContinue ? [{ id: 'continue', title: 'Continue', command: 'crawl.continue' } as const] : []),
    ...(controls.canAnalyse && outOfDate
      ? [
          {
            id: 'analyse',
            title: mode === 'simple' ? 'Work out the results again' : 'Analyse again',
            command: 'crawl.analyse',
          } as const,
        ]
      : []),
  ];
}

/**
 * The bar at its narrowest (step 10): the crawl's controls and search in one menu, named for what's in it, each item
 * with its key.
 */
function MoreMenu({ controls, onRun }: { controls: ControlEntry[]; onRun: (id: ControlId) => void }) {
  const label = `More: ${[...controls.map((control) => control.title), 'Search'].join(', ')}`;
  return (
    <span data-more="" className="hidden group-data-give-10/topbar:flex">
      <DropdownMenu align="end" trigger={<IconButton label={label} icon={Ellipsis} size="sm" variant="secondary" />}>
        {controls.map((control) => (
          <MenuItem key={control.id} shortcut={commandChord(control.command)} onSelect={() => onRun(control.id)}>
            {control.title}
          </MenuItem>
        ))}
        <MenuItem shortcut="mod+k" onSelect={() => requestAnimationFrame(() => setOverlay('palette'))}>
          Search or run a command
        </MenuItem>
      </DropdownMenu>
    </span>
  );
}

/** The key a control's command has, shown in its menu item. */
function commandChord(command: string): string | undefined {
  return command === 'crawl.pauseResume' ? 'f7' : command === 'crawl.stop' ? 'f8' : undefined;
}
