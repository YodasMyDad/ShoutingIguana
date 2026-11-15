import { useQuery } from '@tanstack/react-query';
import { useLocation, useNavigate } from '@tanstack/react-router';
import clsx from 'clsx';
import { ChevronsUpDown, House } from 'lucide-react';
import type { ButtonHTMLAttributes, Ref } from 'react';
import { libraryQuery } from '../api/queries';
import type { CrawlListItem } from '../api/types';
import { goToSection } from '../app/goToSection';
import { sectionOf } from '../app/sections';
import { CrawlFavicon } from '../crawl/CrawlFavicon';
import { crawlName } from '../crawl/status';
import { formatExactTime } from '../lib/format';
import type { Mode } from '../mode/mode';
import { Icon } from '../ui/Icon';
import { DropdownMenu, MenuItem, MenuLabel, MenuRadioGroup, MenuRadioItem, MenuSeparator } from '../ui/Menu';
import { Tooltip } from '../ui/Tooltip';
import { recentCrawls } from './recentCrawls';

/** The switcher's words in each mode: Simple calls a crawl a site check, and the library Your sites. */
const WORDS: Record<Mode, { switchTo: string; recent: string; all: string }> = {
  advanced: { switchTo: 'Switch crawl', recent: 'Recent crawls', all: 'All crawls, on Home' },
  simple: { switchTo: 'Switch site', recent: 'Recent checks', all: 'All your sites, on Home' },
};

type TriggerProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  ref?: Ref<HTMLButtonElement> | undefined;
  crawl: CrawlListItem | undefined;
  name: string;
  date: string | null;
  label: string;
  /** "Switch crawl", read before the crawl's name. */
  switchTo: string;
  collapsed: boolean;
};

/** The open crawl, as a bordered button: its icon, name and date, and the up and down chevron that says it opens. */
function SwitcherButton({ ref, crawl, name, date, label, switchTo, collapsed, className, ...rest }: TriggerProps) {
  return (
    // Collapsed, it's the site's icon alone: its tooltip says what it is.
    <Tooltip content={collapsed ? label : undefined} side="right" triggerName={label}>
      <button
        ref={ref}
        type="button"
        {...rest}
        // Collapsed, nothing shows but the icon: the label names it. Open, its words name it, with what's read but not
        // shown around them, so the name holds what's seen (WCAG 2.5.3).
        aria-label={collapsed ? label : undefined}
        className={clsx('si-switcher', collapsed && 'si-switcher--collapsed', className)}
      >
        <CrawlFavicon item={crawl} size="lg" />
        {collapsed ? null : (
          <>
            <span className="si-switcher__text">
              <span className="si-switcher__name" title={crawl?.startUrl ?? undefined}>
                <span className="sr-only">{switchTo}: </span>
                {name}
                {date ? <span className="sr-only">, </span> : null}
              </span>
              {date ? <span className="si-switcher__date">{date}</span> : null}
            </span>
            <Icon icon={ChevronsUpDown} size="sm" className="si-switcher__chevron" />
          </>
        )}
      </button>
    </Tooltip>
  );
}

/**
 * The sidebar's open crawl, as a switcher (the framed workspace, docs/v2-ui-ia.md, The shell): a menu of the library's
 * recent crawls, the open one ticked, each opening on the section you're on; then Home, for the whole library. A
 * Radix menu as the app's others: Enter, Space or the down arrow opens it, the arrows and typing move, Esc closes it
 * and gives focus back to the button. Each crawl is a radio item, so a screen reader hears which one is open.
 */
export function CrawlSwitcher({
  crawlId,
  crawl,
  collapsed,
  mode,
}: {
  crawlId: string;
  crawl: CrawlListItem | undefined;
  collapsed: boolean;
  mode: Mode;
}) {
  const navigate = useNavigate();
  const pathname = useLocation({ select: (location) => location.pathname });
  const library = useQuery(libraryQuery()).data;
  const words = WORDS[mode];
  const name = crawlName(crawl);
  const date = crawl?.startedAt ? formatExactTime(crawl.startedAt) : null;
  const label = `${words.switchTo}: ${name}${date ? `, ${date}` : ''}`;
  const recent = recentCrawls(library ?? (crawl ? [crawl] : []), crawlId);

  const open = (id: string) => {
    if (id === crawlId) return;
    void goToSection(navigate, sectionOf(pathname) ?? 'overview', id);
  };

  return (
    <DropdownMenu
      align="start"
      side={collapsed ? 'right' : 'bottom'}
      className="si-switcher-menu"
      trigger={
        <SwitcherButton
          crawl={crawl}
          name={name}
          date={date}
          label={label}
          switchTo={words.switchTo}
          collapsed={collapsed}
        />
      }
    >
      <MenuLabel>{words.recent}</MenuLabel>
      <MenuRadioGroup value={crawlId} onValueChange={open}>
        {recent.map((item) => (
          <MenuRadioItem key={item.id} value={item.id}>
            {/* The site, then when it was crawled: two lines, so a long name keeps its room. */}
            <span className="flex min-w-0 items-center gap-2.5">
              <CrawlFavicon item={item} size="md" />
              <span className="flex min-w-0 flex-col">
                <span className="truncate">{crawlName(item)}</span>
                {item.startedAt ? (
                  <span className="si-switcher-menu__date truncate">{formatExactTime(item.startedAt)}</span>
                ) : null}
              </span>
            </span>
          </MenuRadioItem>
        ))}
      </MenuRadioGroup>
      <MenuSeparator />
      <MenuItem icon={House} onSelect={() => void navigate({ to: '/', search: {} })}>
        {words.all}
      </MenuItem>
    </DropdownMenu>
  );
}
