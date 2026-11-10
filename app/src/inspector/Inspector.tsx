import { useQuery } from '@tanstack/react-query';
import clsx from 'clsx';
import {
  Check,
  Copy,
  ExternalLink,
  File,
  FileQuestion,
  FileText,
  Image as ImageIcon,
  PanelBottom,
  PanelRight,
  X,
} from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { api } from '../api/client';
import { isProblem } from '../api/errors';
import type { UrlDetail } from '../api/inspector';
import { crawlQuery, queryKeys, urlDetailQuery } from '../api/queries';
import { isCrawling } from '../api/types';
import { effectiveStatus } from '../crawl/status';
import { announce } from '../feedback/announce';
import { RegionBoundary } from '../feedback/RegionBoundary';
import { Skeleton } from '../feedback/Skeleton';
import { useDelayedLoading } from '../feedback/useDelayedLoading';
import { useCommands, type Command } from '../keyboard/registry';
import { copyText } from '../lib/clipboard';
import { useLiveCrawl } from '../live/liveStore';
import { arrivedByName, useGoneToByName, useInspector } from '../routing/inspector';
import { updateSettings, useSettings } from '../settings/settingsStore';
import { openInBrowser } from '../shell/os';
import { Button } from '../ui/Button';
import { EmptyState } from '../ui/EmptyState';
import { Icon } from '../ui/Icon';
import { IconButton } from '../ui/IconButton';
import { LiveBadge } from '../ui/LiveBadge';
import { RefreshLine } from '../ui/ProgressBar';
import { Tabs, type TabItem } from '../ui/Tabs';
import { siteOriginOf, urlDisplay } from '../ui/urlText';
import { HeadersSection } from './HeadersSection';
import { HreflangSection } from './HreflangSection';
import { InspectorContext, type InspectorContextValue } from './inspectorContext';
import { IssuesSection } from './IssuesSection';
import { LinksSection } from './LinksSection';
import { SectionError, SectionSkeleton } from './parts';
import { RedirectsSection } from './RedirectsSection';
import {
  SECTION_IDS,
  SECTION_TITLES,
  rememberSection,
  rememberedSection,
  sectionCounts,
  type SectionId,
} from './sections';
import { SourceSection } from './SourceSection';
import { preloadSourceViewer } from './sourceViewerLoader';
import { StructuredDataSection } from './StructuredDataSection';
import { SummarySection } from './SummarySection';
import { TYPE_WORDS } from './words';

const COPIED_MS = 1_500;
/** The link counts are read once the inspector has stayed on a URL this long: a grid's cursor passing by reads none. */
export const LINK_COUNT_DELAY_MS = 200;
/** A URL longer than this is folded in the head, so it never pushes the sections off the sheet. */
const LONG_URL = 240;

const TYPE_ICONS = { page: FileText, image: ImageIcon } as const;

export interface InspectorProps {
  crawlId: string;
  /** The URL shown, by its id in the crawl. */
  urlId: number;
  onClose: () => void;
}

/** What the sections that read `/urls/inspect` are given: the URL, a skeleton to show, or why it couldn't be read. */
type DetailState =
  | { status: 'ready'; detail: UrlDetail }
  | { status: 'loading' }
  | { status: 'blank' }
  | { status: 'error'; error: unknown; retry: () => void };

/** The value, once it has stayed the same for `delayMs`. */
function useSettled<T>(value: T, delayMs: number): T | undefined {
  const [settled, setSettled] = useState<T | undefined>(undefined);
  useEffect(() => {
    const timer = setTimeout(() => setSettled(value), delayMs);
    return () => clearTimeout(timer);
  }, [value, delayMs]);
  return settled === value ? settled : undefined;
}

/** How many links the Inlinks or Outlinks section lists, for its tab: every kind, as the section shows them. */
function useLinkCount(crawlId: string, urlId: number, direction: 'inlinks' | 'outlinks', enabled: boolean) {
  return useQuery({
    queryKey: [...queryKeys.urlDetail(crawlId, urlId), 'linkCount', direction],
    queryFn: async ({ signal }) => (await api.urlLinks(crawlId, urlId, direction, { limit: 1 }, signal)).total,
    enabled,
    staleTime: Infinity,
  }).data;
}

/**
 * The URL inspector: everything about one URL, in sections. The head names the URL and holds its actions; the
 * sections are tabs, the one chosen remembered for the session, so walking from URL to URL stays on it. While another
 * URL loads, the one shown stays for a moment (150 ms), then the section's skeleton: a quick walk never flashes, and a
 * slow one never shows the wrong URL for long. Sections with their own reads (the links, the source) load and fail on
 * their own.
 */
export function Inspector({ crawlId, urlId, onClose }: InspectorProps) {
  const inspector = useInspector();
  const dock = useSettings((settings) => settings.inspectorDock);
  const item = useQuery(crawlQuery(crawlId)).data;
  const live = useLiveCrawl(crawlId);
  const status = effectiveStatus(item, live);
  const crawling = status !== null && isCrawling(status);
  const siteOrigin = siteOriginOf(item?.startUrl);

  const query = useQuery({
    ...urlDetailQuery(crawlId, urlId),
    // The URL shown before stays while this one loads, for the moment before the skeleton.
    placeholderData: (previous, previousQuery) => (previousQuery?.queryKey[1] === crawlId ? previous : undefined),
  });
  const waiting = query.isPending || query.isPlaceholderData;
  const skeleton = useDelayedLoading(waiting);
  const detail = skeleton ? undefined : query.data;
  /** The URL asked for, once it's the one shown (not the one before it, kept for a moment). */
  const own = detail !== undefined && !query.isPlaceholderData ? detail : undefined;
  const notFound = isProblem(query.error, 'url-not-found') || isProblem(query.error, 'invalid-query');
  const refetch = query.refetch;
  const state = useMemo<DetailState>(() => {
    if (detail) return { status: 'ready', detail };
    if (query.error && !skeleton) return { status: 'error', error: query.error, retry: () => void refetch() };
    return skeleton ? { status: 'loading' } : { status: 'blank' };
  }, [detail, query.error, skeleton, refetch]);

  const [section, setSection] = useState<SectionId>(rememberedSection);
  // "Search the HTML source" asks the source's field to take focus: once, on the URL shown when it was asked. The
  // request goes when the field has taken it, when another section is chosen and when the inspector shows another
  // URL, so a section that mounts later (each URL has its own) never takes focus from where the keyboard is.
  const [searchRequest, setSearchRequest] = useState<{ urlId: number } | null>(null);
  if (searchRequest !== null && searchRequest.urlId !== urlId) setSearchRequest(null);
  const searchFocused = useCallback(() => setSearchRequest(null), []);
  const sheet = useRef<HTMLDivElement>(null);
  const choose = useCallback((next: SectionId) => {
    rememberSection(next);
    setSection(next);
    setSearchRequest(null);
  }, []);

  // An inspector is open, so its source may be asked for: the viewer's code loads while the app is idle.
  useEffect(() => preloadSourceViewer(), []);

  // The chosen tab is in view, in a list that scrolls sideways when the sheet is narrow.
  useEffect(() => {
    const tab = sheet.current?.querySelector('[role="tab"][aria-selected="true"]');
    if (tab && typeof tab.scrollIntoView === 'function') tab.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  }, [section]);

  // Focus that's in a section stays in the inspector when what has it goes: a link walked from, another URL's
  // content (a walk, back or forward), a button that replaces what it sits in ("Open Issues", "Try again", "Show
  // more"). It moves to the shown section's panel, never falls to the page, from where the next arrow key would go to
  // the view behind and change the URL inspected. A part that has a better place for it (a link grid: the grid that
  // comes) takes it itself, before or after.
  useEffect(() => {
    const root = sheet.current;
    if (!root) return undefined;
    /** What has focus in a section, or had it when it went. */
    let held: Element | null = null;
    const inSection = (node: EventTarget | null): node is Element =>
      node instanceof Element && root.contains(node) && node.closest('[role="tabpanel"]') !== null;
    /** Taken out of the page, or hidden with its section. (Every section has a panel; the others are hidden.) */
    const gone = (element: Element) => !element.isConnected || element.closest('[hidden]') !== null;
    const keep = () => {
      if (held === null || !gone(held)) return;
      const focused = document.activeElement;
      if (focused !== null && focused !== document.body) return;
      held = null;
      root.querySelector<HTMLElement>('[role="tabpanel"][data-state="active"]')?.focus({ preventScroll: true });
    };
    const onFocusIn = (event: FocusEvent) => {
      held = inSection(event.target) ? event.target : null;
    };
    const onFocusOut = (event: FocusEvent) => {
      const target = event.target;
      if (target !== held || event.relatedTarget !== null) return;
      // Focus went nowhere. Put down by the user (a click on nothing), it's theirs to put down; taken away with
      // what it was on, it's kept. Chromium says the same for both, before the element has gone: look once it has.
      queueMicrotask(() => {
        if (held === null || target !== held) return;
        if (gone(held)) keep();
        // The window losing focus isn't the user putting it down: it comes back to the same place.
        else if (document.hasFocus()) held = null;
      });
    };
    // Where taking an element away says nothing (no `focusout`), the change to the page does.
    const observer = new MutationObserver(keep);
    observer.observe(root, { childList: true, subtree: true });
    document.addEventListener('focusin', onFocusIn);
    document.addEventListener('focusout', onFocusOut);
    return () => {
      observer.disconnect();
      document.removeEventListener('focusin', onFocusIn);
      document.removeEventListener('focusout', onFocusOut);
    };
  }, []);
  // "Go to URL…" asked for this URL by name: the keyboard goes where it shows, as it stays with a grid's cursor.
  const byName = useGoneToByName(urlId);
  useEffect(() => {
    if (!byName) return;
    arrivedByName();
    sheet.current?.querySelector<HTMLElement>('[role="tabpanel"][data-state="active"]')?.focus({ preventScroll: true });
  }, [byName]);
  const walk = inspector.walk;
  const context = useMemo<InspectorContextValue>(
    () => ({ crawlId, urlId, siteOrigin, walk, crawling }),
    [crawlId, urlId, siteOrigin, walk, crawling],
  );

  const url = own?.url;
  const web = url !== undefined && /^https?:\/\//i.test(url);
  const [copied, setCopied] = useState(false);
  useEffect(() => {
    if (!copied) return undefined;
    const timer = setTimeout(() => setCopied(false), COPIED_MS);
    return () => clearTimeout(timer);
  }, [copied]);
  const copyUrl = useCallback(async () => {
    if (url === undefined) return;
    const done = await copyText(url);
    setCopied(done);
    announce(done ? 'Copied the URL.' : 'The URL couldn’t be copied.');
  }, [url]);
  const openUrl = useCallback(() => (url === undefined ? undefined : void openInBrowser(url)), [url]);
  const toggleDock = useCallback(
    () => updateSettings({ inspectorDock: dock === 'right' ? 'bottom' : 'right' }),
    [dock],
  );
  const dockTitle = dock === 'right' ? 'Dock the inspector at the bottom' : 'Dock the inspector on the right';

  useCommands(
    () => [
      {
        id: 'inspector.openInBrowser',
        title: 'Open this URL in the browser',
        group: 'Inspector',
        enabled: () => web,
        run: openUrl,
      },
      {
        id: 'inspector.copyUrl',
        title: 'Copy this URL',
        group: 'Inspector',
        keywords: ['clipboard'],
        enabled: () => url !== undefined,
        run: copyUrl,
      },
      { id: 'inspector.dock', title: dockTitle, group: 'Inspector', keywords: ['panel', 'side'], run: toggleDock },
      ...SECTION_IDS.map<Command>((id) => ({
        id: `inspector.section.${id}`,
        title: `Inspector: show ${SECTION_TITLES[id]}`,
        group: 'Inspector',
        enabled: () => !notFound && section !== id,
        run: () => choose(id),
      })),
      {
        id: 'inspector.searchSource',
        title: 'Search the HTML source',
        group: 'Inspector',
        keywords: ['find', 'html'],
        enabled: () => !notFound,
        run: () => {
          choose('source');
          setSearchRequest({ urlId });
        },
      },
    ],
    [web, url, openUrl, copyUrl, dockTitle, toggleDock, section, choose, notFound, urlId],
    'app',
  );

  // Counted once the inspector settles on the URL, and only for a URL the crawl has.
  const settled = useSettled(urlId, LINK_COUNT_DELAY_MS) === urlId && !notFound;
  const inlinks = useLinkCount(crawlId, urlId, 'inlinks', settled);
  const outlinks = useLinkCount(crawlId, urlId, 'outlinks', settled);

  const tabs = useMemo<TabItem<SectionId>[]>(() => {
    const counts: Partial<Record<SectionId, number>> = state.status === 'ready' ? sectionCounts(state.detail) : {};
    if (inlinks !== undefined && inlinks > 0) counts.inlinks = inlinks;
    else delete counts.inlinks;
    if (outlinks !== undefined && outlinks > 0) counts.outlinks = outlinks;
    else delete counts.outlinks;
    const fromDetail = (id: SectionId, render: (detail: UrlDetail) => ReactNode): ReactNode => {
      if (state.status === 'ready') {
        return (
          <RegionBoundary key={state.detail.id} name={`inspector’s ${SECTION_TITLES[id].toLowerCase()}`}>
            {render(state.detail)}
          </RegionBoundary>
        );
      }
      if (state.status === 'error') return <SectionError error={state.error} onRetry={state.retry} what="This URL" />;
      if (state.status === 'blank') return null;
      return id === 'summary' ? <SummarySkeleton /> : <SectionSkeleton label={`Loading ${SECTION_TITLES[id]}`} />;
    };
    const content: Record<SectionId, ReactNode> = {
      summary: fromDetail('summary', (value) => <SummarySection detail={value} onShowSection={choose} />),
      issues: fromDetail('issues', (value) => <IssuesSection detail={value} />),
      inlinks: (
        <RegionBoundary name="inspector’s inlinks">
          <LinksSection direction="inlinks" />
        </RegionBoundary>
      ),
      outlinks: (
        <RegionBoundary name="inspector’s outlinks">
          <LinksSection direction="outlinks" />
        </RegionBoundary>
      ),
      redirects: fromDetail('redirects', (value) => <RedirectsSection detail={value} />),
      headers: fromDetail('headers', (value) => <HeadersSection detail={value} />),
      structuredData: fromDetail('structuredData', (value) => <StructuredDataSection detail={value} />),
      hreflang: fromDetail('hreflang', (value) => <HreflangSection detail={value} />),
      source: (
        <RegionBoundary key={urlId} name="inspector’s source">
          <SourceSection focusSearch={searchRequest?.urlId === urlId} onSearchFocused={searchFocused} />
        </RegionBoundary>
      ),
    };
    return SECTION_IDS.map((id) => ({ value: id, label: SECTION_TITLES[id], count: counts[id], content: content[id] }));
  }, [state, inlinks, outlinks, choose, urlId, searchRequest, searchFocused]);

  const refreshing = query.isFetching && !waiting && !crawling;
  const compact = dock === 'bottom';
  return (
    <InspectorContext value={context}>
      <div ref={sheet} className="relative flex min-h-0 flex-1 flex-col" aria-busy={waiting || refreshing}>
        {refreshing ? <RefreshLine label="Reading this URL again" /> : null}
        {/* Docked at the bottom the sheet is short: what the URL is, the URL and the actions share one line. */}
        <div className={clsx('flex flex-wrap items-center gap-x-3 px-5', compact ? 'pt-2 pb-1' : 'pt-3 pb-2')}>
          <Kind detail={own} loading={state.status === 'loading'} crawling={crawling} compact={compact} />
          <div className={clsx('ml-auto flex shrink-0 gap-0.5', compact && 'order-3')}>
            <IconButton label="Open in the browser" icon={ExternalLink} size="sm" disabled={!web} onClick={openUrl} />
            <IconButton
              label="Copy the URL"
              tooltip={copied ? 'Copied' : 'Copy the URL'}
              icon={copied ? Check : Copy}
              size="sm"
              disabled={url === undefined}
              onClick={() => void copyUrl()}
            />
            <IconButton
              label={dock === 'right' ? 'Dock at the bottom' : 'Dock on the right'}
              icon={dock === 'right' ? PanelBottom : PanelRight}
              size="sm"
              onClick={toggleDock}
            />
            <IconButton
              label="Close the inspector"
              tooltip="Close"
              icon={X}
              size="sm"
              command="inspector.close"
              onClick={onClose}
            />
          </div>
          <Heading url={detail?.url} loading={state.status === 'loading'} notFound={notFound} compact={compact} />
        </div>
        {notFound ? (
          <div className="px-5 py-4">
            <EmptyState
              title="This crawl doesn’t have that URL"
              icon={FileQuestion}
              level="none"
              actions={
                <Button size="sm" onClick={onClose}>
                  Close the inspector
                </Button>
              }
            >
              The link to it may be from another crawl, or from before this one was crawled again.
            </EmptyState>
          </div>
        ) : (
          <Tabs
            label="About this URL"
            tabs={tabs}
            value={section}
            onValueChange={choose}
            className="flex min-h-0 flex-1 flex-col"
            panelClassName="flex min-h-0 flex-1 flex-col"
          />
        )}
      </div>
    </InspectorContext>
  );
}

/** "Page, depth 2": what the URL is, and how far from the start. */
function Kind({
  detail,
  loading,
  crawling,
  compact,
}: {
  detail: UrlDetail | undefined;
  loading: boolean;
  crawling: boolean;
  compact: boolean;
}) {
  const room = compact ? 'flex-none' : 'min-h-(--control-height-sm) min-w-0 flex-1';
  if (!detail) {
    return (
      <span className={clsx('flex items-center gap-2 text-12 text-text-muted', room)}>
        {loading ? <Skeleton className={compact ? 'h-(--skeleton-text) w-16' : 'h-(--skeleton-text) w-1/3'} /> : null}
        {crawling ? <LiveBadge /> : null}
      </span>
    );
  }
  const type = detail.type;
  const icon = type === 'page' || type === 'image' ? TYPE_ICONS[type] : File;
  const depth = detail.discovery.depth;
  return (
    <span className={clsx('flex items-center gap-1.5 text-12 text-text-muted', room)}>
      <Icon icon={icon} size="sm" className="shrink-0" />
      <span className="truncate">
        {TYPE_WORDS[type]}
        {detail.isInternal ? '' : ' on another site'}
        {depth === null ? '' : `, depth ${depth}`}
      </span>
      {crawling ? <LiveBadge className="ml-1 shrink-0" /> : null}
    </span>
  );
}

/** The URL, in full: the inspector is about this one URL, so nothing of it is left out. A very long one folds. */
function Heading({
  url,
  loading,
  notFound,
  compact,
}: {
  url: string | undefined;
  loading: boolean;
  notFound: boolean;
  /** On one line beside the actions (docked at the bottom): cut at its end, in full in its tooltip and by Copy. */
  compact: boolean;
}) {
  const [open, setOpen] = useState(false);
  const place = compact ? 'order-2 min-w-0 flex-1' : 'mt-2 w-full';
  if (url === undefined) {
    return (
      <h2 className={clsx('min-h-(--leading-14) text-13', place)}>
        {loading ? (
          <>
            <span className="sr-only">Loading the URL</span>
            <Skeleton className="mt-1 h-(--skeleton-text) w-4/5" />
          </>
        ) : notFound ? (
          <span className="text-text-secondary">Not in this crawl</span>
        ) : (
          <span className="sr-only">Inspector</span>
        )}
      </h2>
    );
  }
  const display = urlDisplay(url);
  const long = !compact && url.length > LONG_URL;
  return (
    <div className={place}>
      <h2
        title={compact ? url : undefined}
        className={clsx(
          'font-mono text-13 wrap-anywhere text-text',
          compact ? 'line-clamp-1' : long && !open && 'line-clamp-3',
        )}
      >
        {display.origin ? <span className="text-text-muted">{display.origin}</span> : null}
        {url.slice(display.origin.length)}
      </h2>
      {long ? (
        <button
          type="button"
          className="si-link mt-1 text-12"
          aria-expanded={open}
          onClick={() => setOpen((value) => !value)}
        >
          {open ? 'Show less of the URL' : 'Show all of the URL'}
        </button>
      ) : null}
    </div>
  );
}

/** The summary's own skeleton: its three figures, the verdict, and the title and description with their scales. */
function SummarySkeleton() {
  return (
    <SectionSkeleton label="Loading the summary">
      <div className="grid grid-cols-3 gap-3">
        <Skeleton className="h-16" />
        <Skeleton className="h-16" />
        <Skeleton className="h-16" />
      </div>
      <div className="flex items-center gap-3">
        <Skeleton className="h-(--icon-box) w-(--icon-box) rounded-full" />
        <div className="flex flex-1 flex-col gap-2">
          <Skeleton className="h-(--skeleton-text) w-1/4" />
          <Skeleton className="h-(--skeleton-text) w-2/3" />
        </div>
      </div>
      {[0, 1].map((index) => (
        <div key={index} className="flex flex-col gap-2 pt-2">
          <Skeleton className="h-(--skeleton-text) w-1/6" />
          <Skeleton className="h-(--skeleton-text) w-5/6" />
          <Skeleton className="h-(--skeleton-text) w-full" />
        </div>
      ))}
      <Skeleton className="h-(--skeleton-text) w-1/2" />
      <Skeleton className="h-(--skeleton-text) w-2/5" />
    </SectionSkeleton>
  );
}
