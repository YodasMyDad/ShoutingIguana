// The URL grid's columns as the sidecar lists them (`GET /api/url-columns` at 46a2703), for the mock: their types,
// which need the URL summary, which sort and filter, and each enum's values. The operators each type takes are the
// contract's (docs/v2-sidecar-api.md, URLs).
import type { GridFilterOp, UrlColumn, UrlColumnType } from '../grids';

const column = (
  id: string,
  type: UrlColumnType,
  options: {
    needsAnalysis?: boolean;
    sortable?: boolean;
    filterable?: boolean;
    values?: string[];
    issueRowsOnly?: boolean;
  } = {},
): UrlColumn => ({
  id,
  type,
  needsAnalysis: options.needsAnalysis ?? false,
  // Every number, enum and boolean column sorts, and `url`; other text columns are filters only.
  sortable: options.sortable ?? (type !== 'text' || id === 'url'),
  filterable: options.filterable ?? true,
  values: options.values ?? [],
  issueRowsOnly: options.issueRowsOnly ?? false,
});

const analysed = { needsAnalysis: true };

export const URL_COLUMNS: readonly UrlColumn[] = [
  column('id', 'number'),
  column('url', 'text'),
  column('fetched', 'boolean'),
  column('isInternal', 'boolean'),
  column('source', 'enum', { values: ['seed', 'link', 'sitemap', 'siteVersion'] }),
  column('depth', 'number'),
  column('type', 'enum', { values: ['page', 'image', 'script', 'stylesheet', 'font', 'media', 'pdf', 'other'] }),
  column('statusCode', 'number'),
  column('statusClass', 'enum', { values: ['2xx', '3xx', '4xx', '5xx', 'other', 'noResponse'] }),
  // Not sortable at 46a2703, though the contract says every enum, boolean and number column sorts.
  column('outcome', 'enum', {
    sortable: false,
    values: [
      'success',
      'redirect',
      'clientError',
      'serverError',
      'blocked',
      'robotsDisallowed',
      'timeout',
      'networkError',
      'processingError',
      'notChecked',
    ],
  }),
  column('redirectTarget', 'text'),
  // Looked up per row: it filters, but doesn't sort.
  column('redirectTargetStatusCode', 'number', { sortable: false }),
  column('indexable', 'boolean'),
  column('indexability', 'enum', {
    values: [
      'none',
      'noindex',
      'canonicalised',
      'redirected',
      'clientError',
      'serverError',
      'blockedByRobots',
      'blocked',
      'unreachable',
    ],
  }),
  column('noindex', 'boolean', { sortable: false }),
  column('nofollow', 'boolean', { sortable: false }),
  // Read from each page's stored details: shown only.
  column('metaRobots', 'text', { filterable: false }),
  column('xRobotsTag', 'text', { filterable: false }),
  column('canonicalUrl', 'text'),
  column('contentType', 'text'),
  column('title', 'text'),
  column('description', 'text'),
  column('h1', 'text'),
  column('h1Count', 'number', { sortable: false }),
  column('wordCount', 'number'),
  column('outlinks', 'number'),
  column('sizeBytes', 'number'),
  column('responseMs', 'number'),
  column('ttfbMs', 'number', { sortable: false }),
  column('titleLength', 'number', analysed),
  column('descriptionLength', 'number', analysed),
  column('finalUrl', 'text', analysed),
  column('finalStatusCode', 'number', analysed),
  column('inSitemap', 'boolean', analysed),
  column('inlinks', 'number', analysed),
  column('followedInlinks', 'number', analysed),
  column('nofollowInlinks', 'number', analysed),
  column('linkingPages', 'number', analysed),
  column('issues', 'number', analysed),
  column('errors', 'number', analysed),
  column('warnings', 'number', analysed),
  column('notices', 'number', analysed),
  // A check's own columns: only its rows (`/issues/{checkId}/rows`) have them.
  column('detail', 'text', { sortable: true, issueRowsOnly: true }),
  column('relatedUrlId', 'number', { issueRowsOnly: true }),
  column('relatedUrl', 'text', { issueRowsOnly: true }),
  column('relatedStatusCode', 'number', { sortable: false, issueRowsOnly: true }),
];

/** The operators each column type takes. */
export const OPS_BY_TYPE: Record<UrlColumnType, readonly GridFilterOp[]> = {
  number: ['eq', 'ne', 'lt', 'lte', 'gt', 'gte', 'between', 'in', 'notIn', 'isEmpty', 'isNotEmpty'],
  enum: ['in', 'notIn', 'isEmpty', 'isNotEmpty'],
  text: ['contains', 'notContains', 'startsWith', 'endsWith', 'eq', 'ne', 'regex', 'notRegex', 'isEmpty', 'isNotEmpty'],
  boolean: ['is', 'isEmpty', 'isNotEmpty'],
};

/** A column by its id: a check's own columns only for its rows. */
export function urlColumn(id: string, issueRows = false): UrlColumn | undefined {
  return URL_COLUMNS.find((candidate) => candidate.id === id && (issueRows || !candidate.issueRowsOnly));
}
