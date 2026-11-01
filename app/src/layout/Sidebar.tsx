import { useQuery } from '@tanstack/react-query';
import { Link, useParams } from '@tanstack/react-router';
import clsx from 'clsx';
import { crawlQuery, libraryQuery } from '../api/queries';
import { CRAWL_SECTIONS } from '../app/sections';
import logoUrl from '../assets/iguana-64.png';
import { crawlName } from '../crawl/status';
import { useShortcut } from '../keyboard/registry';
import { formatCount } from '../lib/format';
import { useLiveCrawl, useLiveIds } from '../live/liveStore';
import { updateSettings, useSettings } from '../settings/settingsStore';

const ITEM =
  'flex h-(--nav-item-height) items-center gap-2 rounded-md px-2.5 text-13 text-text-secondary hover:bg-surface-hover hover:text-text';
const ACTIVE = 'bg-surface-selected text-accent-text font-medium';

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
        'flex shrink-0 flex-col gap-4 border-r border-border bg-surface-sunken p-2 transition-[width] duration-(--duration-panel) ease-out',
        collapsed ? 'w-(--sidebar-width-collapsed)' : 'w-(--sidebar-width)',
      )}
    >
      <div className="flex h-(--topbar-height) items-center gap-2 px-1">
        <img src={logoUrl} alt="" className="size-7 shrink-0" />
        {collapsed ? null : <span className="text-14 font-semibold tracking-snug">Shouting Iguana</span>}
        <button
          type="button"
          onClick={() => updateSettings({ sidebarCollapsed: !collapsed })}
          aria-label={collapsed ? 'Expand the sidebar' : 'Collapse the sidebar'}
          aria-expanded={!collapsed}
          className="ml-auto rounded-sm px-1.5 text-text-muted hover:bg-surface-hover"
        >
          {collapsed ? '»' : '«'}
        </button>
      </div>

      <ul className="flex flex-col gap-0.5">
        <li>
          <Link
            to="/"
            search={{}}
            className={ITEM}
            activeProps={{ className: ACTIVE }}
            activeOptions={{ exact: true }}
            title="Home"
          >
            <span aria-hidden="true">⌂</span>
            {collapsed ? <span className="sr-only">Home</span> : 'Home'}
          </Link>
        </li>
      </ul>

      {crawlId ? (
        <section aria-label={crawlName(crawl)} className="flex flex-col gap-0.5">
          {collapsed ? null : (
            <h2
              className="truncate px-2.5 pb-1 text-12 font-medium text-text-muted"
              title={crawl?.startUrl ?? undefined}
            >
              {crawlName(crawl)}
            </h2>
          )}
          <ul className="flex flex-col gap-0.5">
            {CRAWL_SECTIONS.map((section) => (
              <SectionLink
                key={section.id}
                crawlId={crawlId}
                section={section}
                collapsed={collapsed}
                count={section.id === 'issues' ? issues : null}
              />
            ))}
          </ul>
        </section>
      ) : null}

      <div className="mt-auto flex flex-col gap-2">
        <RunningCrawls collapsed={collapsed} />
        <Link
          to="/settings/{-$section}"
          params={{}}
          className={ITEM}
          activeProps={{ className: ACTIVE }}
          title="Settings"
        >
          <span aria-hidden="true">⚙</span>
          {collapsed ? <span className="sr-only">Settings</span> : 'Settings'}
        </Link>
      </div>
    </nav>
  );
}

function SectionLink({
  crawlId,
  section,
  collapsed,
  count,
}: {
  crawlId: string;
  section: (typeof CRAWL_SECTIONS)[number];
  collapsed: boolean;
  count: number | null;
}) {
  const shortcut = useShortcut(`section.${section.id}`);
  const title = shortcut ? `${section.title} (${shortcut})` : section.title;
  const content = (
    <>
      <span className={collapsed ? 'sr-only' : 'truncate'}>{section.title}</span>
      {count !== null && !collapsed ? (
        <span className="figures ml-auto text-12 text-text-muted">{formatCount(count)}</span>
      ) : null}
    </>
  );
  const props = { className: ITEM, activeProps: { className: ACTIVE }, title };
  switch (section.id) {
    case 'issues':
      return (
        <li>
          <Link to="/crawls/$crawl/issues/{-$check}" params={{ crawl: crawlId }} {...props}>
            {content}
          </Link>
        </li>
      );
    case 'urls':
      return (
        <li>
          <Link to="/crawls/$crawl/urls" params={{ crawl: crawlId }} {...props}>
            {content}
          </Link>
        </li>
      );
    case 'links':
      return (
        <li>
          <Link to="/crawls/$crawl/links" params={{ crawl: crawlId }} {...props}>
            {content}
          </Link>
        </li>
      );
    case 'sitemaps':
      return (
        <li>
          <Link to="/crawls/$crawl/sitemaps/{-$sitemap}" params={{ crawl: crawlId }} {...props}>
            {content}
          </Link>
        </li>
      );
    case 'robots':
      return (
        <li>
          <Link to="/crawls/$crawl/robots/{-$origin}" params={{ crawl: crawlId }} {...props}>
            {content}
          </Link>
        </li>
      );
    case 'settings':
      return (
        <li>
          <Link to="/crawls/$crawl/settings" params={{ crawl: crawlId }} {...props}>
            {content}
          </Link>
        </li>
      );
    default:
      return (
        <li>
          <Link to="/crawls/$crawl/overview" params={{ crawl: crawlId }} {...props}>
            {content}
          </Link>
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
    <section aria-label="Running crawls" className="flex flex-col gap-0.5 border-t border-border-subtle pt-2">
      {collapsed ? null : (
        <h2 className="px-2.5 text-12 font-medium text-text-muted">
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
      <Link to="/crawls/$crawl/overview" params={{ crawl: id }} className={ITEM} title={name}>
        <span aria-hidden="true" className="size-(--mark-size) shrink-0 rounded-full bg-accent" />
        <span className={collapsed ? 'sr-only' : 'truncate'}>{name}</span>
        {collapsed || fetched === null ? null : (
          <span className="figures ml-auto text-12 text-text-muted">{formatCount(fetched)}</span>
        )}
      </Link>
    </li>
  );
}
