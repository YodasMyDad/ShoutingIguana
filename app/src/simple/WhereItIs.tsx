import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import { ArrowUpRight, ChevronDown, Copy, FileSpreadsheet } from 'lucide-react';
import { useState } from 'react';
import { api } from '../api/client';
import type { CheckGroupRef, GridPage, IssueGroup, IssueGroupPage, IssueRow } from '../api/grids';
import { queryKeys } from '../api/queries';
import type { Check, CrawlListItem } from '../api/types';
import { reasonPhrase } from '../inspector/words';
import { formatCount } from '../lib/format';
import { switchMode } from '../mode/switchMode';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { OpenUrlButton } from '../ui/OpenUrlButton';
import { OneFixTag } from '../ui/PlainCheckRow';
import { evidenceOf } from '../screens/issues/evidenceColumns';
import { FoundOnWords } from './FoundOnWords';
import { saysShownAt, useShownAt } from '../screens/issues/useShownAt';
import { ImageThumb } from '../ui/ImageThumb';
import { plainEvidence, plainImageInsight, plainRelated } from './plainEvidence';
import { TargetWords } from './TargetWords';
import { pathOrigin, shownWords } from './whereWords';

/** Causes (or pages, for a check that doesn't group) shown at a time. */
export const WHERE_STEP = 25;
/** The most "Show 25 more" goes to: past it, the spreadsheet and Advanced. */
export const WHERE_MAX = 200;
/** A cause on many pages names this many first. */
const FIRST_PAGES = 3;

const ROW_COLUMNS = ['url', 'title', 'detail', 'relatedUrl', 'relatedStatusCode'];

/** What a check's rows carry here: the page's name and address, the detail, the other URL, and the check's evidence. */
function rowColumns(check: Pick<Check, 'evidence'>): string[] {
  return [...ROW_COLUMNS, ...evidenceOf(check).filter((id) => !ROW_COLUMNS.includes(id))];
}

const groupsQuery = (crawlId: string, checkId: string, limit: number) => ({
  queryKey: [...queryKeys.crawl(crawlId), 'issues', checkId, 'simple-groups', limit] as const,
  queryFn: ({ signal }: { signal: AbortSignal }): Promise<IssueGroupPage> =>
    api.issueGroups(crawlId, checkId, { offset: 0, limit }, signal),
  staleTime: Infinity,
});

const rowsQuery = (crawlId: string, check: Check, group: CheckGroupRef | null, limit: number) => ({
  // The check's evidence decides the columns: its id and its evidence are the key.
  queryKey: [...queryKeys.crawl(crawlId), 'issues', check.id, 'simple-rows', group, limit, rowColumns(check)] as const,
  queryFn: ({ signal }: { signal: AbortSignal }): Promise<GridPage<IssueRow>> =>
    api.issueRows(
      crawlId,
      check.id,
      { columns: rowColumns(check), filters: [], sort: [], offset: 0, limit, ...(group ? { group } : {}) },
      signal,
    ),
  staleTime: Infinity,
});

/**
 * A page's evidence in plain words, a line each, muted under what it's about: "The rules file says “Disallow:
 * /cart/”, on line 12", "Forwards to /b/, then /new/, which works". Nothing for a check without evidence.
 */
function EvidenceLines({ row, check, origin }: { row: IssueRow; check: Check; origin: string | null }) {
  const values = row as unknown as Record<string, unknown>;
  const lines = evidenceOf(check)
    .map((column) => plainEvidence(column, values[column], origin))
    .filter((line): line is string => line !== null);
  if (lines.length === 0) return null;
  return (
    <span className="si-evidence-lines mt-0.5 block text-13 text-text-secondary">
      {lines.map((line) => (
        <span key={line} className="block">
          {line}
        </span>
      ))}
    </span>
  );
}

/** An address as Where it is shows it: its path on the site's own origin, else in full; the full one is read. */
function Address({ url, origin, small = false }: { url: string; origin: string | null; small?: boolean }) {
  const shown = origin && url.startsWith(origin) ? url.slice(origin.length) || '/' : url;
  return (
    <span className={small ? 'block font-mono text-12 text-text-muted' : 'font-mono text-13'} title={url}>
      <span aria-hidden="true">{shown}</span>
      <span className="sr-only">{url}</span>
      <OpenUrlButton url={url} className="ml-1" />
    </span>
  );
}

/** A page: its title, then its address; an address alone when it has no title. */
function PageLine({ row, origin }: { row: IssueRow; origin: string | null }) {
  if (!row.url) return null;
  return row.title ? (
    <span className="block">
      <span className="block font-medium">{row.title}</span>
      <Address url={row.url} origin={origin} small />
    </span>
  ) : (
    <Address url={row.url} origin={origin} />
  );
}

function headings(check: Check): [string, string] {
  const pages = (check.subject ?? 'page') === 'page';
  if (check.grouping === 'none') return [pages ? 'Page' : 'Address', 'What was found'];
  return [
    check.grouping === 'relatedUrl' ? 'What it’s about' : 'What was found',
    pages ? 'On these pages' : 'At these addresses',
  ];
}

/**
 * Where it is (docs/v2-ui-simple.md, "One problem"): the check's causes as Advanced groups them, the cause on the left
 * and the pages it's on at the right, each page by its title and its address; a check that doesn't group lists its
 * pages with what was measured. 25 at a time up to 200; past that, the spreadsheet and Advanced.
 */
export function WhereItIs({
  crawlId,
  item,
  check,
  total,
  onCopy,
  onSave,
}: {
  crawlId: string;
  item: CrawlListItem;
  check: Check;
  /** The check's count: the URLs it flagged. */
  total: number;
  onCopy: () => void;
  onSave: () => void;
}) {
  const router = useRouter();
  const [limit, setLimit] = useState(WHERE_STEP);
  const grouped = check.grouping !== 'none';
  const groups = useQuery({
    ...groupsQuery(crawlId, check.id, limit),
    enabled: grouped,
    placeholderData: keepPreviousData,
  });
  const rows = useQuery({
    ...rowsQuery(crawlId, check, null, limit),
    enabled: !grouped,
    placeholderData: keepPreviousData,
  });
  // The first address the list holds says which of the site's origins its pages are on (www. or not).
  const sample = useQuery({ ...rowsQuery(crawlId, check, null, 1), enabled: grouped });
  const firstUrl = grouped
    ? (sample.data?.rows[0]?.url ?? groups.data?.groups[0]?.relatedUrl)
    : rows.data?.rows[0]?.url;
  const origin = pathOrigin(firstUrl, item);
  const [left, right] = headings(check);
  const pages = (check.subject ?? 'page') === 'page';
  const noun = (count: number) => (pages ? (count === 1 ? 'page' : 'pages') : count === 1 ? 'address' : 'addresses');
  const causes = grouped ? (groups.data?.groupCount ?? null) : null;
  const listed = grouped ? (groups.data?.groups.length ?? 0) : (rows.data?.rows.length ?? 0);
  const all = grouped ? (causes ?? 0) : total;
  const meta =
    causes === null
      ? `${formatCount(total)} ${noun(total)}`
      : `${causes === 1 ? '1 cause' : `${formatCount(causes)} causes`}, on ${formatCount(total)} ${noun(total)}`;
  const error = grouped ? groups.error : rows.error;
  // How many are shown, and the ways to more, only once the list is read: before then a grouped check's count of
  // causes isn't known, and "All 0 are shown" would be false.
  const read = (grouped ? groups.data : rows.data) !== undefined && !error;

  return (
    <Card as="section" aria-labelledby="where-it-is" className="mt-4 overflow-hidden">
      <div className="flex flex-wrap items-center gap-3 border-b border-border py-4 pr-4 pl-6">
        <h2 id="where-it-is" className="text-16 font-semibold">
          Where it is
        </h2>
        <span className="text-14 text-text-muted">{meta}</span>
        <div className="ml-auto flex flex-wrap gap-2">
          <Button size="sm" icon={Copy} command="problem.copyList" onClick={onCopy}>
            Copy the list
          </Button>
          <Button size="sm" icon={FileSpreadsheet} command="problem.saveSheet" onClick={onSave}>
            Save as a spreadsheet
          </Button>
        </div>
      </div>
      {error ? (
        <p className="px-6 py-4 text-14 text-text-secondary">The list couldn’t be read. Try again in a moment.</p>
      ) : (
        <table className="si-where-table">
          <thead>
            <tr>
              <th scope="col">{left}</th>
              <th scope="col">{right}</th>
            </tr>
          </thead>
          <tbody>
            {grouped
              ? (groups.data?.groups ?? []).map((group) => (
                  <GroupRow
                    key={`${group.detail ?? ''}|${group.relatedUrlId ?? ''}`}
                    crawlId={crawlId}
                    check={check}
                    group={group}
                    origin={origin}
                    noun={noun}
                  />
                ))
              : (rows.data?.rows ?? []).map((row) => (
                  <tr key={row.id}>
                    <td>
                      <PageLine row={row} origin={origin} />
                      <FoundOnWords crawlId={crawlId} check={check} urlId={row.id} origin={origin} />
                    </td>
                    <td className="text-text-secondary">
                      {row.detail ?? ''}
                      <EvidenceLines row={row} check={check} origin={origin} />
                    </td>
                  </tr>
                ))}
          </tbody>
        </table>
      )}
      {read ? (
        <div className="flex flex-wrap items-center gap-3 border-t border-border-subtle bg-surface-sunken px-6 py-3 text-14 text-text-secondary">
          <span>
            {shownWords({
              grouped,
              listed,
              all,
              noun,
              origin,
              shortened: origin !== null && (firstUrl ?? '').startsWith(origin),
            })}
          </span>
          {listed < all && limit < WHERE_MAX ? (
            <Button
              variant="ghost"
              size="sm"
              icon={ChevronDown}
              onClick={() => setLimit((value) => value + WHERE_STEP)}
            >
              Show {formatCount(Math.min(WHERE_STEP, all - listed))} more
            </Button>
          ) : null}
          {listed < all && limit >= WHERE_MAX ? (
            <Button variant="ghost" size="sm" iconEnd={ArrowUpRight} onClick={() => switchMode(router, 'advanced')}>
              See all {formatCount(all)} in Advanced
            </Button>
          ) : null}
        </div>
      ) : null}
    </Card>
  );
}

/** One cause: what it is at the left; its first pages at the right, with "Show them" for the rest. */
function GroupRow({
  crawlId,
  check,
  group,
  origin,
  noun,
}: {
  crawlId: string;
  check: Check;
  group: IssueGroup;
  origin: string | null;
  noun: (count: number) => string;
}) {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const count = group.urls ?? group.pages;
  const ref: CheckGroupRef = { detail: group.detail, relatedUrlId: group.relatedUrlId };
  const first = useQuery(rowsQuery(crawlId, check, ref, open ? WHERE_MAX : FIRST_PAGES));
  const rows = first.data?.rows ?? [];
  const status = rows[0]?.relatedStatusCode ?? null;
  const many = count > FIRST_PAGES;
  // Broken or redirected addresses say where each is linked from, and a page holding the cause how it has it (the
  // link's place and words, the size it shows the image at), and a page with evidence its own: the first few are
  // listed, not run together.
  const references = check.references ?? 'none';
  const perPage =
    references === 'sources' ||
    (references === 'targets' && (check.targetFacts ?? []).length > 0) ||
    evidenceOf(check).some((column) => column !== 'title');
  // What the cause is, in plain words: the image's file, where a forwarding address ends, how it answered. An
  // oversized image says the size its pages show it at, against the file's: how much smaller to make it.
  const shownAt = useShownAt(
    crawlId,
    check,
    group.relatedUrlId,
    rows.map((row) => row.id),
  );
  const about =
    saysShownAt(check) && group.related
      ? plainImageInsight(group.related, shownAt)
      : plainRelated(group.related, check, origin);
  // An image is known by sight before by its address.
  const image = check.grouping === 'relatedUrl' && check.id.startsWith('image-') && group.relatedUrl !== null;
  const broken =
    typeof group.related?.statusCode === 'number' ? group.related.statusCode >= 400 : Boolean(group.related?.outcome);
  // Each page's own facts in plain words: the engine's detail is Advanced's words, so these lines say it instead.
  const pageLines = (row: IssueRow) => (
    <>
      <PageLine row={row} origin={origin} />
      <FoundOnWords crawlId={crawlId} check={check} urlId={row.id} origin={origin} />
      <TargetWords crawlId={crawlId} check={check} urlId={row.id} cause={group.relatedUrlId} />
      <EvidenceLines row={row} check={check} origin={origin} />
    </>
  );
  return (
    <tr>
      <td>
        {group.sharedText ? (
          // What the pages share is the site's own words.
          <span className="block font-medium" data-site-text>
            “{group.sharedText}”
          </span>
        ) : group.relatedUrl ? (
          <span className={image ? 'flex items-start gap-3' : 'block'}>
            {image ? <ImageThumb url={group.relatedUrl} size="lg" broken={broken} /> : null}
            <span className="block min-w-0">
              {origin && (group.relatedUrl === origin || group.relatedUrl === `${origin}/`) ? (
                <span className="block font-medium">Your home page</span>
              ) : null}
              <Address url={group.relatedUrl} origin={origin} small={group.relatedUrl === `${origin ?? ''}/`} />
              {about ? (
                <span className="si-cause-facts mt-1 block text-13 text-text-secondary">{about}</span>
              ) : status !== null && status >= 400 ? (
                <span className="mt-1 block text-12 text-text-muted">
                  {status} {reasonPhrase(status) ?? ''}
                </span>
              ) : null}
            </span>
          </span>
        ) : (
          <span>{group.detail ?? ''}</span>
        )}
      </td>
      <td>
        {many ? (
          <span className="mb-1 flex flex-wrap items-center gap-2 font-medium">
            {formatCount(count)} {noun(count)}
            {group.likelyTemplate ? <OneFixTag /> : null}
          </span>
        ) : null}
        {many && !open && perPage ? (
          <>
            <ul className="flex flex-col gap-2">
              {rows.slice(0, FIRST_PAGES).map((row) => (
                <li key={row.id}>{pageLines(row)}</li>
              ))}
            </ul>
            <span className="mt-2 block text-text-secondary">
              And {formatCount(count - Math.min(rows.length, FIRST_PAGES))} more.{' '}
              <Button variant="ghost" size="sm" onClick={() => setOpen(true)}>
                Show them
              </Button>
            </span>
          </>
        ) : many && !open ? (
          <span className="text-text-secondary">
            {rows
              .slice(0, FIRST_PAGES)
              .map((row) => row.title ?? row.url ?? '')
              .join(', ')}{' '}
            and {formatCount(count - Math.min(rows.length, FIRST_PAGES))} more.{' '}
            <Button variant="ghost" size="sm" onClick={() => setOpen(true)}>
              Show them
            </Button>
          </span>
        ) : (
          <ul className="flex flex-col gap-2">
            {rows.map((row) => (
              <li key={row.id}>{pageLines(row)}</li>
            ))}
          </ul>
        )}
        {open && count > WHERE_MAX ? (
          <Button variant="ghost" size="sm" iconEnd={ArrowUpRight} onClick={() => switchMode(router, 'advanced')}>
            See all {formatCount(count)} in Advanced
          </Button>
        ) : null}
      </td>
    </tr>
  );
}
