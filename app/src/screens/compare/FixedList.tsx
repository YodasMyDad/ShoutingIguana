import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { ChevronRight } from 'lucide-react';
import { useId, useState } from 'react';
import { api } from '../../api/client';
import type { ClearedGroup, ClearedIssue, FixedQuery } from '../../api/fixLoop';
import { fixedQuery, queryKeys } from '../../api/queries';
import type { Check } from '../../api/types';
import { clearedWords } from '../../changes/changeWords';
import { formatCount } from '../../lib/format';
import { Button } from '../../ui/Button';
import { Icon } from '../../ui/Icon';
import { OpenUrlButton } from '../../ui/OpenUrlButton';
import { Url } from '../../ui/Url';
import { periodLabel, type ComparedPeriod } from './changeCell';

const PAGE = 100;
const HEAD = 'border-b border-border py-1.5 pr-3 text-left text-12 font-medium whitespace-nowrap text-text-secondary';
const CELL = 'border-b border-border-subtle py-1.5 pr-3 align-middle';

/**
 * What a check no longer flags since the crawl compared with (docs/v2-fix-loop.md, Advanced, the Fixed tab): for a check
 * that groups, each cause it had then and has no URL of now, with its URLs then and how they moved, each opening its
 * URLs; for one that doesn't, each URL: Fixed, No longer linked (what others referred to), No longer found, or Not
 * checked this time with why, never counted as fixed. The detail then beside each, and the inspector for a URL the crawl
 * still has, or the browser.
 */
export function FixedList({
  crawlId,
  check,
  compareWith,
  period,
  siteOrigin,
  onInspect,
}: {
  crawlId: string;
  check: Pick<Check, 'id' | 'grouping' | 'references' | 'title'>;
  compareWith: string;
  period: ComparedPeriod;
  siteOrigin: string | null;
  onInspect: (urlId: number) => void;
}) {
  return check.grouping === 'none' ? (
    <FixedRows
      crawlId={crawlId}
      check={check}
      compareWith={compareWith}
      period={period}
      siteOrigin={siteOrigin}
      onInspect={onInspect}
    />
  ) : (
    <FixedGroups
      crawlId={crawlId}
      check={check}
      compareWith={compareWith}
      period={period}
      siteOrigin={siteOrigin}
      onInspect={onInspect}
    />
  );
}

type Props = Parameters<typeof FixedList>[0];

function FixedRows({
  crawlId,
  check,
  compareWith,
  period,
  siteOrigin,
  onInspect,
  group,
}: Props & { group?: FixedQuery['group'] }) {
  const query: FixedQuery = { compareWith, level: 'rows', limit: PAGE, ...(group ? { group } : {}) };
  const rows = useInfiniteQuery({
    queryKey: [...queryKeys.fixed(crawlId, check.id, query), 'pages'],
    queryFn: ({ pageParam, signal }) => api.fixed(crawlId, check.id, { ...query, offset: pageParam }, signal),
    initialPageParam: 0,
    getNextPageParam: (last) => {
      const next = last.offset + (last.rows?.length ?? 0);
      return last.total !== null && next < last.total ? next : undefined;
    },
  });
  if (rows.error) return <p className="text-13 text-text-muted">What it no longer flags couldn’t be read.</p>;
  if (rows.isPending) return <p className="text-13 text-text-muted">Reading what it no longer flags…</p>;
  const listed = rows.data.pages.flatMap((page) => page.rows ?? []);
  if (listed.length === 0) {
    return <p className="text-13 text-text-muted">Nothing it flagged {periodLabel(period).toLowerCase()} is gone.</p>;
  }
  return (
    <div className="flex flex-col gap-2">
      <table className="w-full border-separate border-spacing-0 text-13">
        <caption className="sr-only">{`${check.title}: what it no longer flags`}</caption>
        <thead>
          <tr>
            <th scope="col" className={`${HEAD} w-1/2`}>
              URL
            </th>
            <th scope="col" className={HEAD}>
              {periodLabel(period)}
            </th>
            <th scope="col" className={HEAD}>
              Detail then
            </th>
          </tr>
        </thead>
        <tbody>
          {listed.map((row) => (
            <FixedRow key={`${row.thenUrlId}`} row={row} check={check} siteOrigin={siteOrigin} onInspect={onInspect} />
          ))}
        </tbody>
      </table>
      {rows.hasNextPage ? (
        <div>
          <Button size="sm" loading={rows.isFetchingNextPage} onClick={() => void rows.fetchNextPage()}>
            Show {formatCount(PAGE)} more
          </Button>
        </div>
      ) : null}
    </div>
  );
}

function FixedRow({
  row,
  check,
  siteOrigin,
  onInspect,
}: {
  row: ClearedIssue;
  check: Pick<Check, 'references'>;
  siteOrigin: string | null;
  onInspect: (urlId: number) => void;
}) {
  const state = clearedWords(row.state, check.references, 'advanced');
  return (
    <tr>
      <td className={`${CELL} max-w-0`}>
        <span className="flex min-w-0 items-center gap-1">
          {row.urlId === null ? (
            <Url url={row.url} siteOrigin={siteOrigin} copyable={false} className="min-w-0 flex-1" />
          ) : (
            <button
              type="button"
              className="si-link min-w-0 flex-1 truncate text-left"
              title={`Inspect ${row.url}`}
              onClick={() => {
                if (row.urlId !== null) onInspect(row.urlId);
              }}
            >
              <Url url={row.url} siteOrigin={siteOrigin} copyable={false} tabbable={false} />
            </button>
          )}
          <OpenUrlButton url={row.url} />
        </span>
      </td>
      <td className={`${CELL} whitespace-nowrap`}>
        {row.state === 'notChecked' ? (
          <span title={row.reason?.text ?? undefined}>
            {state}
            {row.reason ? <span className="text-text-muted">: {row.reason.text}</span> : null}
          </span>
        ) : (
          state
        )}
      </td>
      <td className={`${CELL} text-text-secondary`}>{row.detailThen ?? '–'}</td>
    </tr>
  );
}

function FixedGroups(props: Props) {
  const { crawlId, check, compareWith, period } = props;
  const groups = useQuery(fixedQuery(crawlId, check.id, { compareWith, level: 'groups', limit: PAGE }));
  if (groups.error) return <p className="text-13 text-text-muted">What it no longer flags couldn’t be read.</p>;
  if (groups.isPending) return <p className="text-13 text-text-muted">Reading what it no longer flags…</p>;
  const listed = groups.data.groups ?? [];
  if (listed.length === 0) {
    return <p className="text-13 text-text-muted">No cause it had {periodLabel(period).toLowerCase()} is gone.</p>;
  }
  return (
    <ul className="flex flex-col">
      {listed.map((group) => (
        <FixedGroupRow key={`${group.detail ?? ''}|${group.relatedUrl ?? ''}`} group={group} {...props} />
      ))}
      {(groups.data.total ?? 0) > listed.length ? (
        <li className="pt-2 text-13 text-text-muted">
          And {formatCount((groups.data.total ?? 0) - listed.length)} more causes.
        </li>
      ) : null}
    </ul>
  );
}

/** A cause gone since then: what it was, its URLs then and how they moved, opening its URLs. */
function FixedGroupRow({ group, ...props }: Props & { group: ClearedGroup }) {
  const bodyId = useId();
  const [open, setOpen] = useState(false);
  const what = group.sharedText ?? group.relatedUrl ?? group.detail ?? 'Its URLs';
  const moved = [
    group.fixed > 0 ? `${formatCount(group.fixed)} fixed` : '',
    group.gone > 0
      ? `${formatCount(group.gone)} ${props.check.references === 'sources' ? 'no longer linked' : 'no longer found'}`
      : '',
    group.notChecked > 0 ? `${formatCount(group.notChecked)} not checked this time` : '',
  ].filter(Boolean);
  return (
    <li className="border-b border-border-subtle">
      <button
        type="button"
        aria-expanded={open}
        aria-controls={open ? bodyId : undefined}
        onClick={() => setOpen((shown) => !shown)}
        className="flex w-full items-center gap-2 py-2 text-left text-13 hover:bg-surface-hover"
      >
        <span className={open ? 'rotate-90 text-text-muted' : 'text-text-muted'}>
          <Icon icon={ChevronRight} size="sm" />
        </span>
        <span className="min-w-0 flex-1 truncate" title={what}>
          {what}
        </span>
        <span className="shrink-0 text-text-secondary tabular-nums">
          {formatCount(group.then)} {group.then === 1 ? 'URL' : 'URLs'} then
        </span>
        <span className="shrink-0 text-12 text-text-muted">{moved.join(' · ')}</span>
      </button>
      {open ? (
        <div id={bodyId} className="pb-3 pl-6">
          <FixedRows {...props} group={{ detail: group.detail, relatedUrl: group.relatedUrl }} />
        </div>
      ) : null}
    </li>
  );
}
