// A reference of a check's row, drawn as parts (rules 3 and 4 of the 2026-11-19 UX round): the other URL as a link
// with its open button, what the link says, and where on the page it is, each in a cell of its own. The words are
// whereFound.ts's; the list and the tree both draw their rows with these, so a page reads the same in either.
import { useNavigate } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import type { Check } from '../../api/types';
import { noStatusWords } from '../../crawl/evidence';
import { formatCount } from '../../lib/format';
import { StatusCode } from '../../ui/StatusCode';
import { TextLink } from '../../ui/TextLink';
import { linksOfUrl } from '../links/linkPlaces';
import { imagesOfPage } from '../links/presets';
import { IssueUrl } from './IssueUrl';
import { openAtLink, referencePlace, referenceRest, referenceText } from './referenceWords';
import { SAME_PAGE, allWords, referenceColumns, type WhereRow } from './whereFound';

/** What every reference cell of a check's table is drawn with. */
export interface ReferenceView {
  crawlId: string;
  check: Check;
  siteOrigin: string | null;
  onInspect: (urlId: number) => void;
}

/** The row's own URL: the one its references are of. */
export interface ReferenceSubject {
  id: number;
  url?: string | null | undefined;
}

/**
 * "+2 more", on a row with more references than the one it shows: pressed, it opens the rest under the row, and says
 * "Hide 2" while they show. It's the grid's own toggle (`data-tree-toggle`), so Right and Left do the same.
 */
export function MoreToggle({ count, open }: { count: number; open: boolean }) {
  if (count <= 0) return null;
  return (
    <span data-tree-toggle className="si-more-toggle" title={open ? 'Hide them' : 'Show them here'}>
      {open ? `Hide ${formatCount(count)}` : `+${formatCount(count)} more`}
    </span>
  );
}

/**
 * The other URL of a reference: the page that links to the row's URL (a link that inspects it, its open button opening
 * it at the link), the URL the row's page names with its status, the sitemap that lists it, or the way on to all of
 * them. `more` follows it: the row's "+2 more".
 */
export function ReferenceOther({
  where,
  subject,
  view,
  lead,
  more,
}: {
  where: WhereRow;
  subject: ReferenceSubject;
  view: ReferenceView;
  /** Words ahead of it, where its column's header isn't beside it (the tree's rows): "Linked from", "Links to". */
  lead?: string | undefined;
  more?: ReactNode;
}) {
  const { crawlId, check, siteOrigin, onInspect } = view;
  const navigate = useNavigate();
  const leading = lead ? <span className="shrink-0 text-text-muted">{lead}</span> : null;
  let body: ReactNode;
  switch (where.kind) {
    case 'source':
      body = (
        <>
          {leading}
          <IssueUrl
            url={where.link.url}
            siteOrigin={siteOrigin}
            onInspect={() => onInspect(where.link.id)}
            {...openAtLink(where.link.url, where.link.text)}
          />
        </>
      );
      break;
    case 'target':
      body = (
        <>
          {leading}
          {where.target.id === subject.id ? (
            <span className="min-w-0 truncate text-text-secondary">{SAME_PAGE}</span>
          ) : (
            <IssueUrl
              url={where.target.url}
              siteOrigin={siteOrigin}
              onInspect={() => onInspect(where.target.id)}
              label={where.target.kind === 'image' ? 'file' : 'address'}
            />
          )}
          <StatusCode
            code={where.target.statusCode}
            {...(where.target.statusCode === null ? { reason: noStatusWords(where.target.outcome) } : {})}
            className="shrink-0"
          />
        </>
      );
      break;
    case 'sitemap':
      body = (
        <>
          <span className="shrink-0 text-text-muted">Listed in the sitemap</span>
          <IssueUrl
            url={where.sitemap.url}
            siteOrigin={siteOrigin}
            goHint="Open in Sitemaps"
            onInspect={() =>
              void navigate({
                to: '/crawls/$crawl/sitemaps/{-$sitemap}',
                params: { crawl: crawlId, sitemap: String(where.sitemap.id) },
              })
            }
          />
        </>
      );
      break;
    case 'start':
      body = <span className="min-w-0 truncate text-text-muted">The URL the crawl started from</span>;
      break;
    case 'all':
      body =
        where.of === 'links' ? (
          <TextLink
            to="/crawls/$crawl/links"
            params={{ crawl: crawlId }}
            search={{ filter: linksOfUrl('inlinks', subject.id, 'all') }}
            tabIndex={-1}
            className="min-w-0 truncate"
          >
            {allWords(where)}
          </TextLink>
        ) : (
          <TextLink
            to="/crawls/$crawl/links"
            params={{ crawl: crawlId }}
            search={
              check.category === 'images'
                ? imagesOfPage(subject.id)
                : { filter: linksOfUrl('outlinks', subject.id, 'all') }
            }
            tabIndex={-1}
            className="min-w-0 truncate"
            title={
              check.category === 'images'
                ? 'The images this page shows, in Links'
                : 'Every link this page has, in Links'
            }
          >
            {allWords(where)}
          </TextLink>
        );
      break;
    case 'nothing':
      body = <span className="min-w-0 truncate text-text-muted">{where.words}</span>;
      break;
  }
  return (
    <span className="flex min-w-0 items-center gap-2" data-where={where.kind}>
      {body}
      {more}
    </span>
  );
}

/** What the link says, in quotes: the words to look for on the page. A link with none says so, quietly. */
export function ReferenceText({ where }: { where: WhereRow }) {
  if (where.kind !== 'source' && where.kind !== 'target') return null;
  const text = referenceText(where);
  const kind = where.kind === 'source' ? where.link.kind : where.target.kind;
  if (!text) {
    return (
      <span className="text-text-muted" title={kind === 'image' ? 'The image has no alt text' : 'The link has no text'}>
        {kind === 'image' ? 'No alt text' : kind === 'hyperlink' ? 'No text' : '–'}
      </span>
    );
  }
  return (
    <span className="block min-w-0 truncate" title={text}>
      {kind === 'image' ? <span className="text-text-muted">alt </span> : null}“{text}”
    </span>
  );
}

/** Where on the page the link is: the place to look for it. */
export function ReferencePlace({ where }: { where: WhereRow }) {
  const place = referencePlace(where);
  return place ? (
    <span className="block min-w-0 truncate text-text-secondary" title={place}>
      {place}
    </span>
  ) : null;
}

export function ReferenceRest({ where, view }: { where: WhereRow; view: Pick<ReferenceView, 'check' | 'siteOrigin'> }) {
  const rest = referenceRest(where, view);
  return rest ? (
    <span className="block min-w-0 truncate text-text-secondary" title={rest}>
      {rest}
    </span>
  ) : null;
}

/**
 * What a row says of a reference after its URL, where its columns' headers aren't above it (the tree's rows): what the
 * link says and where it is, as the check's list has them in columns, then whatever else the check says of it.
 */
export function ReferenceFacts({
  where,
  view,
}: {
  where: WhereRow;
  view: Pick<ReferenceView, 'check' | 'siteOrigin'>;
}) {
  if (where.kind === 'source') {
    return (
      <>
        <ReferenceText where={where} />
        <ReferencePlace where={where} />
      </>
    );
  }
  if (where.kind !== 'target') return null;
  const columns = referenceColumns(view.check);
  return (
    <>
      {columns?.text ? <ReferenceText where={where} /> : null}
      {columns?.place ? <ReferencePlace where={where} /> : null}
      <ReferenceRest where={where} view={view} />
    </>
  );
}
