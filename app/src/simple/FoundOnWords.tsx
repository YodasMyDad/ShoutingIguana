// Where to fix a broken or redirected address, in plain words, under it in "Where it is": "Linked from /garden/ (in
// the footer: “Old range”), /sofas/ and 3 more", or how the pages refer to it where they don't link to it: "Named as
// the main address by /a/" (an unlinked page, X7), "/ext-moved forwards here" (plainLinkedFrom). The read is
// Advanced's (screens/issues/useWhereFound.ts), batched with the other addresses shown.
import type { Check } from '../api/types';
import { useWhereFoundOf } from '../screens/issues/useWhereFound';
import { plainLinkedFrom } from './plainEvidence';

export function FoundOnWords({
  crawlId,
  check,
  urlId,
  origin,
}: {
  crawlId: string;
  check: Pick<Check, 'id' | 'references'>;
  urlId: number;
  origin: string | null;
}) {
  const where = useWhereFoundOf(crawlId, check, urlId);
  if (where.references !== 'sources') return null;
  const words = where.foundOn ? plainLinkedFrom(where.foundOn, origin) : null;
  if (!words) return null;
  return <span className="si-found-on mt-0.5 block text-13 text-text-secondary">{words}</span>;
}
