import { groupChangeWords, rowChangeWords } from '../changes/changeWords';
import type { SimpleChanges } from './useSimpleChanges';
import { CausesFixedSince } from './WhereItIsSince';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Link, useRouter } from '@tanstack/react-router';
import { ArrowUpRight, ChevronDown, Copy, FileSpreadsheet } from 'lucide-react';
import { useState } from 'react';
import type { CheckGroupRef, IssueGroup, IssueRow } from '../api/grids';
import type { Check, CrawlListItem } from '../api/types';
import { reasonPhrase } from '../inspector/words';
import { formatCount } from '../lib/format';
import { switchMode } from '../mode/switchMode';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { Icon } from '../ui/Icon';
import { OpenUrlButton } from '../ui/OpenUrlButton';
import { OneFixTag } from '../ui/PlainCheckRow';
import { evidenceOf } from '../screens/issues/evidenceColumns';
import { FoundOnWords } from './FoundOnWords';
import { OpenLink } from './OpenLink';
import { cutHeading, cutTextOf, settingNames, sitemapLine } from './developerNotes';
import { announce } from '../feedback/announce';
import { copyText } from '../lib/clipboard';
import { saysShownAt, useShownAt } from '../screens/issues/useShownAt';
import { ImageThumb } from '../ui/ImageThumb';
import { fileLabelOf } from '../ui/urlText';
import { isBrokenImage } from '../ui/imageFacts';
import { plainAddress, plainDetail, plainEvidence, plainImageInsight, plainRelated } from './plainEvidence';
import { TargetWords } from './TargetWords';
import { groupsQuery, rowsQuery, type Compare } from './whereQueries';
import { oneFixHolds, oneFixTitle } from './oneFixWords';
import {
  causeName,
  causeWords,
  causesWords,
  fileWords,
  pathOrigin,
  robotsOrigin,
  rowDetailSaysMore,
  shownWords,
  type FileWords,
} from './whereWords';

/** Causes (or pages, for a check that doesn't group) shown at a time. */
export const WHERE_STEP = 25;
/** The most "Show 25 more" goes to: past it, the spreadsheet and Advanced. */
export const WHERE_MAX = 200;
/** A cause on many pages names this many first. */
const FIRST_PAGES = 3;

/**
 * Whether one of a page's evidence columns earns its line (the UX audit of 2026-11-19, S-07 and S-17: "It’s a page",
 * "Checking it took 0.5 seconds" under a broken address, "Can appear in search results" and a count said twice help
 * nobody find or fix it). A file that isn't a page still says what it is ("It’s an image").
 */
function saysSomething(check: Pick<Check, 'category'>, column: string, value: unknown, columns: readonly string[]) {
  if (column === 'type') return value !== 'page';
  // How long a broken address took to fail isn't about the fault.
  if (column === 'responseMs') return check.category !== 'responseCodes';
  if (column === 'indexability') return value !== 'none';
  // "1 link to it" beside "1 page links here": the pages are what an owner counts.
  if (column === 'inlinks') return !columns.includes('linkingPages');
  return true;
}

/**
 * A page's evidence in plain words, a line each, muted under what it's about: "The rules file says “Disallow:
 * /cart/”, on line 12", "Forwards to /b/, then /new/, which works". Nothing for a check without evidence.
 */
function EvidenceLines({ row, check, origin }: { row: IssueRow; check: Check; origin: string | null }) {
  const values = row as unknown as Record<string, unknown>;
  // The row's own address, so it's never said to forward to itself or be its own main address (X1); a line two columns
  // say alike ("No pages link here") once.
  const columns = evidenceOf(check);
  const lines = columns
    .filter((column) => saysSomething(check, column, values[column], columns))
    .map((column) => plainEvidence(column, values[column], origin, row.url ?? null))
    .filter((line, index, all): line is string => line !== null && all.indexOf(line) === index);
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

/**
 * What was found on one page of a check that lists its pages: the engine's detail, unless the check says it plainly
 * ("Google will likely cut…"), then its evidence. A title or description too long is shown whole, the part Google cuts
 * greyed, with its length (S-13); a security check names its settings for whoever will add them (S-16).
 */
function Found({ row, check, origin }: { row: IssueRow; check: Check; origin: string | null }) {
  const values = row as unknown as Record<string, unknown>;
  const cut = cutTextOf(check, values);
  if (cut) {
    return (
      <>
        <span className="block text-text" data-site-text>
          “{cut.shown}
          <span className="si-cut-text" title="Google will likely cut this part off">
            {cut.cut}
          </span>
          ”
        </span>
        <span className="mt-1 block text-13">{cut.words}</span>
      </>
    );
  }
  const names = settingNames(check, row.detail);
  return (
    <>
      {plainDetail(check, values) ?? row.detail ?? ''}
      {names.length > 0 ? (
        <span className="mt-1 block text-13">
          Their names, for your developer or host:{' '}
          {names.map((name, index) => (
            <span key={name}>
              {index > 0 ? ', ' : null}
              <code className="font-mono text-text">{name}</code>
            </span>
          ))}
        </span>
      ) : null}
      <EvidenceLines row={row} check={check} origin={origin} />
    </>
  );
}

/** The fix loop: a cause's or a page's change since the site's last check, muted under it. */
function ChangeLine({ words }: { words: string }) {
  return <span className="mt-1 block text-12 text-text-muted">{words}</span>;
}

/** An address as Where it is shows it: its path on the site's own origin, else in full; the full one is read. */
function Address({
  url,
  origin,
  small = false,
  file = false,
  at,
  hint,
}: {
  url: string;
  origin: string | null;
  small?: boolean;
  /** The address its link opens, where that's the page at some of its words (lib/textFragment). */
  at?: string | undefined;
  hint?: string | undefined;
  /** A file a page loads (an image): named by its file, its folder or query after it, muted. */
  file?: boolean;
}) {
  const shown = origin && url.startsWith(origin) ? url.slice(origin.length) || '/' : url;
  const label = file ? fileLabelOf(url, origin) : null;
  return (
    <span className={small ? 'block font-mono text-12 text-text-muted' : 'font-mono text-13'}>
      {/* The address is the link (the UX fix round's rules 1 and 2); its open button sits straight after it. */}
      <OpenLink
        url={at ?? url}
        hint={hint ?? `Open ${file ? 'it' : 'this address'} in your browser`}
        short={!label && shown.length < 4}
      >
        {label ? (
          <span aria-hidden="true">
            <span className="si-file-name">{label.name}</span>
            {label.rest ? <span className="block text-12 text-text-muted">{label.rest}</span> : null}
          </span>
        ) : (
          <span aria-hidden="true">{shown}</span>
        )}
        <span className="sr-only">{url}</span>
      </OpenLink>
      <OpenUrlButton url={url} className="ml-1" />
    </span>
  );
}

/** A page: its title, then its address; an address alone when it has no title. */
function PageLine({ row, origin }: { row: IssueRow; origin: string | null }) {
  if (!row.url) return null;
  return row.title ? (
    <span className="block">
      {/* The page's title opens it too: the name is what an owner knows the page by. */}
      <OpenLink url={row.url} hint="Open the page in your browser" className="block font-medium">
        {row.title}
      </OpenLink>
      <Address url={row.url} origin={origin} small />
    </span>
  ) : (
    <Address url={row.url} origin={origin} />
  );
}

/** The table's headings: what its causes hold and the pages they're on, or a check's pages and what each has. */
function headings(check: Check): [string, string] {
  const pages = (check.subject ?? 'page') === 'page';
  // A text cut in search results is shown whole: the heading says once what its grey part is.
  const cut = cutHeading(check);
  if (cut) return ['Page', cut];
  if (check.grouping === 'none') return [pages ? 'Page' : 'Address', 'What was found'];
  const words = causeWords(check);
  return [words.heading, words.pagesHeading];
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
  changes,
  files = null,
}: {
  crawlId: string;
  item: CrawlListItem;
  check: Check;
  /** The check's count: the URLs it flagged. */
  total: number;
  onCopy: () => void;
  onSave: () => void;
  /** The fix loop: what changed since the site's last check; each cause and page says whether it's new or still there. */
  changes?: SimpleChanges | undefined;
  /**
   * X4: the site's files a check about its rules file or a sitemap names (whereWords' filesOf), listed where its home
   * page's row would be; null lists the row.
   */
  files?: readonly { url: string; sitemapId: number | null }[] | null | undefined;
}) {
  const router = useRouter();
  const [limit, setLimit] = useState(WHERE_STEP);
  const groupedCheck = check.grouping !== 'none';
  // Counts only (another start address, other rules): no cause or page can be matched, so none is marked.
  const compare: Compare = changes?.comparison && changes.comparison.comparable !== 'countsOnly' ? 'previous' : null;
  const since = changes?.since ?? null;
  const groups = useQuery({
    ...groupsQuery(crawlId, check.id, limit, compare),
    enabled: groupedCheck,
    placeholderData: keepPreviousData,
  });
  // Its groups, unless they come back empty though the check flagged addresses (a file its reader can't group): then the
  // addresses list on their own, so a count is never over an empty list (round 4).
  const grouped = groupedCheck && !(groups.data?.groupCount === 0 && total > 0);
  const rows = useQuery({
    ...rowsQuery(crawlId, check, null, limit, compare),
    enabled: !grouped,
    placeholderData: keepPreviousData,
  });
  // The first address the list holds says which of the site's origins its pages are on (www. or not).
  const sample = useQuery({ ...rowsQuery(crawlId, check, null, 1), enabled: grouped });
  const firstUrl = grouped
    ? (sample.data?.rows[0]?.url ?? groups.data?.groups[0]?.relatedUrl)
    : rows.data?.rows[0]?.url;
  const origin = pathOrigin(firstUrl, item);
  // X4: the files a site-wide check is about, named where its home page's row was (the issue stays on the home page).
  const named = files && check.about ? { files, words: fileWords(check.about) } : null;
  const [left, right] = named ? ['File', 'What was found'] : headings(check);
  const pages = (check.subject ?? 'page') === 'page';
  const noun = (count: number) => (pages ? (count === 1 ? 'page' : 'pages') : count === 1 ? 'address' : 'addresses');
  const causes = grouped ? (groups.data?.groupCount ?? null) : null;
  const listed = grouped ? (groups.data?.groups.length ?? 0) : (rows.data?.rows.length ?? 0);
  const all = grouped ? (causes ?? 0) : total;
  // What it counts, by what its causes are, on the pages the headline counts: "129 images on 60 pages".
  const fileNoun = (count: number) => (named ? (count === 1 ? named.words.one : named.words.many) : noun(count));
  const meta = named
    ? `${formatCount(named.files.length)} ${fileNoun(named.files.length)}`
    : causes === null
      ? `${formatCount(total)} ${noun(total)}`
      : causesWords(check, causes, total);
  const causeWord = causeWords(check);
  const cause = (count: number) => (count === 1 ? causeWord.one : causeWord.many);
  const error = grouped ? groups.error : rows.error;
  // How many are shown, and the ways to more, only once the list is read: before then a grouped check's count of
  // causes isn't known, and "All 0 are shown" would be false.
  const read = (grouped ? groups.data : rows.data) !== undefined && !error;
  // A check that lists its pages says what was found on each, unless nothing was: no detail and no evidence (the fixit
  // index audit's X5). Then the column goes, never a column of blanks. Kept while the pages are read.
  const saysFound =
    grouped ||
    rows.data === undefined ||
    evidenceOf(check).length > 0 ||
    rows.data.rows.some((row) => Boolean(plainDetail(check, row as unknown as Record<string, unknown>) ?? row.detail));

  return (
    <Card as="section" aria-labelledby="where-it-is" id="the-list" className="mt-4 overflow-hidden">
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
              {saysFound ? <th scope="col">{right}</th> : null}
            </tr>
          </thead>
          <tbody>
            {named ? (
              <FilesRow
                crawlId={crawlId}
                check={check}
                files={named.files}
                words={named.words}
                rows={rows.data?.rows ?? []}
                origin={origin}
                saysFound={saysFound}
              />
            ) : grouped ? (
              (groups.data?.groups ?? []).map((group) => (
                <GroupRow
                  key={`${group.detail ?? ''}|${group.relatedUrlId ?? ''}`}
                  crawlId={crawlId}
                  check={check}
                  group={group}
                  total={total}
                  origin={origin}
                  noun={noun}
                  since={since}
                />
              ))
            ) : (
              (rows.data?.rows ?? []).map((row) => (
                <tr key={row.id}>
                  <td>
                    <PageLine row={row} origin={origin} />
                    {/* Only what's new says so (S-17): "Still there since 17 Nov" on every row said nothing. */}
                    {row.change && (row.change.state === 'new' || row.change.state === 'newUrl') ? (
                      <ChangeLine words={rowChangeWords(row.change.state, since, 'simple')} />
                    ) : null}
                    <FoundOnWords crawlId={crawlId} check={check} urlId={row.id} origin={origin} />
                  </td>
                  {saysFound ? (
                    <td className="text-text-secondary">
                      <Found row={row} check={check} origin={origin} />
                    </td>
                  ) : null}
                </tr>
              ))
            )}
          </tbody>
        </table>
      )}
      {changes ? <CausesFixedSince crawlId={crawlId} item={item} check={check} changes={changes} /> : null}
      {read ? (
        <div className="flex flex-wrap items-center gap-3 border-t border-border-subtle bg-surface-sunken px-6 py-3 text-14 text-text-secondary">
          <span>
            {named
              ? shownWords({
                  grouped: false,
                  listed: named.files.length,
                  all: named.files.length,
                  noun: fileNoun,
                  origin,
                  shortened: origin !== null && named.files.some((file) => file.url.startsWith(origin)),
                })
              : shownWords({
                  grouped,
                  listed,
                  all,
                  noun,
                  cause,
                  origin,
                  shortened: origin !== null && (firstUrl ?? '').startsWith(origin),
                })}
          </span>
          {!named && listed < all && limit < WHERE_MAX ? (
            <Button
              variant="ghost"
              size="sm"
              icon={ChevronDown}
              onClick={() => setLimit((value) => value + WHERE_STEP)}
            >
              Show {formatCount(Math.min(WHERE_STEP, all - listed))} more
            </Button>
          ) : null}
          {!named && listed < all && limit >= WHERE_MAX ? (
            <Button variant="ghost" size="sm" iconEnd={ArrowUpRight} onClick={() => switchMode(router, 'advanced')}>
              See all {formatCount(all)} in Advanced
            </Button>
          ) : null}
        </div>
      ) : null}
    </Card>
  );
}

/**
 * X4: a check's files where its home page's row would be: each named ("Your sitemap") over its address, with the way to
 * it in Advanced (a sitemap the check read, by its id; a rules file, by its origin), and what was found at the right.
 * Neither the home page's title nor how it's linked: the files are what the check is about.
 */
function FilesRow({
  crawlId,
  check,
  files,
  words,
  rows,
  origin,
  saysFound,
}: {
  crawlId: string;
  check: Check;
  files: readonly { url: string; sitemapId: number | null }[];
  words: FileWords;
  /** The check's rows (the home page's, once for the site): what was found. */
  rows: readonly IssueRow[];
  origin: string | null;
  saysFound: boolean;
}) {
  const found = rows
    .map((row) => ({ row, said: plainDetail(check, row as unknown as Record<string, unknown>) ?? row.detail ?? '' }))
    .filter(({ said }, index, all) => all.findIndex((other) => other.said === said) === index);
  return (
    <tr>
      <td>
        <ul className="flex flex-col gap-2">
          {files.map((file) => (
            <li key={file.url}>
              <span className="block font-medium">{words.label}</span>
              <Address url={file.url} origin={origin} small />
              <FileInAdvanced crawlId={crawlId} check={check} file={file} named={files.length > 1} />
            </li>
          ))}
        </ul>
      </td>
      {saysFound ? (
        <td className="text-text-secondary">
          {found.map(({ row, said }) => (
            <span key={row.id} className="block">
              {said}
              <EvidenceLines row={row} check={check} origin={origin} />
              <LineToAdd line={sitemapLine(check, row.detail)} />
            </span>
          ))}
        </td>
      ) : null}
    </tr>
  );
}

/** The line a file needs, to copy into it (S-16: the rules file's row said a line was missing and didn't give it). */
function LineToAdd({ line }: { line: string | null }) {
  if (!line) return null;
  return (
    <span className="mt-2 block text-text">
      <span className="block text-13 text-text-secondary">Add this line to it:</span>
      <span className="mt-1 flex flex-wrap items-center gap-2">
        <code className="rounded-sm bg-surface-sunken px-2 py-1 font-mono text-13">{line}</code>
        <Button
          size="sm"
          icon={Copy}
          onClick={() =>
            void copyText(line).then((copied) => announce(copied ? 'Copied the line' : 'The line couldn’t be copied'))
          }
        >
          Copy the line
        </Button>
      </span>
    </span>
  );
}

/**
 * "See it in Advanced" for a file (docs/v2-ui-simple.md, The switch: Simple's links to an Advanced-only screen say so,
 * and arriving there turns Advanced on): robots.txt by the file's origin, a sitemap by its id. Nothing for a sitemap the
 * check didn't read, which has no page there. With more than one file, each link's name says which.
 */
function FileInAdvanced({
  crawlId,
  check,
  file,
  named,
}: {
  crawlId: string;
  check: Check;
  file: { url: string; sitemapId: number | null };
  named: boolean;
}) {
  const content = (
    <>
      See it in Advanced
      {named ? <span className="sr-only"> ({file.url})</span> : null}
      <Icon icon={ArrowUpRight} size="sm" />
    </>
  );
  const className = 'si-link mt-1 inline-flex items-center gap-1 text-13';
  if (check.about === 'robotsTxt') {
    return (
      <Link
        to="/crawls/$crawl/robots/{-$origin}"
        params={{ crawl: crawlId, origin: robotsOrigin(file.url) }}
        className={className}
      >
        {content}
      </Link>
    );
  }
  if (file.sitemapId === null) return null;
  return (
    <Link
      to="/crawls/$crawl/sitemaps/{-$sitemap}"
      params={{ crawl: crawlId, sitemap: String(file.sitemapId) }}
      className={className}
    >
      {content}
    </Link>
  );
}

/** One cause: what it is at the left; its first pages at the right, with "Show them" for the rest. */
function GroupRow({
  crawlId,
  check,
  group,
  total,
  origin,
  noun,
  since,
}: {
  crawlId: string;
  check: Check;
  group: IssueGroup;
  /** The check's count: One fix clears the problem only where the cause is on all of them. */
  total: number;
  origin: string | null;
  noun: (count: number) => string;
  /** The fix loop: when the site's last check was, for the cause's "Still there since 3 Nov". */
  since: string | null;
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
    evidenceOf(check).some((column) => column !== 'title') ||
    rows.some((row) => rowDetailSaysMore(check, group, row));
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
  const broken = isBrokenImage(group.related ?? {});
  // Each page's own facts in plain words: the engine's detail is Advanced's words, so these lines say it instead, but
  // where the detail says more than they do (the codes that disagree, the instance of the fault), it says it too.
  const pageLines = (row: IssueRow) => (
    <>
      <PageLine row={row} origin={origin} />
      <FoundOnWords crawlId={crawlId} check={check} urlId={row.id} origin={origin} />
      <TargetWords crawlId={crawlId} check={check} urlId={row.id} cause={group.relatedUrlId} />
      {rowDetailSaysMore(check, group, row) ? (
        <span className="si-row-detail mt-0.5 block text-13 text-text-secondary">
          {plainDetail(check, row as unknown as Record<string, unknown>) ?? row.detail}
        </span>
      ) : null}
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
              <Address
                url={group.relatedUrl}
                origin={origin}
                small={group.relatedUrl === `${origin ?? ''}/`}
                file={image}
              />
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
          // Its detail, said plainly; one the engine leaves empty is named by the check ("No main heading").
          <span>{causeName(check, group.detail)}</span>
        )}
        {group.change?.state === 'new' ? (
          <ChangeLine words={groupChangeWords(group.change, group.urls ?? group.pages, since, 'simple')} />
        ) : null}
      </td>
      <td>
        {many ? (
          <span className="mb-1 flex flex-wrap items-center gap-2 font-medium">
            {formatCount(count)} {noun(count)}
            {/* One fix, where it clears the whole problem; else how much of it one fix covers. */}
            {group.likelyTemplate && oneFixHolds(check) && count >= total ? <OneFixTag /> : null}
            {group.likelyTemplate && oneFixHolds(check) && count < total ? (
              <span className="text-13 font-regular text-text-secondary">
                {oneFixTitle(count, total, (check.subject ?? 'page') === 'page' ? 'pages' : 'addresses')}
              </span>
            ) : null}
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
            {/* Each page named is a link that opens it (S-21: the names were words). */}
            {rows.slice(0, FIRST_PAGES).map((row, index) => (
              <span key={row.id}>
                {index > 0 ? ', ' : null}
                {row.url ? (
                  <OpenLink url={row.url} hint="Open the page in your browser">
                    {row.title ?? plainAddress(row.url, origin)}
                  </OpenLink>
                ) : (
                  (row.title ?? '')
                )}
              </span>
            ))}{' '}
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
