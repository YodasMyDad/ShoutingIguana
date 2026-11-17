// What each screen offers to export, built from what it shows (docs/v2-ui-ia.md, flow 5): a grid's own view as CSV or
// Excel (its columns, filters, search and sort, exactly as its read takes them), a check, and on Overview and Issues
// the report and the spreadsheet of everything. Pure: each screen hands in its state, and the tests hold every request.
import type { CheckGroupRef, GridFilter, GridSort, LinkEnds, RobotsBlockReason } from '../api/grids';
import type { CrawlListItem, CheckReferences } from '../api/types';
import { crawlName } from '../crawl/status';
import { formatCount, formatCountOf } from '../lib/format';
import { currentHiddenChecks } from '../settings/hiddenChecks';
import type { ExportChoice, ExportOffer } from './exportOffer';

/** A column's id and its title, as the engine's column lists give them. */
export interface TitledColumn {
  id: string;
  title?: string | undefined;
}

/** Said above the choices while the crawl runs: what an export of a running crawl holds. */
export const CRAWLING_NOTE =
  'The crawl is running. The file will hold the crawl as it is when the export begins: what it finds after that isn’t in it.';

/** The columns a running crawl's export may still be sorted by: these don't change as it goes on. */
const STABLE_SORT_COLUMNS: ReadonlySet<string> = new Set(['url', 'id', 'to.url', 'to.id']);

function base(item: CrawlListItem, title: string, crawling: boolean): Omit<ExportOffer, 'choices'> {
  return {
    crawlId: item.id,
    site: item.site ?? crawlName(item),
    crawlDate: item.startedAt,
    title,
    note: crawling ? CRAWLING_NOTE : null,
  };
}

function titlesOf(columns: readonly TitledColumn[] | undefined): Record<string, string> {
  const titles: Record<string, string> = {};
  for (const column of columns ?? []) if (column.title) titles[column.id] = column.title;
  return titles;
}

/** "these filters, this search, this sort, these 7 columns": what the file keeps of the view, in words. */
export function viewWords(view: { filters: number; search: boolean; sort: number; columns: number }): string {
  const parts = [
    view.filters > 0 ? (view.filters === 1 ? 'this filter' : `these ${formatCount(view.filters)} filters`) : null,
    view.search ? 'this search' : null,
    view.sort > 0 ? 'this sort' : 'in crawl order',
    view.columns === 1 ? 'this column' : `these ${formatCount(view.columns)} columns`,
  ].filter((part): part is string => part !== null);
  return parts.join(', ');
}

/**
 * The URL grid's columns as its export writes them: the URL first, then those shown in their order. A status shown
 * as words where a URL has no code (blocked by robots.txt, not checked) is its `outcome`, so it goes beside the code.
 */
export function urlExportColumns(shown: readonly string[]): string[] {
  const columns = new Set<string>(['url']);
  for (const id of shown) {
    columns.add(id);
    if (id === 'statusCode') columns.add('outcome');
  }
  return [...columns];
}

/** The links grid's columns as its export writes them: the follow column is its four flags. */
export function linkExportColumns(
  shown: readonly string[],
  followColumn: string,
  followParts: readonly string[],
): string[] {
  const columns = new Set<string>();
  for (const id of shown) {
    if (id === followColumn) followParts.forEach((part) => columns.add(part));
    else columns.add(id);
    const end = /^(from|to)\.statusCode$/.exec(id)?.[1];
    if (end) columns.add(`${end}.outcome`);
  }
  return [...columns];
}

interface GridViewArgs {
  item: CrawlListItem;
  crawling: boolean;
  /** The columns the export writes, in order. */
  columns: readonly string[];
  filters: readonly GridFilter[];
  sort: readonly GridSort[];
  search: string | undefined;
  /** The rows the view holds now, when known. */
  rows: number | null;
  catalogue?: readonly TitledColumn[] | undefined;
}

function sortedWhileCrawling(crawling: boolean, sort: readonly GridSort[]): boolean {
  return crawling && sort.some((key) => !STABLE_SORT_COLUMNS.has(key.column));
}

function gridQuery(args: GridViewArgs, crawlOrder: boolean) {
  return {
    columns: [...args.columns],
    filters: [...args.filters],
    ...(args.search ? { search: args.search } : {}),
    ...(crawlOrder || args.sort.length === 0 ? {} : { sort: [...args.sort] }),
  };
}

function gridDescription(args: GridViewArgs, noun: [string, string]): string {
  const what = viewWords({
    filters: args.filters.length,
    search: Boolean(args.search),
    sort: args.sort.length,
    columns: args.columns.length,
  });
  return args.rows === null
    ? `The ${noun[1]} shown: ${what}`
    : `${formatCountOf(args.rows, noun[0], noun[1])}: ${what}`;
}

/** A view narrowed to a links view's ends, in its description and in its file's name. */
const LINK_ENDS_WORDS: Record<LinkEnds['end'], { description: string; file: string }> = {
  to: { description: 'only the URLs a links view’s links lead to', file: 'link targets' },
  from: { description: 'only the pages a links view’s links are on', file: 'pages with the links' },
};

/**
 * URLs: the view, as CSV or Excel. A view narrowed to a links view's pages or targets (`linkEnds`) is exported
 * narrowed to them, as the grid reads it (E7), and its file's name says which.
 */
export function urlGridOffer(
  args: GridViewArgs & { viewName: string | null; linkEnds: LinkEnds | undefined },
): ExportOffer {
  const ends = args.linkEnds;
  const narrowed = ends ? LINK_ENDS_WORDS[ends.end] : null;
  const view: ExportChoice = {
    id: 'view',
    title: 'This view',
    description: narrowed
      ? `${gridDescription(args, ['URL', 'URLs'])}, ${narrowed.description}`
      : gridDescription(args, ['URL', 'URLs']),
    formats: ['csv', 'xlsx'],
    fileView: [args.viewName ? `URLs ${args.viewName}` : 'URLs', narrowed?.file].filter(Boolean).join(' '),
    columnTitles: titlesOf(args.catalogue),
    sortedWhileCrawling: sortedWhileCrawling(args.crawling, args.sort),
    build: ({ format, path, crawlOrder }) => ({
      kind: 'urls',
      format: format === 'xlsx' ? 'xlsx' : 'csv',
      path,
      ...gridQuery(args, crawlOrder),
      ...(ends ? { linkEnds: linkEndsOf(ends) } : {}),
    }),
  };
  return { ...base(args.item, 'Export URLs', args.crawling), choices: [view] };
}

/** A links view's ends as the export's request takes them: the fields given, and only those. */
function linkEndsOf(ends: LinkEnds): LinkEnds {
  return {
    end: ends.end,
    ...(ends.filters && ends.filters.length > 0 ? { filters: [...ends.filters] } : {}),
    ...(ends.search ? { search: ends.search } : {}),
  };
}

/** The inspector's links of one URL as the links grid's export writes them: the other end, then the link's own. */
export function urlLinksExportColumns(direction: 'inlinks' | 'outlinks', followParts: readonly string[]): string[] {
  const end = direction === 'inlinks' ? 'from' : 'to';
  return [`${end}.url`, `${end}.statusCode`, `${end}.outcome`, 'text', 'kind', 'position', ...followParts];
}

/**
 * The inspector's inlinks or outlinks of one URL (gap 22 of the closing audit): the links grid's export, filtered to
 * them as "All N in Links" filters, so the file holds exactly the list's links.
 */
export function urlLinksOffer(args: {
  item: CrawlListItem;
  crawling: boolean;
  direction: 'inlinks' | 'outlinks';
  /** The list's own filters (`linksOfUrl`): the URL, the kind chosen, no link from a page to itself. */
  filters: readonly GridFilter[];
  rows: number | null;
  /** The URL whose links they are, as the inspector shows it, for the file's name and the dialog's words. */
  url: string;
  followParts: readonly string[];
  catalogue?: readonly TitledColumn[] | undefined;
}): ExportOffer {
  const columns = urlLinksExportColumns(args.direction, args.followParts);
  const noun: [string, string] = args.direction === 'inlinks' ? ['inlink', 'inlinks'] : ['outlink', 'outlinks'];
  const choice: ExportChoice = {
    id: 'view',
    title: args.direction === 'inlinks' ? 'These inlinks' : 'These outlinks',
    description:
      args.rows === null
        ? `The ${noun[1]} of ${args.url}`
        : `${formatCountOf(args.rows, noun[0], noun[1])} of ${args.url}, in crawl order`,
    formats: ['csv', 'xlsx'],
    fileView: `${noun[1]} of ${args.url}`,
    columnTitles: titlesOf(args.catalogue),
    build: ({ format, path }) => ({
      kind: 'links',
      format: format === 'xlsx' ? 'xlsx' : 'csv',
      path,
      columns,
      filters: [...args.filters],
    }),
  };
  return {
    ...base(args.item, args.direction === 'inlinks' ? 'Export inlinks' : 'Export outlinks', args.crawling),
    choices: [choice],
  };
}

/** Links: the view, as CSV or Excel. */
export function linksOffer(args: GridViewArgs & { viewName: string | null }): ExportOffer {
  const view: ExportChoice = {
    id: 'view',
    title: 'This view',
    description: gridDescription(args, ['link', 'links']),
    formats: ['csv', 'xlsx'],
    fileView: args.viewName ?? 'links',
    columnTitles: titlesOf(args.catalogue),
    sortedWhileCrawling: sortedWhileCrawling(args.crawling, args.sort),
    build: ({ format, path, crawlOrder }) => ({
      kind: 'links',
      format: format === 'xlsx' ? 'xlsx' : 'csv',
      path,
      ...gridQuery(args, crawlOrder),
    }),
  };
  return { ...base(args.item, 'Export links', args.crawling), choices: [view] };
}

/** One sitemap's URLs: the view, as CSV or Excel. */
export function sitemapUrlsOffer(args: GridViewArgs & { sitemapId: number; sitemapName: string }): ExportOffer {
  const view: ExportChoice = {
    id: 'view',
    title: 'This sitemap’s URLs',
    description: gridDescription(args, ['URL', 'URLs']),
    formats: ['csv', 'xlsx'],
    fileView: `URLs in ${args.sitemapName}`,
    columnTitles: titlesOf(args.catalogue),
    sortedWhileCrawling: sortedWhileCrawling(args.crawling, args.sort),
    build: ({ format, path, crawlOrder }) => ({
      kind: 'sitemapUrls',
      format: format === 'xlsx' ? 'xlsx' : 'csv',
      path,
      sitemapId: args.sitemapId,
      ...gridQuery(args, crawlOrder),
    }),
  };
  return { ...base(args.item, 'Export a sitemap’s URLs', args.crawling), choices: [view] };
}

/**
 * An origin's blocked URLs, all or one line's (or one reason's, or those your sitemap or menu asks for), as CSV or
 * Excel, each with what blocking it costs: the pages linking to it, its sitemap, and whether that conflicts.
 */
export function robotsBlockedOffer(args: {
  item: CrawlListItem;
  crawling: boolean;
  origin: string;
  line?: number | undefined;
  reason?: RobotsBlockReason | undefined;
  /** Step 3b: only the blocked URLs a sitemap lists or a menu links to. */
  conflict?: boolean | undefined;
  rows: number | null;
}): ExportOffer {
  const host = args.origin.replace(/^[a-z]+:\/\//i, '');
  // What the list is narrowed to, as its title, its description and its file's name say it.
  const narrowed =
    args.line !== undefined
      ? {
          title: `What line ${formatCount(args.line)} blocked`,
          which: `what line ${formatCount(args.line)} blocked`,
          file: `blocked by ${host} robots.txt line ${args.line}`,
        }
      : args.reason === 'unreachable'
        ? {
            title: 'Refused as robots.txt couldn’t be read',
            which: 'every URL refused because its robots.txt couldn’t be read',
            file: `refused by ${host} robots.txt unread`,
          }
        : args.reason === 'unknown'
          ? {
              title: 'Blocked with no matching rule',
              which: 'every URL blocked that no rule in the copy of the file kept matches',
              file: `blocked by ${host} robots.txt no matching rule`,
            }
          : args.reason === 'rule'
            ? {
                title: 'Blocked by a rule',
                which: 'every URL a rule blocked',
                file: `blocked by ${host} robots.txt rules`,
              }
            : args.conflict === true
              ? {
                  title: 'In your sitemap or menu but blocked',
                  which: 'every URL it blocked that your sitemap or menu asks for',
                  file: `blocked by ${host} robots.txt in sitemap or menu`,
                }
              : { title: 'The blocked URLs', which: 'every URL it blocked', file: `blocked by ${host} robots.txt` };
  // The conflicts are the ones the costs are about: their line is the file's to show.
  const columns =
    args.conflict === true
      ? 'with the pages linking to each and its sitemap'
      : 'with the line that decided each, the pages linking to it and its sitemap';
  const view: ExportChoice = {
    id: 'view',
    title: narrowed.title,
    description:
      args.rows === null
        ? `${host}’s robots.txt: ${narrowed.which}, ${columns}`
        : `${formatCountOf(args.rows, 'URL')}: ${narrowed.which}, ${columns}`,
    formats: ['csv', 'xlsx'],
    fileView: narrowed.file,
    build: ({ format, path }) => ({
      kind: 'robotsBlocked',
      format: format === 'xlsx' ? 'xlsx' : 'csv',
      path,
      origin: args.origin,
      ...(args.line !== undefined ? { line: args.line } : {}),
      ...(args.reason !== undefined ? { reason: args.reason } : {}),
      ...(args.conflict === true ? { conflict: true } : {}),
    }),
  };
  return { ...base(args.item, 'Export blocked URLs', args.crawling), choices: [view] };
}

/** Whether the crawl's results have been worked out: the report and the workbook need them. */
export function isAnalysed(item: CrawlListItem): boolean {
  return item.analyzedAt !== null;
}

/**
 * The checks the user hid in this crawl, as they are when Export is pressed: the report, the workbook and one check's
 * file leave them out, as every screen does (docs/v2-ui-discovery.md, decision 2). Read from the preferences, the one
 * place outside the screen's own state an offer looks. Nothing while none is hidden.
 */
function hiddenChecksOf(item: CrawlListItem): { hiddenChecks?: string[] } {
  const ids = currentHiddenChecks(item.id);
  return ids.length > 0 ? { hiddenChecks: [...ids] } : {};
}

/**
 * "The report…" and "Spreadsheet of everything…" (docs/v2-ui-simple.md, What round 3 builds, 6 and 7). A crawl never
 * analysed has neither: each says so, with "Work out the results" in its place.
 */
export function crawlFileChoices(item: CrawlListItem, simple: boolean): ExportChoice[] {
  const hidden = () => hiddenChecksOf(item);
  const unavailable = isAnalysed(item)
    ? null
    : simple
      ? 'The results haven’t been worked out for this check, so there’s no report yet. Work out the results first.'
      : 'The crawl hasn’t been analysed, so there’s no report or workbook yet. Analyse it first.';
  return [
    {
      id: 'report',
      title: 'The report',
      description: 'One page to send: the score, what to fix first and how. Opens in any browser, prints to PDF',
      formats: ['html'],
      cuts: true,
      fileView: 'report',
      unavailable,
      remedy: 'analyse',
      build: ({ path, cut }) => ({
        kind: 'report',
        format: 'html',
        path,
        ...(cut === 'summary' ? { cut } : {}),
        ...hidden(),
      }),
    },
    {
      id: 'workbook',
      title: 'Spreadsheet of everything',
      description: simple
        ? 'A summary sheet, then every check that found something on a sheet of its own, with every address'
        : 'A summary sheet, then every check that found something on a sheet of its own, with every URL',
      formats: ['xlsx'],
      // The design names it beside the report: "… report.html" and "… report.xlsx" (docs/v2-ui-simple.md).
      fileView: 'report',
      unavailable,
      remedy: 'analyse',
      build: ({ path }) => ({ kind: 'workbook', format: 'xlsx', path, ...hidden() }),
    },
  ];
}

/** Overview: the report and the spreadsheet of everything. */
export function overviewOffer(item: CrawlListItem, crawling: boolean, simple: boolean): ExportOffer {
  return { ...base(item, simple ? 'Save the report' : 'Export', crawling), choices: crawlFileChoices(item, simple) };
}

/**
 * What a check's file adds, where its URLs are referred to (the engine's where-found columns): the pages linking to
 * each, or the URLs each page links to.
 */
function whereInTheFile(references: CheckReferences | undefined): string {
  if (references === 'sources') return ' and the pages that link to it';
  if (references === 'targets') return ' and the URLs it links to';
  return '';
}

/**
 * Issues: this check (its sheet of the workbook, as a file of its own), the open group's pages, then the report and
 * every check (the spreadsheet of everything).
 */
export function issuesOffer(args: {
  item: CrawlListItem;
  crawling: boolean;
  simple: boolean;
  /**
   * The check selected. `flat` is a check whose pages the screen lists as they are (its grid, sorted as its route
   * says), not in groups: its export is that list, in that order.
   */
  check: {
    id: string;
    title: string;
    urls: number | null;
    flat?: boolean;
    sort?: readonly GridSort[];
    /** How its URLs stand to references: the file says where each is found, or what each page names. */
    references?: CheckReferences | undefined;
  } | null;
  /** The open group, with its pages' count and its words. */
  group: { ref: CheckGroupRef; pages: number | null; words: string } | null;
  /**
   * The checks the list's filters keep (severity, category, the text filter), by id, when the list is filtered: the
   * workbook of those alone is offered beside the one of every check. Null or left out when the list isn't filtered.
   */
  listed?: { ids: readonly string[]; words: string } | null | undefined;
}): ExportOffer {
  const choices: ExportChoice[] = [];
  if (args.check) {
    const check = args.check;
    choices.push({
      id: 'check',
      title: 'This check',
      description:
        (check.urls === null
          ? `${check.title}: every page it found, with what was found on each`
          : `${check.title}: ${formatCountOf(check.urls, 'page')}, with what was found on each`) +
        whereInTheFile(check.references),
      formats: ['xlsx', 'csv'],
      fileView: check.title,
      sortedWhileCrawling: check.flat === true && sortedWhileCrawling(args.crawling, check.sort ?? []),
      build: ({ format, path, crawlOrder }) =>
        check.flat
          ? {
              // The grid's own read: its two columns, in its order.
              kind: 'issueRows',
              format: format === 'csv' ? 'csv' : 'xlsx',
              path,
              checkId: check.id,
              columns: ['url', 'detail'],
              filters: [],
              ...(crawlOrder || !check.sort?.length ? {} : { sort: [...check.sort] }),
            }
          : {
              // The check's sheet of the workbook, as a file of its own: numbered as the workbook with the same
              // checks hidden numbers it (R2-9).
              kind: 'check',
              format: format === 'csv' ? 'csv' : 'xlsx',
              path,
              checkId: check.id,
              ...hiddenChecksOf(args.item),
            },
    });
    if (args.group) {
      const group = args.group;
      choices.push({
        id: 'group',
        title: 'This group’s pages',
        description: group.pages === null ? group.words : `${formatCountOf(group.pages, 'page')}: ${group.words}`,
        formats: ['xlsx', 'csv'],
        fileView: `${check.title} group`,
        build: ({ format, path }) => ({
          kind: 'issueRows',
          format: format === 'csv' ? 'csv' : 'xlsx',
          path,
          checkId: check.id,
          group: group.ref,
          columns: ['url', 'title', 'detail', 'relatedUrl'],
        }),
      });
    }
  }
  const all = crawlFileChoices(args.item, args.simple);
  const files = all.flatMap((choice) => {
    if (choice.id !== 'workbook') return [choice];
    const every = { ...choice, title: 'Every check: the spreadsheet of everything' };
    const listed = args.listed;
    if (!listed) return [every];
    // The workbook takes the list's filters, as a grid's file takes the grid's (docs/v2-ui-discovery.md, Export).
    const view: ExportChoice = {
      ...choice,
      id: 'workbookListed',
      title: 'The checks listed: their spreadsheet',
      description: `${formatCountOf(listed.ids.length, 'check')} (${listed.words}): a summary sheet, then each that found something on a sheet of its own`,
      fileView: 'checks listed',
      build: (target) => {
        const request = choice.build(target);
        return request.kind === 'workbook' ? { ...request, checks: [...listed.ids] } : request;
      },
    };
    return [view, every];
  });
  return { ...base(args.item, 'Export issues', args.crawling), choices: [...choices, ...files] };
}

/**
 * Sitemaps: the list, as CSV or Excel (E7): every sitemap the crawl looked for, in the order it found them, each with
 * the index that lists it, how it was found, what became of it and its URLs counted, in the list's own words.
 */
export function sitemapsOffer(args: { item: CrawlListItem; crawling: boolean; rows: number | null }): ExportOffer {
  const view: ExportChoice = {
    id: 'view',
    title: 'The list of sitemaps',
    description:
      args.rows === null
        ? 'Every sitemap the crawl looked for, with what became of it and its URLs counted'
        : `${formatCountOf(args.rows, 'sitemap')}: what became of each, and its URLs counted`,
    formats: ['csv', 'xlsx'],
    fileView: 'sitemaps',
    build: ({ format, path }) => ({ kind: 'sitemaps', format: format === 'xlsx' ? 'xlsx' : 'csv', path }),
  };
  return { ...base(args.item, 'Export sitemaps', args.crawling), choices: [view] };
}

/**
 * robots.txt: the origins, as CSV or Excel (E7): each origin's answer, what it meant to the crawl and the URLs it
 * blocked, in the list's order; the list's search and its filter (`blocked`) narrow it, as they narrow the list.
 */
export function robotsOriginsOffer(args: {
  item: CrawlListItem;
  crawling: boolean;
  rows: number | null;
  search?: string | undefined;
  blocked?: boolean | undefined;
}): ExportOffer {
  const which = args.blocked === true ? ' that blocked a URL' : args.blocked === false ? ' that blocked nothing' : '';
  const searched = args.search ? `, whose name has “${args.search}”` : '';
  const view: ExportChoice = {
    id: 'view',
    title: 'The origins',
    description:
      args.rows === null
        ? `Every origin the crawl asked for robots.txt${which}${searched}, with what its answer meant`
        : `${formatCountOf(args.rows, 'origin')}${which}${searched}: what each answer meant, and the URLs it blocked`,
    formats: ['csv', 'xlsx'],
    fileView: `robots.txt origins${which}`,
    build: ({ format, path }) => ({
      kind: 'robotsOrigins',
      format: format === 'xlsx' ? 'xlsx' : 'csv',
      path,
      ...(args.search ? { search: args.search } : {}),
      ...(args.blocked !== undefined ? { blocked: args.blocked } : {}),
    }),
  };
  return { ...base(args.item, 'Export robots.txt', args.crawling), choices: [view] };
}
