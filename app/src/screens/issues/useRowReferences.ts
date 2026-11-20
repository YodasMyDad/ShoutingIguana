// The references of the rows a check's list has in view (A-01, A-06): for each URL, where it's found or what its page
// names, as the rows its list draws (the first in the URL's own row, the rest under it). The reads are the same ones a
// single row asks for (api/queries.ts), so the asks of one moment still go as one request.
import { useQueries } from '@tanstack/react-query';
import { useMemo } from 'react';
import { foundOnQuery, issueTargetsQuery } from '../../api/queries';
import type { Check } from '../../api/types';
import { SOURCES_SHOWN, foundOnRows, moreReferences, referencesOf, targetRows, type WhereRow } from './whereFound';

/** One URL's references: the first is its row's own, the rest open under it; `more` is how many it has past the first. */
export interface RowReferences {
  rows: readonly WhereRow[];
  more: number;
}

/** A URL's references couldn't be read. */
export const UNREAD: RowReferences = {
  rows: [{ kind: 'nothing', words: 'Where it’s found couldn’t be read' }],
  more: 0,
};

const NAMES_NOTHING: RowReferences = { rows: [{ kind: 'nothing', words: 'It names no other URL' }], more: 0 };

/**
 * A URL's rows less "Show the link in Links" where the rows already show every link: the way on is for the links
 * there's no room for here (the row menu still has it).
 */
function withoutEmptyWayOn(rows: WhereRow[]): WhereRow[] {
  const links = rows.filter((row) => row.kind === 'source').length;
  return rows.filter((row) => !(row.kind === 'all' && row.of === 'links' && row.total <= links));
}

/** The rows of an answer, made once for it: a row the grid has drawn keeps its identity. */
const made = new WeakMap<object, RowReferences>();

function once(answer: object, make: () => RowReferences): RowReferences {
  let references = made.get(answer);
  if (!references) {
    references = make();
    made.set(answer, references);
  }
  return references;
}

/**
 * The references of these URLs, by URL id: undefined while one is read, `UNREAD` where it couldn't be. Nothing is
 * asked for a check that refers to nothing.
 */
export function useRowReferences(
  crawlId: string,
  check: Pick<Check, 'id' | 'references' | 'targetFacts'>,
  urlIds: readonly number[],
  siteOrigin: string | null,
): ReadonlyMap<number, RowReferences> {
  const references = referencesOf(check);
  const found = useQueries({
    queries: (references === 'sources' ? urlIds : []).map((id) => foundOnQuery(crawlId, id, SOURCES_SHOWN)),
  });
  const named = useQueries({
    queries: (references === 'targets' ? urlIds : []).map((id) =>
      issueTargetsQuery(crawlId, check.id, id, SOURCES_SHOWN),
    ),
  });
  const byId = new Map<number, RowReferences>();
  urlIds.forEach((id, at) => {
    if (references === 'sources') {
      const result = found[at];
      if (result?.data) {
        const data = result.data;
        byId.set(
          id,
          once(data, () => ({
            rows: withoutEmptyWayOn(foundOnRows(data)),
            more: moreReferences({ foundOn: data, targets: null }),
          })),
        );
      } else if (result?.isError) byId.set(id, UNREAD);
    } else if (references === 'targets') {
      const result = named[at];
      if (result?.data) {
        const data = result.data;
        byId.set(
          id,
          once(data, () => ({
            rows: targetRows(data, check, siteOrigin),
            more: moreReferences({ foundOn: null, targets: data }),
          })),
        );
      } else if (result?.isError) byId.set(id, UNREAD);
      // A page the check didn't flag (the rows moved under an analysis): it names nothing.
      else if (result?.isSuccess) byId.set(id, NAMES_NOTHING);
    }
  });
  // The same map while no URL's answer changed, so what's memoised on it holds from one render to the next.
  const key = urlIds.map((id) => `${id}:${serialOf(byId.get(id))}`).join(',');
  // eslint-disable-next-line react-hooks/exhaustive-deps -- `key` says when an answer changed
  return useMemo(() => byId, [key]);
}

const serials = new WeakMap<RowReferences, number>();
let lastSerial = 0;

/** A number for each answer, the same for the same one: 0 for none yet. */
function serialOf(references: RowReferences | undefined): number {
  if (!references) return 0;
  let serial = serials.get(references);
  if (serial === undefined) {
    lastSerial += 1;
    serial = lastSerial;
    serials.set(references, serial);
  }
  return serial;
}
