// The reads behind a problem's list in Simple ("Where it is", "Where to fix it"): a check's causes as Advanced groups
// them, its rows (all of them, or one cause's), and the links to some addresses with the page each is on.
import { api } from '../api/client';
import type { CheckGroupRef, GridPage, IssueGroupPage, IssueRow } from '../api/grids';
import { queryKeys } from '../api/queries';
import type { Check } from '../api/types';
import { evidenceOf } from '../screens/issues/evidenceColumns';
import { LINK_COLUMNS } from './fixPages';
import { plainDetailColumn } from './plainEvidence';

const ROW_COLUMNS = ['url', 'title', 'detail', 'relatedUrl', 'relatedStatusCode'];

/** What a check's rows carry here: the page's name and address, the detail, the other URL, and the check's evidence. */
function rowColumns(check: Pick<Check, 'id' | 'evidence'>): string[] {
  // With the column its plain line is said from ("Only 16 characters long" is its `titleLength`): without it the line
  // fell back to the engine's detail.
  const plain = plainDetailColumn(check);
  const wanted = [...evidenceOf(check), ...(plain ? [plain] : [])];
  return [...ROW_COLUMNS, ...wanted.filter((id, index) => !ROW_COLUMNS.includes(id) && wanted.indexOf(id) === index)];
}

/** The fix loop: compared with the site's last check (each cause and page then gains its `change`), or not. */
export type Compare = 'previous' | null;

export const groupsQuery = (crawlId: string, checkId: string, limit: number, compare: Compare = null) => ({
  queryKey: [...queryKeys.crawl(crawlId), 'issues', checkId, 'simple-groups', limit, compare] as const,
  queryFn: ({ signal }: { signal: AbortSignal }): Promise<IssueGroupPage> =>
    api.issueGroups(crawlId, checkId, { offset: 0, limit, ...(compare ? { compareWith: compare } : {}) }, signal),
  staleTime: Infinity,
});

export const rowsQuery = (
  crawlId: string,
  check: Check,
  group: CheckGroupRef | null,
  limit: number,
  compare: Compare = null,
  /** Columns a list reads beside the check's own (Where to fix it: how each address answered). */
  extra: readonly string[] = [],
) => ({
  // The check's evidence decides the columns: its id and its evidence are the key.
  queryKey: [
    ...queryKeys.crawl(crawlId),
    'issues',
    check.id,
    'simple-rows',
    group,
    limit,
    [...rowColumns(check), ...extra],
    compare,
  ] as const,
  queryFn: ({ signal }: { signal: AbortSignal }): Promise<GridPage<IssueRow>> =>
    api.issueRows(
      crawlId,
      check.id,
      {
        columns: [...rowColumns(check), ...extra],
        filters: [],
        sort: [],
        offset: 0,
        limit,
        ...(group ? { group } : {}),
        ...(compare ? { compareWith: compare } : {}),
      },
      signal,
    ),
  staleTime: Infinity,
});

/** The links read of the addresses listed: enough for 25 addresses on ten pages each, several links a page. */
const LINKS_READ = 1_000;

/** The links to some addresses, with the page each is on: the links grid's rows, read once for the addresses listed. */
export const linksToQuery = (crawlId: string, targetIds: readonly number[], limit = LINKS_READ) => ({
  queryKey: [...queryKeys.crawl(crawlId), 'links', 'simple-fix', [...targetIds], limit] as const,
  queryFn: ({ signal }: { signal: AbortSignal }) =>
    api.links(
      crawlId,
      {
        columns: [...LINK_COLUMNS],
        filters: [{ column: 'to.id', op: 'in', values: [...targetIds] }],
        sort: [],
        offset: 0,
        limit,
      },
      signal,
    ),
  staleTime: Infinity,
});
