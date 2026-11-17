// Step 3b's link reads as the sidecar answers them (docs/v2-sidecar-api.md, "Links grouped by target" and "A page's
// images"), for the mock, Storybook and tests: a view of the links grid one row a target, and the images one page
// shows. Both work from the links grid's own rows, so a view is filtered, searched and refused by the links grid's
// rules, and a target's facts are what its row's `to.` columns say of it.
import type { ProblemBody } from '../errors';
import type {
  GridFilter,
  GridSort,
  LinkGroup,
  LinkGroupsPage,
  LinkPosition,
  LinkRow,
  RelatedFacts,
  SummaryState,
  UrlOutcome,
} from '../grids';
import type { PageImages } from '../inspector';
import { invalidQuery } from './gridQuery';
import { LINK_COLUMNS, LINK_POSITION_VALUES, answerLinkQuery } from './linkGrid';

export type LinkAnswer<T> = { ok: true; page: T } | { ok: false; problem: ProblemBody };

/** The most of a target's links read for their text, as the sidecar reads them. */
const TEXT_SAMPLE = 200;
/** The fewest pages a template link is on, as a check's group is called one. */
const TEMPLATE_MIN_PAGES = 10;
const MAX_LIMIT = 200;

const badRequest = (detail: string): ProblemBody => ({
  type: 'bad-request',
  title: 'Bad request',
  status: 400,
  detail,
});

/** Every link a view of the links grid matches, in crawl order; or why the view can't be run. */
function viewLinks(
  links: readonly LinkRow[],
  filters: readonly GridFilter[] | undefined,
  search: string | undefined,
  summary: SummaryState,
): LinkAnswer<LinkRow[]> {
  const byId = new Map(links.map((link) => [link.id, link]));
  const matched: LinkRow[] = [];
  for (let offset = 0; ; offset += 1_000) {
    const answer = answerLinkQuery(
      links,
      {
        columns: ['kind'],
        filters: [...(filters ?? [])],
        sort: [],
        offset,
        limit: 1_000,
        ...(search !== undefined ? { search } : {}),
      },
      summary,
    );
    if (!answer.ok) return answer;
    for (const row of answer.page.rows) {
      const link = byId.get(row.id);
      if (link) matched.push(link);
    }
    if (answer.page.rows.length < 1_000) return { ok: true, page: matched };
  }
}

const numberOr = (value: unknown) => (typeof value === 'number' ? value : null);
const textOr = (value: unknown) => (typeof value === 'string' ? value : null);

/** What a link's target is, from its row's `to.` columns. */
export function targetFacts(link: LinkRow): RelatedFacts {
  return {
    statusCode: numberOr(link['to.statusCode']),
    outcome: textOr(link['to.outcome']) as UrlOutcome | null,
    contentType: textOr(link['to.contentType']),
    sizeBytes: numberOr(link['to.sizeBytes']),
    pixelWidth: numberOr(link['to.pixelWidth']),
    pixelHeight: numberOr(link['to.pixelHeight']),
    final: null,
    indexability: null,
    canonical: null,
  };
}

/** The text most of some links have among the first 200, the first found on a tie, and how many texts they have. */
function textsOf(links: readonly LinkRow[]): { text: string | null; texts: number } {
  const counts = new Map<string, { count: number; first: number }>();
  links.slice(0, TEXT_SAMPLE).forEach((link, index) => {
    const text = link.text ?? '';
    const seen = counts.get(text);
    counts.set(text, seen ? { count: seen.count + 1, first: seen.first } : { count: 1, first: index });
  });
  const top = [...counts.entries()]
    .filter(([text]) => text.length > 0)
    .sort(([, a], [, b]) => b.count - a.count || a.first - b.first)[0];
  return { text: top?.[0] ?? null, texts: counts.size };
}

const positionsOf = (links: readonly LinkRow[]): LinkPosition[] =>
  LINK_POSITION_VALUES.filter((position) => links.some((link) => link.position === position));

const SORTABLE_TARGET = new Set(
  LINK_COLUMNS.filter((column) => column.sortable && column.id.startsWith('to.')).map((column) => column.id),
);

/** A target's value for a sort by one of its columns, from its first link's row. */
function sortValue(link: LinkRow, column: string): string | number | null {
  const value = (link as unknown as Record<string, unknown>)[column];
  return typeof value === 'number' || typeof value === 'string' ? value : null;
}

/** `POST /crawls/{id}/links/groups`: a page of a view's targets, as the sidecar answers it. */
export function answerLinkGroups(
  links: readonly LinkRow[],
  body: unknown,
  summary: SummaryState,
): LinkAnswer<LinkGroupsPage> {
  const query = (body ?? {}) as Partial<{
    filters: GridFilter[];
    search: string;
    sort: GridSort[];
    offset: number;
    limit: number;
    end: unknown;
    columns: unknown;
  }>;
  if (query.end !== undefined || query.columns !== undefined) {
    return {
      ok: false,
      problem: badRequest('The links grouped by target take the links grid’s filters, a sort and a page.'),
    };
  }
  const offset = query.offset ?? 0;
  const limit = query.limit ?? 100;
  const asked = query.sort ?? [];
  const sort: GridSort = asked[0] ?? { column: 'pages', desc: true };
  const errors: Record<string, string[]> = {};
  if (!Number.isInteger(limit) || limit < 1 || limit > MAX_LIMIT)
    errors.limit = [`limit must be from 1 to ${MAX_LIMIT}.`];
  if (!Number.isInteger(offset) || offset < 0) errors.offset = ['offset must be 0 or more.'];
  if (asked.length > 1) errors.sort = ['The links grouped by target sort by one key.'];
  else if (sort.column !== 'pages' && sort.column !== 'links' && !SORTABLE_TARGET.has(sort.column)) {
    errors['sort.column'] = [
      `The links grouped by target sort by 'pages', 'links', or a column of the target that the URL grid sorts by; not by '${sort.column}'.`,
    ];
  }
  if (Object.keys(errors).length > 0) return { ok: false, problem: invalidQuery(errors) };
  const matched = viewLinks(links, query.filters, query.search, summary);
  if (!matched.ok) return matched;

  const byTarget = new Map<number, LinkRow[]>();
  for (const link of matched.page) {
    const id = link['to.id'];
    if (typeof id !== 'number') continue;
    const own = byTarget.get(id);
    if (own) own.push(link);
    else byTarget.set(id, [link]);
  }
  const sources = new Set(matched.page.map((link) => link['from.id'])).size;
  const groups = [...byTarget.entries()].flatMap(([id, own]): (LinkGroup & { first: LinkRow })[] => {
    const [first] = own;
    if (!first) return [];
    const pages = new Set(own.map((link) => link['from.id'])).size;
    return [
      {
        id,
        url: first['to.url'] ?? '',
        links: own.length,
        pages,
        ...textsOf(own),
        positions: positionsOf(own),
        likelyTemplate: pages >= TEMPLATE_MIN_PAGES && pages * 2 >= sources,
        target: targetFacts(first),
        first,
      },
    ];
  });
  const sign = sort.desc ? -1 : 1;
  groups.sort((a, b) => {
    if (sort.column === 'pages') return sign * (a.pages - b.pages) || sign * (a.links - b.links) || a.id - b.id;
    if (sort.column === 'links') return sign * (a.links - b.links) || sign * (a.pages - b.pages) || a.id - b.id;
    const left = sortValue(a.first, sort.column);
    const right = sortValue(b.first, sort.column);
    if (left === null || right === null) return left === right ? sign * (a.id - b.id) : left === null ? 1 : -1;
    const order =
      typeof left === 'number' && typeof right === 'number' ? left - right : String(left).localeCompare(String(right));
    return sign * order || sign * (a.id - b.id);
  });
  return {
    ok: true,
    page: {
      total: groups.length,
      links: matched.page.length,
      pages: sources,
      offset,
      sort,
      rows: groups.slice(offset, offset + limit).map(({ first: _first, ...group }) => group),
      summary,
    },
  };
}

/** `POST /crawls/{id}/urls/images`: the images a page shows, each file once, the largest first. Null for no such page. */
export function answerPageImages(links: readonly LinkRow[], urlId: number, limit = 100): PageImages {
  const own = links.filter((link) => link['from.id'] === urlId && link.kind === 'image' && link['to.id'] !== urlId);
  const byTarget = new Map<number, LinkRow[]>();
  for (const link of own) {
    const id = link['to.id'];
    if (typeof id !== 'number') continue;
    const shown = byTarget.get(id);
    if (shown) shown.push(link);
    else byTarget.set(id, [link]);
  }
  const images = [...byTarget.entries()].flatMap(([id, shown], index) => {
    const [first] = shown;
    if (!first) return [];
    return [
      {
        index,
        image: {
          id,
          url: first['to.url'] ?? '',
          links: shown.length,
          text:
            shown
              .map((link) => link.text)
              .find((text): text is string => typeof text === 'string' && text.length > 0) ?? null,
          positions: positionsOf(shown),
          target: targetFacts(first),
        },
      },
    ];
  });
  images.sort((a, b) => {
    const left = a.image.target.sizeBytes;
    const right = b.image.target.sizeBytes;
    if (left === null || right === null) return left === right ? a.index - b.index : left === null ? 1 : -1;
    return right - left || a.index - b.index;
  });
  return { urlId, total: images.length, links: own.length, rows: images.slice(0, limit).map((entry) => entry.image) };
}
