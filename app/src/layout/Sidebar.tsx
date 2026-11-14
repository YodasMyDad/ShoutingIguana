import { useQuery } from '@tanstack/react-query';
import { useParams } from '@tanstack/react-router';
import clsx from 'clsx';
import {
  Bot,
  FileText,
  House,
  LayoutDashboard,
  Link as LinkIcon,
  List,
  Network,
  PanelLeft,
  Settings,
  SlidersHorizontal,
  TriangleAlert,
  type LucideIcon,
} from 'lucide-react';
import { useMemo } from 'react';
import { analysisQuery, checksQuery, crawlQuery, libraryQuery } from '../api/queries';
import { isCrawlStatus, type IssueCounts } from '../api/types';
import { CRAWL_SECTIONS, REPORT_SECTION, type CrawlSection, type CrawlSectionEntry } from '../app/sections';
import { SIMPLE_SECTIONS } from '../app/modeRoutes';
import { SIMPLE_SCREENS } from '../app/simpleScreens';
import logoUrl from '../assets/iguana-64.png';
import { runningTitle } from '../crawl/runningWords';
import { crawlName, crawledUrls } from '../crawl/status';
import { ExportsInProgress } from '../export/ExportsInProgress';
import { formatExactTime } from '../lib/format';
import { useLive, useLiveCrawl, useLiveIds } from '../live/liveStore';
import { useMode } from '../mode/mode';
import { useSettings } from '../settings/settingsStore';
import { CrawlFavicon } from '../crawl/CrawlFavicon';
import { IconButton } from '../ui/IconButton';
import { LiveDot } from '../ui/LiveBadge';
import { NavItem } from '../ui/NavItem';
import { SeverityMark } from '../ui/SeverityMark';
import { toggleSidebar, useSidebarCollapsed } from './sidebarState';
import { SEVERITIES, type Severity } from '../ui/severity';
import { checkEntries, foundBySeverity } from '../screens/issues/checkList';

/** Each section's icon, as the design draws the sidebar. */
const SECTION_ICONS: Record<CrawlSection, LucideIcon> = {
  overview: LayoutDashboard,
  issues: TriangleAlert,
  urls: List,
  links: LinkIcon,
  sitemaps: Network,
  robots: Bot,
  settings: SlidersHorizontal,
  report: FileText,
};

/**
 * The sidebar (docs/v2-ui-ia.md, The shell; docs/v2-ui-simple.md, The shell in Simple): Home, the open crawl's
 * sections, crawls running now, Settings. In Simple the crawl's sections are three: Summary, What to fix (counting the
 * problems that are Serious or Worth fixing) and Report; in Advanced, the seven as built, and Report after a rule. The
 * Report is listed once its screen exists (app/simpleScreens.ts).
 */
export function Sidebar() {
  const collapsed = useSidebarCollapsed();
  const mode = useMode();
  const { crawl: crawlId } = useParams({ strict: false });
  const crawl = useQuery({ ...crawlQuery(crawlId ?? ''), enabled: crawlId !== undefined }).data;
  const counts = useIssueCounts(crawlId, crawl?.issues != null);
  const issues = counts
    ? mode === 'simple'
      ? counts.error + counts.warning
      : counts.error + counts.warning + counts.notice
    : null;
  const report = SIMPLE_SCREENS.report !== null;
  const sections =
    mode === 'simple'
      ? [...CRAWL_SECTIONS, REPORT_SECTION].filter(
          (section) => SIMPLE_SECTIONS.includes(section.id) && (section.id !== 'report' || report),
        )
      : CRAWL_SECTIONS;

  return (
    <nav
      aria-label="Main"
      className={clsx(
        'flex shrink-0 flex-col gap-5 border-r border-border-subtle bg-canvas p-3 transition-width duration-(--duration-panel) ease-out',
        collapsed ? 'w-(--sidebar-width-collapsed) px-2' : 'w-(--sidebar-width)',
      )}
    >
      <div className={clsx('flex h-(--control-height) items-center gap-2.5', collapsed ? 'flex-col' : 'pl-1')}>
        {collapsed ? null : (
          <>
            <img src={logoUrl} alt="" className="size-(--favicon-lg) shrink-0" />
            <span className="flex-1 truncate text-14 font-semibold tracking-snug">Shouting Iguana</span>
          </>
        )}
        <IconButton
          label={collapsed ? 'Expand the sidebar' : 'Collapse the sidebar'}
          icon={PanelLeft}
          size="sm"
          command="view.sidebar"
          aria-expanded={!collapsed}
          onClick={toggleSidebar}
        />
      </div>

      <ul className="flex flex-col gap-0.5">
        <li>
          <NavItem to="/" search={{}} activeOptions={{ exact: true }} label="Home" icon={House} collapsed={collapsed} />
        </li>
      </ul>

      {crawlId ? (
        <section aria-label={crawlName(crawl)} className="flex flex-col gap-1.5">
          <div className={clsx('flex items-center gap-2.5 py-1', collapsed ? 'justify-center' : 'px-2')}>
            <CrawlFavicon item={crawl} size="lg" />
            {collapsed ? null : (
              <div className="flex min-w-0 flex-col">
                <h2 className="truncate text-13 font-semibold" title={crawl?.startUrl ?? undefined}>
                  {crawlName(crawl)}
                </h2>
                {crawl?.startedAt ? (
                  <span className="truncate text-12 text-text-muted">{formatExactTime(crawl.startedAt)}</span>
                ) : null}
              </div>
            )}
          </div>
          <ul className="flex flex-col gap-0.5">
            {sections.map((section) => (
              <SectionLink
                key={section.id}
                crawlId={crawlId}
                section={section}
                title={(mode === 'simple' ? section.simpleTitle : undefined) ?? section.title}
                collapsed={collapsed}
                count={section.id === 'issues' ? issues : null}
                // Simple counts problems, not severities: no Errors, Warnings and Notices under What to fix.
                issueCounts={section.id === 'issues' && mode === 'advanced' ? counts : null}
              />
            ))}
          </ul>
          {mode === 'advanced' && report ? (
            <ul className="flex flex-col gap-0.5 border-t border-border-subtle pt-1.5">
              <SectionLink
                crawlId={crawlId}
                section={REPORT_SECTION}
                title="Report"
                collapsed={collapsed}
                count={null}
              />
            </ul>
          ) : null}
        </section>
      ) : null}

      <div className="mt-auto flex flex-col gap-2">
        <RunningCrawls collapsed={collapsed} simple={mode === 'simple'} />
        <ExportsInProgress collapsed={collapsed} />
        <div className="border-t border-border-subtle pt-2">
          <NavItem to="/settings/{-$section}" params={{}} label="Settings" icon={Settings} collapsed={collapsed} />
        </div>
      </div>
    </nav>
  );
}

/**
 * The open crawl's checks with issues, by severity, counted from the same check entries as the Issues list (the
 * catalog's checks, as its last analysis found them, the hidden ones left out), so the two never disagree. Null until
 * both are read, and for a crawl never analysed.
 */
function useIssueCounts(crawlId: string | undefined, analysed: boolean): IssueCounts | null {
  const checks = useQuery({ ...checksQuery(), enabled: analysed }).data;
  const analysis = useQuery({ ...analysisQuery(crawlId ?? ''), enabled: crawlId !== undefined && analysed }).data;
  const hiddenIds = useSettings((settings) => settings.hiddenChecks);
  return useMemo(() => {
    if (!analysed || !checks || !analysis) return null;
    const { error, warning, notice } = foundBySeverity(checkEntries(checks, analysis, new Set(hiddenIds)));
    return { error, warning, notice };
  }, [analysed, checks, analysis, hiddenIds]);
}

/** Each severity's words under Issues: "Errors 2". */
const SEVERITY_LABELS: Record<Severity, string> = { error: 'Errors', warning: 'Warnings', notice: 'Notices' };

function SectionLink({
  crawlId,
  section,
  title,
  collapsed,
  count,
  issueCounts = null,
}: {
  crawlId: string;
  section: CrawlSectionEntry;
  /** Its name in the mode shown. */
  title: string;
  collapsed: boolean;
  count: number | null;
  /** Checks with issues, by severity: each opens the list filtered to it. */
  issueCounts?: IssueCounts | null;
}) {
  const props = {
    label: title,
    icon: SECTION_ICONS[section.id],
    collapsed,
    count,
    command: `section.${section.id}`,
  };
  switch (section.id) {
    case 'issues':
      return (
        <li>
          <NavItem
            to="/crawls/$crawl/issues/{-$check}"
            params={{ crawl: crawlId }}
            activeOptions={{ includeSearch: false }}
            {...props}
          />
          {issueCounts && !collapsed ? (
            <ul aria-label="Issues by severity" className="flex flex-col gap-0.5 pt-0.5">
              {SEVERITIES.map((severity) => (
                <li key={severity}>
                  <NavItem
                    to="/crawls/$crawl/issues/{-$check}"
                    params={{ crawl: crawlId }}
                    search={{ severity: [severity] }}
                    activeOptions={{ includeSearch: true, exact: false }}
                    activeProps={{ 'aria-current': undefined }}
                    label={SEVERITY_LABELS[severity]}
                    icon={<SeverityMark severity={severity} decorative />}
                    count={issueCounts[severity]}
                    className="pl-8"
                  />
                </li>
              ))}
            </ul>
          ) : null}
        </li>
      );
    case 'urls':
      return (
        <li>
          <NavItem to="/crawls/$crawl/urls" params={{ crawl: crawlId }} {...props} />
        </li>
      );
    case 'links':
      return (
        <li>
          <NavItem to="/crawls/$crawl/links" params={{ crawl: crawlId }} {...props} />
        </li>
      );
    case 'sitemaps':
      return (
        <li>
          {/* The list, from one sitemap's page too: left out, the router keeps the sitemap the page is on. */}
          <NavItem
            to="/crawls/$crawl/sitemaps/{-$sitemap}"
            params={{ crawl: crawlId, sitemap: undefined }}
            {...props}
          />
        </li>
      );
    case 'robots':
      return (
        <li>
          <NavItem to="/crawls/$crawl/robots/{-$origin}" params={{ crawl: crawlId, origin: undefined }} {...props} />
        </li>
      );
    case 'settings':
      return (
        <li>
          <NavItem to="/crawls/$crawl/settings" params={{ crawl: crawlId }} {...props} />
        </li>
      );
    case 'report':
      return (
        <li>
          <NavItem to="/crawls/$crawl/report" params={{ crawl: crawlId }} {...props} />
        </li>
      );
    default:
      return (
        <li>
          <NavItem to="/crawls/$crawl/overview" params={{ crawl: crawlId }} {...props} />
        </li>
      );
  }
}

/** Crawls running in the background: each a link with its progress. Simple calls them "Checking now". */
function RunningCrawls({ collapsed, simple }: { collapsed: boolean; simple: boolean }) {
  const live = useLiveIds();
  const library = useQuery(libraryQuery()).data;
  // What each does now, read as one string so the store's answer is stable between changes.
  const doing = useLive((state) => live.map((id) => state.crawls[id]?.status ?? '').join(' '))
    .split(' ')
    .map((status) => (isCrawlStatus(status) ? status : null));
  if (live.length === 0) return null;
  return (
    <section aria-label={simple ? 'Checking now' : 'Running crawls'} className="flex flex-col gap-0.5">
      {collapsed ? null : (
        <h2 className="px-2.5 pb-1 text-12 font-medium text-text-muted">
          {runningTitle(doing, simple ? 'simple' : 'advanced')}
        </h2>
      )}
      <ul className="flex flex-col gap-0.5">
        {live.map((id) => (
          <RunningCrawl
            key={id}
            id={id}
            name={crawlName(library?.find((item) => item.id === id))}
            collapsed={collapsed}
          />
        ))}
      </ul>
    </section>
  );
}

function RunningCrawl({ id, name, collapsed }: { id: string; name: string; collapsed: boolean }) {
  const crawl = useLiveCrawl(id);
  const fetched = crawledUrls(undefined, crawl?.progress);
  return (
    <li>
      <NavItem
        to="/crawls/$crawl/overview"
        params={{ crawl: id }}
        label={name}
        icon={<LiveDot />}
        count={fetched}
        collapsed={collapsed}
        // On the crawl's Overview, its own Overview item is the current place; this link only leads there.
        markCurrent={false}
      />
    </li>
  );
}
