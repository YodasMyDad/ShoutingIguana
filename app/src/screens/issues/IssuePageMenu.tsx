// The row menu of a check's pages (its flat grid, and the pages of its open group): the items every grid of URLs has,
// and the Issues screen's own, "Show the page in URLs", the URL grid narrowed to that one URL with the inspector on
// it. A group's row has its own: its pages in URLs, as its "Show all" link has them.
import { useNavigate } from '@tanstack/react-router';
import { ClipboardList, Copy, ExternalLink, Image, Link2, ListFilter, PanelRight } from 'lucide-react';
import type { GridFilter, GridSort } from '../../api/grids';
import { RowMenu, UrlRowItems } from '../../grid/RowMenu';
import { copyRowUrls, rowMenuLabel, type RowMenuState } from '../../grid/rowMenuState';
import { formatCount } from '../../lib/format';
import { urlAtText } from '../../lib/textFragment';
import { openInBrowser } from '../../shell/os';
import { MenuItem, MenuSeparator } from '../../ui/Menu';
import { urlDisplay } from '../../ui/urlText';
import { oneUrlFilter } from '../urls/urlPlaces';
import { RecheckMenuItem } from '../rechecks/RecheckMenuItem';

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
  group?:
    | {
        words: string;
        filter: GridFilter[];
        urls: number;
        onCopyUrls: () => void;
        /** The URL the group is of (the broken link, the image), which the menu inspects and opens (A-06). */
        url?: { id: number; url: string } | null | undefined;
      }
    | null
    | undefined;
  /**
   * The one page that links to the row's URL, where there is exactly one (A-17): "Inspect the linking page" and "Open
   * the linking page at the link". Null or left out for any other row.
   */
  linkingPage?: { id: number; url: string; text: string | null } | null | undefined;
  /**
   * For a page holding the bad links (a check whose `references` is `targets`): its links in the Links grid, which
   * "Show its links in Links" opens. Null or left out for any other row.
   */
  linksOf?: GridFilter[] | null | undefined;
  /** What the row's URL is called in "Show … in URLs": "the page" where it's one, "it" for a URL that may not be. */
  isPage?: boolean | undefined;
  /** The URL of each row the menu acts on, for a copy; null for a row that has none (a group). */
  urlOf: (row: Row) => string | null | undefined;
  onInspect: (urlId: number) => void;
  /**
   * For a URL other pages refer to (a check whose `references` is `sources`): the links to it in the Links grid, which
   * "Show where it's found in Links" opens. Null or left out for any other row.
   */
  foundIn?: GridFilter[] | null | undefined;
  /**
   * Step 3b: for a page an image check flagged, the images it shows in Links' Images view (its filters, columns and
   * order), which "Show its images in Links" opens. Null or left out for any other row.
   */
  imagesIn?: { filter: GridFilter[]; cols: string[]; sort: GridSort[] } | null | undefined;
  /** The fix loop: Recheck the row or the selection, judged by this check; each row's URL id. */
  recheck?: { checkId: string; idOf: (row: Row) => number | null; fullCheck?: boolean } | null | undefined;
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
  imagesIn,
  recheck,
  linkingPage,
  linksOf,
  isPage = true,
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
            {isPage ? 'Show the page in URLs' : 'Show it in URLs'}
          </MenuItem>
          {linkingPage && count === 1 ? (
            <>
              <MenuItem icon={PanelRight} onSelect={() => onInspect(linkingPage.id)}>
                Inspect the linking page
              </MenuItem>
              <MenuItem
                icon={ExternalLink}
                onSelect={() => void openInBrowser(urlAtText(linkingPage.url, linkingPage.text))}
              >
                {linkingPage.text?.trim() ? 'Open the linking page at the link' : 'Open the linking page'}
              </MenuItem>
            </>
          ) : null}
          {linksOf ? (
            <MenuItem
              icon={Link2}
              onSelect={() =>
                void navigate({ to: '/crawls/$crawl/links', params: { crawl: crawlId }, search: { filter: linksOf } })
              }
            >
              Show its links in Links
            </MenuItem>
          ) : null}
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
          {recheck && state ? (
            <RecheckMenuItem<Row>
              crawlId={crawlId}
              checkId={recheck.checkId}
              count={count}
              rows={() => state.menu.rows()}
              idOf={recheck.idOf}
              fullCheck={recheck.fullCheck}
            />
          ) : null}
          {imagesIn ? (
            <MenuItem
              icon={Image}
              onSelect={() =>
                void navigate({ to: '/crawls/$crawl/links', params: { crawl: crawlId }, search: imagesIn })
              }
            >
              Show its images in Links
            </MenuItem>
          ) : null}
        </>
      ) : group ? (
        <>
          {group.url ? (
            // A group that is a URL (the broken link, the image): the menu acts on it, as on any URL.
            <>
              <MenuItem icon={PanelRight} shortcut="enter" onSelect={() => onInspect(group.url?.id ?? 0)}>
                Inspect
              </MenuItem>
              <MenuItem icon={ExternalLink} onSelect={() => void openInBrowser(group.url?.url ?? '')}>
                Open in the browser
              </MenuItem>
              <MenuItem
                icon={Link2}
                onSelect={() =>
                  void navigate({
                    to: '/crawls/$crawl/links',
                    params: { crawl: crawlId },
                    search: {
                      filter: [
                        { column: 'to.id', op: 'eq', value: group.url?.id ?? 0 },
                        { column: 'selfLink', op: 'is', value: false },
                      ],
                    },
                  })
                }
              >
                Show the links to it in Links
              </MenuItem>
              <MenuSeparator />
            </>
          ) : null}
          <MenuItem icon={Copy} onSelect={group.onCopyUrls}>
            {group.urls === 1 ? 'Copy its URL' : `Copy its ${formatCount(group.urls)} URLs`}
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
