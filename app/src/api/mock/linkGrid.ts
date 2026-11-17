// The links grid as the sidecar answers it (docs/v2-sidecar-api.md, Links), for the mock, Storybook and tests: its
// columns (`GET /api/link-columns`), a page of links (`POST /links`) and the links counted by value (`POST
// /links/facets`). It runs on the URL grid's own query code (gridQuery.ts) over rows keyed as the sidecar keys them
// (`to.statusCode`), so a links query is checked, refused and paged by the same rules, with the links grid's own:
// one sort key, `id` or a column of the target that the URL grid sorts by, and a search of the link's text and both
// ends' URLs. `from.check` and `to.check` are the sidecar's alone: the mock has no issues by link end.
import type { ProblemBody } from '../errors';
import type {
  FacetBucket,
  GridFilter,
  GridQuery,
  LinkColumn,
  LinkFacetId,
  LinkFacetsPage,
  LinkFacetsQuery,
  LinkKind,
  LinkPosition,
  LinkRow,
  SummaryState,
  UrlColumnType,
  UrlRow,
} from '../grids';
import {
  answerGridQuery,
  invalidQuery,
  matchingRows,
  needsAnalysisProblem,
  queryErrors,
  valueFacet,
  type GridAnswer,
  type GridShape,
} from './gridQuery';
import { URL_COLUMNS } from './urlColumns';

export const LINK_KIND_VALUES: readonly LinkKind[] = [
  'hyperlink',
  'image',
  'script',
  'stylesheet',
  'iframe',
  'media',
  'icon',
  'canonical',
  'hreflang',
  'pagination',
  'metaRefresh',
  'redirect',
];

export const LINK_POSITION_VALUES: readonly LinkPosition[] = [
  'content',
  'navigation',
  'header',
  'footer',
  'sidebar',
  'head',
];

const own = (id: string, type: UrlColumnType, values: readonly string[] = []): LinkColumn => ({
  id,
  type,
  needsAnalysis: false,
  // Of a link's own columns, only its id sorts.
  sortable: id === 'id',
  filterable: true,
  values: [...values],
});

/** A link's own columns, then the URL grid's for the page it's on (none sorts) and for its target. */
export const LINK_COLUMNS: readonly LinkColumn[] = [
  own('id', 'number'),
  own('kind', 'enum', LINK_KIND_VALUES),
  own('text', 'text'),
  own('position', 'enum', LINK_POSITION_VALUES),
  own('noFollow', 'boolean'),
  own('sponsored', 'boolean'),
  own('ugc', 'boolean'),
  own('imageLink', 'boolean'),
  own('selfLink', 'boolean'),
  own('followed', 'boolean'),
  own('fragment', 'text'),
  ...(['from', 'to'] as const).flatMap((end) =>
    URL_COLUMNS.filter((column) => !column.issueRowsOnly).map(
      ({ issueRowsOnly: _issueRowsOnly, ...column }): LinkColumn => ({
        ...column,
        id: `${end}.${column.id}`,
        // The engine sorts links by their target only.
        sortable: end === 'to' && column.sortable,
      }),
    ),
  ),
];

export function linkColumn(id: string): LinkColumn | undefined {
  return LINK_COLUMNS.find((column) => column.id === id);
}

const LINK_SHAPE: GridShape = {
  column: linkColumn,
  searchColumns: ['text', 'from.url', 'to.url'],
  maxSorts: 1,
  defaultColumns: ['from.url', 'to.url', 'kind', 'text'],
};

/** What a link is, before its two ends are joined to it. */
export interface LinkFacts {
  fromId: number;
  toId: number;
  kind: LinkKind | null;
  text: string | null;
  position: LinkPosition | null;
  noFollow: boolean;
  sponsored: boolean;
  ugc: boolean;
  imageLink: boolean;
  fragment: string | null;
}

/**
 * Links as the grid's rows: each with its id (its place in `links`, from 1), its own columns, and every URL grid
 * column of both ends from the crawl's URLs. A link either of whose ends the URLs don't have is left out, as the
 * crawl stores no such link. `followed` is the engine's: a hyperlink with no nofollow on it nor on its page, and null
 * for any other kind.
 */
export function linkRows(links: readonly LinkFacts[], urls: readonly UrlRow[]): LinkRow[] {
  const byId = new Map(urls.map((url) => [url.id, url]));
  const rows: LinkRow[] = [];
  for (const link of links) {
    const from = byId.get(link.fromId);
    const to = byId.get(link.toId);
    if (!from || !to) continue;
    const row: LinkRow = {
      id: rows.length + 1,
      kind: link.kind,
      text: link.text,
      position: link.position,
      noFollow: link.noFollow,
      sponsored: link.sponsored,
      ugc: link.ugc,
      imageLink: link.imageLink,
      selfLink: link.fromId === link.toId,
      followed: link.kind === 'hyperlink' ? !link.noFollow && from.nofollow !== true : null,
      fragment: link.fragment,
    };
    for (const [end, url] of [
      ['from', from],
      ['to', to],
    ] as const) {
      for (const [column, value] of Object.entries(url)) {
        row[`${end}.${column}`] = value as string | number | boolean | null;
      }
    }
    rows.push(row);
  }
  return rows;
}

/** A page of links, or the problem the sidecar would give: `group` is a check's rows' alone. */
export function answerLinkQuery(
  rows: readonly LinkRow[],
  query: GridQuery,
  summary: SummaryState,
): GridAnswer<Partial<LinkRow> & { id: number }> {
  const answer = answerGridQuery<LinkRow>(rows, query, summary, { shape: LINK_SHAPE });
  // Whether the crawl measured images' pixel sizes, as the sidecar says it: here, whether any target has one.
  return answer.ok ? { ok: true, page: { ...answer.page, pixelSizes: pixelsMeasured(rows) } } : answer;
}

/** Whether a crawl's links show it measured images' pixel sizes: some target has a width. */
export function pixelsMeasured(rows: readonly LinkRow[]): boolean {
  return rows.some((row) => typeof row['to.pixelWidth'] === 'number');
}

export const LINK_FACETS: readonly LinkFacetId[] = ['kind', 'position', 'follow', 'to.statusClass', 'to.isInternal'];

/** `followed` as the `follow` facet names its values, each with the filter that selects it. */
function followFacet(rows: readonly LinkRow[]): FacetBucket[] {
  const buckets: [string, GridFilter, (row: LinkRow) => boolean][] = [
    ['followed', { column: 'followed', op: 'is', value: true }, (row) => row.followed === true],
    ['nofollow', { column: 'followed', op: 'is', value: false }, (row) => row.followed === false],
    [
      'notHyperlink',
      { column: 'followed', op: 'isEmpty' },
      (row) => row.followed === null || row.followed === undefined,
    ],
  ];
  return buckets
    .map(([value, filter, has]) => ({ value, filter, count: rows.filter(has).length }))
    .filter((bucket) => bucket.count > 0);
}

export type LinkFacetsAnswer = { ok: true; page: LinkFacetsPage } | { ok: false; problem: ProblemBody };

/** The links a filter set and a search match, counted by value: only the values with links, each once per facet. */
export function answerLinkFacets(
  rows: readonly LinkRow[],
  query: LinkFacetsQuery,
  summary: SummaryState,
): LinkFacetsAnswer {
  const filters = query.filters ?? [];
  const errors = queryErrors(
    { filters, sort: [], limit: 1, ...(query.search !== undefined ? { search: query.search } : {}) },
    false,
    LINK_SHAPE,
  );
  ((query.facets ?? []) as readonly unknown[]).forEach((id, index) => {
    if (typeof id !== 'string') (errors[`facets[${index}]`] ??= []).push('Give a facet’s name.');
    else if (!LINK_FACETS.includes(id as LinkFacetId)) {
      (errors[`facets[${index}]`] ??= []).push(`There’s no facet called ‘${id}’: there are ${LINK_FACETS.join(', ')}.`);
    }
  });
  if (Object.keys(errors).length > 0) return { ok: false, problem: invalidQuery(errors) };
  const analysis = needsAnalysisProblem(filters, [], summary, linkColumn);
  if (analysis) return { ok: false, problem: analysis };
  const matched = matchingRows(rows, filters, query.search ?? '', LINK_SHAPE.searchColumns);
  const wanted = query.facets && query.facets.length > 0 ? [...new Set(query.facets)] : LINK_FACETS;
  const facets: LinkFacetsPage['facets'] = {};
  for (const id of wanted) {
    if (id === 'follow') facets.follow = followFacet(matched);
    // The site's own URLs first, as the sidecar lists them.
    else if (id === 'to.isInternal') facets[id] = valueFacet(matched, id, ['true', 'false']);
    else facets[id] = valueFacet(matched, id, linkColumn(id)?.values);
  }
  return { ok: true, page: { matching: matched.length, total: rows.length, summary, unavailable: [], facets } };
}
