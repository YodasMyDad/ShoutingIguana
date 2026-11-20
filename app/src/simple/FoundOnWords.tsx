// Where an address a check flagged is found, in plain words, under it in "Where it is": "Linked from /garden/ (in the
// footer: “Old range”), /sofas/ and 3 more", or how the pages refer to it where they don't link to it: "Named as the
// main address by /a/" (an unlinked page, X7), "/ext-moved forwards here" (plainEvidence's linkedFromParts). Each page
// is a link that opens it, at the link's words where it has some, with its open button straight after it (the UX
// audit of 2026-11-19, S-02: the page that holds the link was text, and only the dead address opened); "and 3 more"
// lists the rest in place. An address no page links to says where it came from (S-07): its sitemap, as a link. The
// read is Advanced's (screens/issues/useWhereFound.ts), batched with the other addresses shown.
import { Fragment, useState } from 'react';
import type { Check } from '../api/types';
import { urlAtText } from '../lib/textFragment';
import { useWhereFoundOf } from '../screens/issues/useWhereFound';
import { SOURCES_SHOWN } from '../screens/issues/whereFound';
import { OpenUrlButton } from '../ui/OpenUrlButton';
import { OpenLink } from './OpenLink';
import { linkedFromParts, plainAddress } from './plainEvidence';

/** The pages named before "and N more". */
const FEW = 3;
/** The pages listed once the rest are asked for. */
const MORE = 50;

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
  const [all, setAll] = useState(false);
  const where = useWhereFoundOf(crawlId, check, urlId, all ? MORE : SOURCES_SHOWN);
  if (where.references !== 'sources' || !where.foundOn) return null;
  const found = where.foundOn;
  const parts = linkedFromParts(found, origin, all ? MORE : FEW);
  const className = 'si-found-on mt-0.5 block text-13 text-text-secondary';
  if (parts === null) return <span className={className}>No page that was checked links to it.</span>;
  if (parts.pages.length === 0) {
    // Where it came from, with the file to take it out of.
    return found.sitemaps.total > 0 ? (
      <span className={className}>
        No page links to it. It’s listed in your {found.sitemaps.total === 1 ? 'sitemap' : 'sitemaps'}:{' '}
        {found.sitemaps.sitemaps.map((sitemap, index) => (
          <Fragment key={sitemap.id}>
            {index > 0 ? ', ' : null}
            <OpenLink url={sitemap.url} hint="Open the sitemap in your browser">
              {plainAddress(sitemap.url, origin)}
            </OpenLink>
          </Fragment>
        ))}
      </span>
    ) : (
      <span className={className}>{parts.lead}</span>
    );
  }
  const last = parts.pages.length - 1;
  return (
    <span className={className}>
      {parts.lead ? `${parts.lead} ` : null}
      {parts.pages.map((page, index) => (
        <Fragment key={page.id}>
          {index > 0 ? (index === last && parts.more === null ? ' and ' : ', ') : null}
          <OpenLink
            url={urlAtText(page.url, page.words)}
            hint={page.words ? 'Open the page at this link' : 'Open the page in your browser'}
            className="font-mono"
            short={page.path.length < 4}
          >
            <span aria-hidden="true">{page.path}</span>
            <span className="sr-only">{page.url}</span>
          </OpenLink>
          <OpenUrlButton url={page.url} className="ml-0.5" />
          {page.place ? ` (${page.place})` : null}
        </Fragment>
      ))}
      {parts.more !== null ? (
        <>
          {' and '}
          {all ? (
            `${parts.more} more`
          ) : (
            <button type="button" className="si-link si-link--inline" onClick={() => setAll(true)}>
              {parts.more} more
            </button>
          )}
        </>
      ) : null}
      {parts.tail ? ` ${parts.tail}` : null}
    </span>
  );
}
