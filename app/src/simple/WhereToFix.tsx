// "Where to fix it" (the UX audit of 2026-11-19, S-02 to S-04, S-07 and its section 4; the owner: "people are going
// to be probably more interested as to where the broken link is"): a problem that is a link, led by the page to edit.
// Each page once, its title and address one link, "Open page" always in sight and opening at the link's words; under
// it each link to change: what it says, where on the page it is, where it leads and what's wrong there. A link many
// pages share is one row, fixed once; an address nothing links to is in a short list of its own. The model and its
// words are fixPages.ts's; the reads are the lists' own (the check's rows and groups, where each is found, the links).
import { keepPreviousData, useQueries, useQuery } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import { ArrowUpRight, ChevronDown, Copy, ExternalLink, FileSpreadsheet } from 'lucide-react';
import { useMemo, useState } from 'react';
import type { CheckGroupRef, LinkRow as GridLink } from '../api/grids';
import type { IssueTargetsOfUrl, UrlFoundOn } from '../api/inspector';
import { foundOnQuery, issueTargetsQuery } from '../api/queries';
import type { Check, CrawlListItem } from '../api/types';
import { formatCount } from '../lib/format';
import { urlAtText } from '../lib/textFragment';
import { switchMode } from '../mode/switchMode';
import { SOURCES_SHOWN } from '../screens/issues/whereFound';
import { openInBrowser } from '../shell/os';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { OpenUrlButton } from '../ui/OpenUrlButton';
import { OpenLink } from './OpenLink';
import {
  SHARED_PAGES,
  addressesModel,
  deadEndWrong,
  findableWords,
  fixSummary,
  fixedOnce,
  linkWords,
  linksToChange,
  pagesModel,
  placeWords,
  sharedWhere,
  unlinkedWords,
  type FixLink,
  type FixModel,
  type FixPage,
  type FixShape,
  type FixTarget,
  type GroupPages,
  type SharedLink,
} from './fixPages';
import { plainAddress } from './plainEvidence';
import type { SimpleChanges } from './useSimpleChanges';
import { WHERE_MAX, WHERE_STEP, WhereItIs } from './WhereItIs';
import { groupsQuery, linksToQuery, rowsQuery } from './whereQueries';
import { CausesFixedSince } from './WhereItIsSince';
import { pathOrigin } from './whereWords';

/** The targets read of a page to find its link to a group's address: the engine lists at most 50 of a page's. */
const TARGETS_READ = 50;

interface FixRead {
  model: FixModel | null;
  /** What the check lists (its addresses, or the addresses its pages link to): how many are read, and how many there are. */
  listed: number;
  all: number | null;
  origin: string | null;
  error: unknown;
}

/** A check of dead addresses: its rows, where each is found, and the links to those a few pages link to. */
function useAddresses(
  crawlId: string,
  item: CrawlListItem,
  check: Check,
  limit: number,
  compare: 'previous' | null,
  enabled: boolean,
): FixRead {
  const rows = useQuery({
    ...rowsQuery(crawlId, check, null, limit, compare, ['statusCode']),
    enabled,
    placeholderData: keepPreviousData,
  });
  const listed = useMemo(() => rows.data?.rows ?? [], [rows.data]);
  const found = useQueries({
    queries: enabled ? listed.map((row) => foundOnQuery(crawlId, row.id, SOURCES_SHOWN)) : [],
    combine: (results) => ({
      ready: results.every((result) => result.data !== undefined || result.isError),
      // Its own count of entries is what changes as they're read: the map is rebuilt only then.
      read: results.filter((result) => result.data).length,
      all: results.map((result) => result.data ?? null),
    }),
  });
  const byId = useMemo(
    () => new Map(found.all.flatMap((entry): [number, UrlFoundOn][] => (entry ? [[entry.urlId, entry]] : []))),
    // eslint-disable-next-line react-hooks/exhaustive-deps -- `all` is a new array each render; `read` says when it changed
    [found.read, listed],
  );
  const few = useMemo(
    () =>
      listed
        .map((row) => row.id)
        .filter((id) => {
          const pages = byId.get(id)?.pages ?? 0;
          return pages > 0 && pages <= SHARED_PAGES;
        }),
    [listed, byId],
  );
  const links = useQuery({
    ...linksToQuery(crawlId, few),
    enabled: enabled && found.ready && few.length > 0,
    placeholderData: keepPreviousData,
  });
  const origin = pathOrigin(listed[0]?.url, item);
  const linkRows = useMemo(
    (): readonly GridLink[] | null => (few.length === 0 ? [] : (links.data?.rows ?? null)),
    [few, links.data],
  );
  const model = useMemo(
    () =>
      rows.data && found.ready && linkRows
        ? addressesModel(
            listed.map((row) => ({ ...row, wrong: deadEndWrong(check, row, origin) })),
            byId,
            linkRows,
          )
        : null,
    [rows.data, found.ready, linkRows, listed, byId, check, origin],
  );
  return { model, listed: listed.length, all: rows.data?.matching ?? null, origin, error: rows.error ?? links.error };
}

/** A check of pages holding a faulty link: its groups (the addresses), the pages of each, and each page's link to it. */
function usePages(
  crawlId: string,
  item: CrawlListItem,
  check: Check,
  limit: number,
  compare: 'previous' | null,
  enabled: boolean,
): FixRead {
  const groups = useQuery({
    ...groupsQuery(crawlId, check.id, limit, compare),
    enabled,
    placeholderData: keepPreviousData,
  });
  const listed = useMemo(() => groups.data?.groups ?? [], [groups.data]);
  const pagesOf = useQueries({
    queries: enabled
      ? listed.map((group) => {
          const ref: CheckGroupRef = { detail: group.detail, relatedUrlId: group.relatedUrlId };
          // A shared link's first pages say where on them it is; a link on a few pages lists them all.
          return rowsQuery(crawlId, check, ref, SHARED_PAGES);
        })
      : [],
    combine: (results) => ({
      ready: results.every((result) => result.data !== undefined || result.isError),
      read: results.filter((result) => result.data).length,
      all: results.map((result) => result.data?.rows ?? null),
    }),
  });
  const groupPages = useMemo(
    (): GroupPages[] => listed.map((group, index) => ({ group, rows: pagesOf.all[index] ?? [] })),
    // eslint-disable-next-line react-hooks/exhaustive-deps -- `all` is a new array each render; `read` says when it changed
    [listed, pagesOf.read],
  );
  const pageIds = useMemo(
    () => [...new Set(groupPages.flatMap(({ rows }) => rows.map((row) => row.id)))],
    [groupPages],
  );
  const targets = useQueries({
    queries:
      enabled && pagesOf.ready ? pageIds.map((id) => issueTargetsQuery(crawlId, check.id, id, TARGETS_READ)) : [],
    combine: (results) => ({
      ready: results.every((result) => result.data !== undefined || result.isError),
      read: results.filter((result) => result.data).length,
      all: results.map((result) => result.data ?? null),
    }),
  });
  const byPage = useMemo(
    () => new Map(targets.all.flatMap((entry): [number, IssueTargetsOfUrl][] => (entry ? [[entry.urlId, entry]] : []))),
    // eslint-disable-next-line react-hooks/exhaustive-deps -- as above
    [targets.read, pageIds],
  );
  const origin = pathOrigin(groupPages[0]?.rows[0]?.url ?? listed[0]?.relatedUrl, item);
  const model = useMemo(
    () => (groups.data && pagesOf.ready && targets.ready ? pagesModel(groupPages, byPage, origin) : null),
    [groups.data, pagesOf.ready, targets.ready, groupPages, byPage, origin],
  );
  return { model, listed: listed.length, all: groups.data?.groupCount ?? null, origin, error: groups.error };
}

/** Opens a page in the browser at a link's words, where it has some a browser can find. */
const openPage = (url: string, link: Pick<FixLink, 'kind' | 'text'> | undefined) =>
  void openInBrowser(urlAtText(url, link ? findableWords(link) : null));

export function WhereToFix({
  crawlId,
  item,
  check,
  shape,
  total,
  onCopy,
  onSave,
  changes,
}: {
  crawlId: string;
  item: CrawlListItem;
  check: Check;
  /** Which way round the check's data comes (fixPages' fixShape). */
  shape: FixShape;
  /** The check's count: the addresses or pages it flagged. */
  total: number;
  onCopy: () => void;
  onSave: () => void;
  /** The fix loop: what changed since the site's last check; a link that's new since then says so. */
  changes?: SimpleChanges | undefined;
}) {
  const router = useRouter();
  const [limit, setLimit] = useState(WHERE_STEP);
  const compare = changes?.comparison && changes.comparison.comparable !== 'countsOnly' ? 'previous' : null;
  const addresses = useAddresses(crawlId, item, check, limit, compare, shape === 'addresses');
  const pages = usePages(crawlId, item, check, limit, compare, shape === 'pages');
  const read = shape === 'addresses' ? addresses : pages;
  const { model, origin } = read;
  const all = read.all ?? total;
  const more = model !== null && read.listed < all;
  // A check of pages whose groups come back empty though it flagged pages (a file its reader can't group): the pages
  // list as they did, so a count is never over an empty list.
  if (shape === 'pages' && pages.all === 0 && total > 0) {
    return (
      <WhereItIs
        crawlId={crawlId}
        item={item}
        check={check}
        total={total}
        onCopy={onCopy}
        onSave={onSave}
        changes={changes}
      />
    );
  }
  return (
    <Card as="section" aria-labelledby="where-to-fix" id="the-list" className="si-fix mt-4 overflow-hidden">
      <div className="flex flex-wrap items-center gap-3 border-b border-border py-4 pr-4 pl-6">
        <h2 id="where-to-fix" className="text-16 font-semibold">
          Where to fix it
        </h2>
        {model ? <span className="text-14 text-text-muted">{fixSummary(model, more)}</span> : null}
        <div className="ml-auto flex flex-wrap gap-2">
          <Button size="sm" icon={Copy} command="problem.copyList" onClick={onCopy}>
            Copy the list
          </Button>
          <Button size="sm" icon={FileSpreadsheet} command="problem.saveSheet" onClick={onSave}>
            Save as a spreadsheet
          </Button>
        </div>
      </div>
      {read.error ? (
        <p className="px-6 py-4 text-14 text-text-secondary">The list couldn’t be read. Try again in a moment.</p>
      ) : model === null ? (
        <p className="px-6 py-4 text-14 text-text-muted" role="status">
          Reading where each one is…
        </p>
      ) : (
        <>
          {model.pages.length > 0 ? (
            <>
              <div className="si-fix-columns" aria-hidden="true">
                <span>The link that says</span>
                <span>Where on the page</span>
                <span>It leads to</span>
              </div>
              <ol aria-label="Pages to edit" className="si-fix-pages">
                {model.pages.map((page) => (
                  <PageToEdit key={page.id} page={page} origin={origin} />
                ))}
              </ol>
            </>
          ) : null}
          {model.shared.length > 0 ? (
            <section aria-labelledby="fix-shared" className="si-fix-part">
              <h3 id="fix-shared" className="si-fix-part__title">
                {model.pages.length > 0 ? 'Links many pages share' : 'Links your pages share'}
              </h3>
              <ul className="si-fix-pages">
                {model.shared.map((shared) => (
                  <SharedRow
                    key={shared.target.id}
                    crawlId={crawlId}
                    check={check}
                    shape={shape}
                    shared={shared}
                    origin={origin}
                  />
                ))}
              </ul>
            </section>
          ) : null}
          {model.unlinked.length > 0 ? (
            <section aria-labelledby="fix-unlinked" className="si-fix-part">
              <h3 id="fix-unlinked" className="si-fix-part__title">
                {model.unlinked.every((unlinked) => unlinked.where === 'sitemap')
                  ? 'Not linked from any page, and still in your sitemap'
                  : 'Not linked from any page'}
              </h3>
              <ul className="si-fix-pages">
                {model.unlinked.map((unlinked) => (
                  <li key={unlinked.target.id} className="si-fix-page">
                    <Leads target={unlinked.target} origin={origin} />
                    <p className="mt-1 text-14 text-text-secondary">
                      {unlinkedWords(unlinked)}{' '}
                      {unlinked.sitemaps.map((sitemap) => (
                        <OpenLink key={sitemap.id} url={sitemap.url} className="si-link si-link--inline mr-2">
                          {plainAddress(sitemap.url, origin)}
                        </OpenLink>
                      ))}
                      {(unlinked.forwards ?? []).map((url) => (
                        <OpenLink key={url} url={url} className="mr-2 font-mono text-13">
                          {plainAddress(url, origin)}
                        </OpenLink>
                      ))}
                    </p>
                  </li>
                ))}
              </ul>
            </section>
          ) : null}
        </>
      )}
      {changes ? <CausesFixedSince crawlId={crawlId} item={item} check={check} changes={changes} /> : null}
      {model ? (
        <div className="flex flex-wrap items-center gap-3 border-t border-border-subtle bg-surface-sunken px-6 py-3 text-14 text-text-secondary">
          <span>
            {more
              ? `Showing the first ${formatCount(read.listed)} of ${formatCount(all)} addresses.`
              : origin
                ? `Addresses starting with “/” are on ${origin.replace(/^https:\/\//, '')}.`
                : null}
          </span>
          {more && limit < WHERE_MAX ? (
            <Button
              variant="ghost"
              size="sm"
              icon={ChevronDown}
              onClick={() => setLimit((value) => value + WHERE_STEP)}
            >
              Show {formatCount(Math.min(WHERE_STEP, all - read.listed))} more
            </Button>
          ) : null}
          {more && limit >= WHERE_MAX ? (
            <Button variant="ghost" size="sm" iconEnd={ArrowUpRight} onClick={() => switchMode(router, 'advanced')}>
              See all {formatCount(all)} in Advanced
            </Button>
          ) : null}
        </div>
      ) : null}
    </Card>
  );
}

/** A page's name as its link: its title over its address, or its address alone. */
function PageName({
  url,
  title,
  origin,
  at,
}: {
  url: string;
  title: string | null;
  origin: string | null;
  at: string;
}) {
  const path = plainAddress(url, origin);
  return (
    <OpenLink url={at} hint="Open the page in your browser" className="si-fix-page__name">
      {title ? <span className="si-fix-page__title">{title}</span> : null}{' '}
      <span className={title ? 'si-fix-page__path' : 'si-fix-page__path si-fix-page__path--alone'}>
        <span aria-hidden="true">{path === '/' && !title ? 'Your home page' : path}</span>
        <span className="sr-only">{url}</span>
      </span>
    </OpenLink>
  );
}

/** Where a link leads: the address as a link that opens it, its open button after it, then what's wrong there. */
function Leads({ target, origin }: { target: FixTarget; origin: string | null }) {
  return (
    <>
      <OpenLink
        url={target.url}
        hint="Open this address in your browser"
        className="si-fix-leads__url"
        short={plainAddress(target.url, origin).length < 4}
      >
        <span aria-hidden="true">{plainAddress(target.url, origin)}</span>
        <span className="sr-only">{target.url}</span>
      </OpenLink>
      <OpenUrlButton url={target.url} className="ml-1" />
      {target.wrong ? <span className="si-fix-leads__wrong">{target.wrong}</span> : null}
      {target.isNew ? <span className="si-fix-new">New since the last check</span> : null}
    </>
  );
}

function PageToEdit({ page, origin }: { page: FixPage; origin: string | null }) {
  const first = page.links[0];
  // At the link's words where every link on the page says the same, or there is one: else the page's top.
  const words = first && page.links.every((link) => findableWords(link) === findableWords(first)) ? first : undefined;
  const at = urlAtText(page.url, words ? findableWords(words) : null);
  const name = page.title ?? plainAddress(page.url, origin);
  return (
    <li className="si-fix-page">
      <div className="si-fix-page__head">
        <PageName url={page.url} title={page.title} origin={origin} at={at} />
        <Button
          size="sm"
          iconEnd={ExternalLink}
          aria-label={`Open page: ${name}`}
          title={words && findableWords(words) ? 'Open the page at this link' : 'Open the page in your browser'}
          onClick={() => openPage(page.url, words)}
        >
          Open page
        </Button>
        <span className="si-fix-page__count">{linksToChange(page.links.length)}</span>
      </div>
      <table className="si-fix-links">
        <thead className="sr-only">
          <tr>
            <th scope="col">The link that says</th>
            <th scope="col">Where on the page</th>
            <th scope="col">It leads to</th>
          </tr>
        </thead>
        <tbody>
          {page.links.map((link) => (
            <tr key={`${link.target.id}|${link.kind ?? ''}|${link.text ?? ''}|${link.position ?? ''}`}>
              <td data-site-text>
                {findableWords(link) ? (
                  <OpenLink
                    url={urlAtText(page.url, findableWords(link))}
                    hint="Open the page at this link"
                    className="si-fix-words"
                  >
                    {linkWords(link)}
                  </OpenLink>
                ) : (
                  <span className="si-fix-words">{linkWords(link)}</span>
                )}
                {link.count > 1 ? <span className="text-text-muted"> · {formatCount(link.count)} times</span> : null}
              </td>
              <td className="text-text-secondary">{placeWords(link.position) ?? ''}</td>
              <td>
                <Leads target={link.target} origin={origin} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </li>
  );
}

/** The pages of a shared link shown when asked: this many. */
const SHARED_SHOWN = WHERE_MAX;

/** A link many pages share: where it is on them, what it says, where it leads, and its pages when asked. */
function SharedRow({
  crawlId,
  check,
  shape,
  shared,
  origin,
}: {
  crawlId: string;
  check: Check;
  shape: FixShape;
  shared: SharedLink;
  origin: string | null;
}) {
  const [open, setOpen] = useState(false);
  // Its pages: the links to it (a check of addresses), or the group's rows (a check of pages).
  const links = useQuery({
    ...linksToQuery(crawlId, [shared.target.id], SHARED_SHOWN),
    enabled: open && shape === 'addresses',
  });
  const rows = useQuery({
    ...rowsQuery(crawlId, check, { detail: null, relatedUrlId: shared.target.id }, SHARED_SHOWN),
    enabled: open && shape === 'pages',
  });
  const pages: { id: number; url: string; title: string | null; text: string | null }[] =
    shape === 'addresses'
      ? [
          ...new Map(
            (links.data?.rows ?? []).flatMap((link) => {
              const id = link['from.id'];
              const url = link['from.url'];
              if (typeof id !== 'number' || !url) return [];
              const title = link['from.title'];
              return [
                [id, { id, url, title: typeof title === 'string' && title ? title : null, text: link.text ?? null }],
              ];
            }),
          ).values(),
        ]
      : (rows.data?.rows ?? []).flatMap((row) =>
          row.url ? [{ id: row.id, url: row.url, title: row.title ?? null, text: shared.text }] : [],
        );
  const pending = open && (shape === 'addresses' ? links.isPending : rows.isPending);
  return (
    <li className="si-fix-page">
      <div className="si-fix-page__head">
        <span className="si-fix-page__name">
          <span className="si-fix-page__title">{sharedWhere(shared)}</span>
          <span className="block text-14 text-text-secondary">
            {fixedOnce(shared)
              ? 'It’s in a part of the site these pages share: fix it there once, in your site’s design or theme.'
              : 'The same link is on each of them.'}
          </span>
        </span>
        <Button size="sm" aria-expanded={open} onClick={() => setOpen((value) => !value)}>
          {open ? 'Hide the pages' : 'Show the pages'}
        </Button>
      </div>
      <table className="si-fix-links">
        <thead className="sr-only">
          <tr>
            <th scope="col">The link that says</th>
            <th scope="col">Where on the page</th>
            <th scope="col">It leads to</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td data-site-text>
              <span className="si-fix-words">{linkWords(shared)}</span>
            </td>
            <td className="text-text-secondary">{placeWords(shared.position) ?? ''}</td>
            <td>
              <Leads target={shared.target} origin={origin} />
            </td>
          </tr>
        </tbody>
      </table>
      {open ? (
        pending ? (
          <p className="mt-2 text-14 text-text-muted" role="status">
            Reading the pages…
          </p>
        ) : (
          <>
            <ul aria-label="Pages with this link" className="si-fix-shared-pages">
              {pages.map((page) => (
                <li key={page.id}>
                  <OpenLink
                    url={urlAtText(page.url, findableWords({ kind: shared.kind, text: page.text }))}
                    hint="Open the page in your browser"
                    className="si-fix-page__name"
                  >
                    {page.title ? <span className="font-medium">{page.title}</span> : null}{' '}
                    <span className="si-fix-page__path si-fix-page__path--inline">
                      <span aria-hidden="true">{plainAddress(page.url, origin)}</span>
                      <span className="sr-only">{page.url}</span>
                    </span>
                  </OpenLink>
                  <OpenUrlButton url={page.url} className="ml-1" />
                </li>
              ))}
            </ul>
            {shared.pages > pages.length ? (
              <p className="mt-2 text-14 text-text-secondary">
                The first {formatCount(pages.length)} of {formatCount(shared.pages)} pages. Save as a spreadsheet lists
                them all.
              </p>
            ) : null}
          </>
        )
      ) : null}
    </li>
  );
}
