// The row menu of a check's pages (its flat grid, and the pages of its open group): the items every grid of URLs has,
// and the Issues screen's own, "Show the page in URLs", the URL grid narrowed to that one URL with the inspector on
// it. A group's row has its own: its pages in URLs, as its "Show all" link has them.
import { useNavigate } from '@tanstack/react-router';
import { ClipboardList, Copy, Link2, ListFilter } from 'lucide-react';
import type { GridFilter } from '../../api/grids';
import { RowMenu, UrlRowItems } from '../../grid/RowMenu';
import { copyRowUrls, rowMenuLabel, type RowMenuState } from '../../grid/rowMenuState';
import { formatCount } from '../../lib/format';
import { MenuItem, MenuSeparator } from '../../ui/Menu';
import { urlDisplay } from '../../ui/urlText';
import { oneUrlFilter } from '../urls/urlPlaces';

export interface IssuePageMenuProps<Row> {
  crawlId: string;
  state: RowMenuState<Row> | null;
  onOpenChange: (open: boolean) => void;
  siteOrigin: string | null;
  /** The page the menu was opened on: its id and URL. Null for a row that isn't one (a group). */
  page: { id: number; url: string } | null;
  /**
   * A menu opened on a group's row: its rows in URLs ("Show all 12 pages in URLs") and their filter, how many URLs it
   * has, and a copy of them.
   */
  group?: { words: string; filter: GridFilter[]; urls: number; onCopyUrls: () => void } | null | undefined;
  /** The URL of each row the menu acts on, for a copy; null for a row that has none (a group). */
  urlOf: (row: Row) => string | null | undefined;
  onInspect: (urlId: number) => void;
  /**
   * For a URL other pages refer to (a check whose `references` is `sources`): the links to it in the Links grid, which
   * "Show where it's found in Links" opens. Null or left out for any other row.
   */
  foundIn?: GridFilter[] | null | undefined;
}

export function IssuePageMenu<Row>({
  crawlId,
  state,
  onOpenChange,
  siteOrigin,
  page,
  group,
  urlOf,
  onInspect,
  foundIn,
}: IssuePageMenuProps<Row>) {
  const navigate = useNavigate();
  const count = state?.menu.indices.length ?? 1;
  const label = page
    ? rowMenuLabel(count, urlDisplay(page.url, siteOrigin).shown, ['URL', 'URLs'])
    : 'Menu for the group';
  return (
    <RowMenu state={state} onOpenChange={onOpenChange} label={label}>
      {page ? (
        <>
          <UrlRowItems
            count={count}
            url={page.url}
            onInspect={() => onInspect(page.id)}
            onCopyUrls={() => {
              if (state) void copyRowUrls(state.menu, urlOf);
            }}
            onCopyRows={() => void state?.menu.copy()}
          />
          <MenuSeparator />
          <MenuItem
            icon={ListFilter}
            onSelect={() =>
              void navigate({
                to: '/crawls/$crawl/urls',
                params: { crawl: crawlId },
                search: { filter: oneUrlFilter(page.url, page.id), inspect: page.id },
              })
            }
          >
            Show the page in URLs
          </MenuItem>
          {foundIn ? (
            <MenuItem
              icon={Link2}
              onSelect={() =>
                void navigate({ to: '/crawls/$crawl/links', params: { crawl: crawlId }, search: { filter: foundIn } })
              }
            >
              Show where it’s found in Links
            </MenuItem>
          ) : null}
        </>
      ) : group ? (
        <>
          <MenuItem icon={Copy} onSelect={group.onCopyUrls}>
            {group.urls === 1 ? 'Copy the group’s URL' : `Copy the group’s ${formatCount(group.urls)} URLs`}
          </MenuItem>
          <MenuItem icon={ClipboardList} shortcut="mod+c" onSelect={() => void state?.menu.copy()}>
            Copy the row
          </MenuItem>
          <MenuSeparator />
          <MenuItem
            icon={ListFilter}
            onSelect={() =>
              void navigate({ to: '/crawls/$crawl/urls', params: { crawl: crawlId }, search: { filter: group.filter } })
            }
          >
            {group.words}
          </MenuItem>
        </>
      ) : null}
    </RowMenu>
  );
}
