import { useQuery } from '@tanstack/react-query';
import { useParams } from '@tanstack/react-router';
import clsx from 'clsx';
import {
  Bot,
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
import { crawlQuery, libraryQuery } from '../api/queries';
import type { IssueCounts } from '../api/types';
import { CRAWL_SECTIONS, type CrawlSection } from '../app/sections';
import logoUrl from '../assets/iguana-64.png';
import { crawlName } from '../crawl/status';
import { formatExactTime } from '../lib/format';
import { useLiveCrawl, useLiveIds } from '../live/liveStore';
import { updateSettings, useSettings } from '../settings/settingsStore';
import { Favicon } from '../ui/Favicon';
import { IconButton } from '../ui/IconButton';
import { LiveDot } from '../ui/LiveBadge';
import { NavItem } from '../ui/NavItem';
import { SeverityMark } from '../ui/SeverityMark';
import { SEVERITIES, type Severity } from '../ui/severity';

/** Each section's icon, as the design draws the sidebar. */
const SECTION_ICONS: Record<CrawlSection, LucideIcon> = {
  overview: LayoutDashboard,
  issues: TriangleAlert,
  urls: List,
  links: LinkIcon,
  sitemaps: Network,
  robots: Bot,
  settings: SlidersHorizontal,
};

/** The sidebar (docs/v2-ui-ia.md, The shell): Home, the open crawl's sections, crawls running now, Settings. */
export function Sidebar() {
  const collapsed = useSettings((settings) => settings.sidebarCollapsed);
  const { crawl: crawlId } = useParams({ strict: false });
  const crawl = useQuery({ ...crawlQuery(crawlId ?? ''), enabled: crawlId !== undefined }).data;
  const issues = crawl?.issues ? crawl.issues.error + crawl.issues.warning + crawl.issues.notice : null;

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
          onClick={() => updateSettings({ sidebarCollapsed: !collapsed })}
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
            <Favicon site={crawl?.site ?? crawlName(crawl)} size="lg" />
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
            {CRAWL_SECTIONS.map((section) => (
              <SectionLink
                key={section.id}
                crawlId={crawlId}
                section={section}
                collapsed={collapsed}
                count={section.id === 'issues' ? issues : null}
                issueCounts={section.id === 'issues' ? (crawl?.issues ?? null) : null}
              />
            ))}
          </ul>
        </section>
      ) : null}

      <div className="mt-auto flex flex-col gap-2">
        <RunningCrawls collapsed={collapsed} />
        <div className="border-t border-border-subtle pt-2">
          <NavItem to="/settings/{-$section}" params={{}} label="Settings" icon={Settings} collapsed={collapsed} />
        </div>
      </div>
    </nav>
  );
}

/** Each severity's words under Issues: "Errors 2". */
const SEVERITY_LABELS: Record<Severity, string> = { error: 'Errors', warning: 'Warnings', notice: 'Notices' };

function SectionLink({
  crawlId,
  section,
  collapsed,
  count,
  issueCounts = null,
}: {
  crawlId: string;
  section: (typeof CRAWL_SECTIONS)[number];
  collapsed: boolean;
  count: number | null;
  /** Checks with issues, by severity: each opens the list filtered to it. */
  issueCounts?: IssueCounts | null;
}) {
  const props = {
    label: section.title,
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
          <NavItem to="/crawls/$crawl/sitemaps/{-$sitemap}" params={{ crawl: crawlId }} {...props} />
        </li>
      );
    case 'robots':
      return (
        <li>
          <NavItem to="/crawls/$crawl/robots/{-$origin}" params={{ crawl: crawlId }} {...props} />
        </li>
      );
    case 'settings':
      return (
        <li>
          <NavItem to="/crawls/$crawl/settings" params={{ crawl: crawlId }} {...props} />
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

/** Crawls running in the background: each a link with its progress. */
function RunningCrawls({ collapsed }: { collapsed: boolean }) {
  const live = useLiveIds();
  const library = useQuery(libraryQuery()).data;
  if (live.length === 0) return null;
  return (
    <section aria-label="Running crawls" className="flex flex-col gap-0.5">
      {collapsed ? null : (
        <h2 className="px-2.5 pb-1 text-12 font-medium text-text-muted">
          {live.length === 1 ? '1 crawling' : `${live.length} crawling`}
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
  const fetched = crawl?.progress?.fetched ?? null;
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
