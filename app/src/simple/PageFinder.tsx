// "What's wrong with this page?" (the UX audit of 2026-11-19, S-06): What to fix lists problems, and an owner about
// to edit one page couldn't ask what is wrong with it. A search by part of a page's address or title finds the page;
// picking it lists everything the check found on it, each a link to that problem. The search is the engine's (the
// one the palette types into); a page's problems are its own (the inspector's read).
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { ExternalLink, X } from 'lucide-react';
import { useEffect, useState } from 'react';
import { api } from '../api/client';
import type { SearchResult } from '../api/grids';
import { checksQuery, queryKeys, urlDetailQuery } from '../api/queries';
import type { Check, CrawlListItem } from '../api/types';
import { openInBrowser } from '../shell/os';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { SearchField } from '../ui/SearchField';
import { SeverityWord } from '../ui/SeverityWord';
import { OpenLink } from './OpenLink';
import { plainAddress, plainDetail } from './plainEvidence';
import { plainSummary, plainTitle } from './plainWords';
import { pathOrigin } from './whereWords';

/** The pages offered for what's typed. */
const PAGES_OFFERED = 8;
/** How long after the last key the search is asked. */
const TYPING_MS = 200;

const pagesQuery = (crawlId: string, text: string) => ({
  queryKey: [...queryKeys.crawl(crawlId), 'simple-page-search', text] as const,
  queryFn: ({ signal }: { signal: AbortSignal }) => api.search(crawlId, text, 50, signal),
  staleTime: Infinity,
});

/** An address that ends as a file does: an image, a style sheet, a script, a document. */
const FILE = /\.(?:jpe?g|png|gif|webp|avif|svg|ico|css|js|mjs|json|xml|txt|pdf|zip|woff2?|ttf|mp4|webm|mp3)(?:[?#]|$)/i;

/** A page as the finder names it: its title, else its address. */
const nameOf = (page: Pick<SearchResult, 'title' | 'url'>, origin: string | null) =>
  page.title ?? plainAddress(page.url, origin);

export function PageFinder({ crawlId, item }: { crawlId: string; item: CrawlListItem }) {
  const [text, setText] = useState('');
  const [asked, setAsked] = useState('');
  const [picked, setPicked] = useState<SearchResult | null>(null);
  useEffect(() => {
    const timer = setTimeout(() => setAsked(text.trim()), TYPING_MS);
    return () => clearTimeout(timer);
  }, [text]);
  const found = useQuery({
    ...pagesQuery(crawlId, asked),
    enabled: asked.length >= 2 && picked === null,
    placeholderData: keepPreviousData,
  });
  // The site's own pages: a file (an image, a script) or another site's address isn't a page to edit.
  const pages = (found.data?.results ?? [])
    .filter((page) => page.isInternal && (page.title !== null || !FILE.test(page.url)))
    .slice(0, PAGES_OFFERED);
  const origin = pathOrigin(picked?.url ?? pages[0]?.url, item);
  const searching = asked.length >= 2 && picked === null;
  return (
    <Card as="section" aria-labelledby="page-finder" className="mb-2 px-6 py-4">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <h2 id="page-finder" className="text-14 font-semibold">
          What’s wrong with one page?
        </h2>
        <div className="basis-80 min-w-0 flex-1">
          <SearchField
            label="Find a page by its address or title"
            placeholder="Type part of a page’s address or title"
            value={text}
            onChange={(event) => {
              setText(event.target.value);
              setPicked(null);
            }}
            onClear={() => {
              setText('');
              setPicked(null);
            }}
          />
        </div>
      </div>
      {picked ? (
        <PageProblems crawlId={crawlId} page={picked} origin={origin} onClose={() => setPicked(null)} />
      ) : searching && found.data ? (
        pages.length > 0 ? (
          <ul aria-label="Pages found" className="mt-3 flex flex-col">
            {pages.map((page) => (
              <li key={page.id}>
                <button type="button" className="si-page-pick" onClick={() => setPicked(page)}>
                  <span className="font-medium text-text">{nameOf(page, origin)}</span>
                  {page.title ? (
                    <span className="font-mono text-13 text-text-secondary">{plainAddress(page.url, origin)}</span>
                  ) : null}
                </button>
              </li>
            ))}
          </ul>
        ) : (
          <p className="mt-3 text-14 text-text-secondary" role="status">
            No page’s address or title has “{asked}” in it.
          </p>
        )
      ) : null}
    </Card>
  );
}

/** Everything the check found on one page, each a link to that problem; or that nothing was. */
function PageProblems({
  crawlId,
  page,
  origin,
  onClose,
}: {
  crawlId: string;
  page: SearchResult;
  origin: string | null;
  onClose: () => void;
}) {
  const detail = useQuery(urlDetailQuery(crawlId, page.id));
  const checks = useQuery(checksQuery()).data;
  const byId = new Map<string, Check>((checks?.checks ?? []).map((check) => [check.id, check]));
  // Each problem once, the most serious first, as the engine gives them.
  const issues = (detail.data?.issues ?? []).filter(
    (issue, index, all) =>
      byId.has(issue.checkId) && all.findIndex((other) => other.checkId === issue.checkId) === index,
  );
  const name = nameOf(page, origin);
  return (
    <div className="mt-3 border-t border-border-subtle pt-3">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
        <OpenLink url={page.url} hint="Open the page in your browser" className="min-w-0 flex-1">
          <span className="block text-16 font-semibold text-text">{name}</span>
          {page.title ? (
            <span className="block font-mono text-13 text-text-secondary">{plainAddress(page.url, origin)}</span>
          ) : null}
        </OpenLink>
        <Button
          size="sm"
          iconEnd={ExternalLink}
          aria-label={`Open page: ${name}`}
          onClick={() => void openInBrowser(page.url)}
        >
          Open page
        </Button>
        <Button size="sm" variant="ghost" icon={X} onClick={onClose}>
          Back to the pages found
        </Button>
      </div>
      {detail.isPending ? (
        <p className="mt-3 text-14 text-text-muted" role="status">
          Reading what was found on it…
        </p>
      ) : detail.error || detail.data.issues === null ? (
        <p className="mt-3 text-14 text-text-secondary">What was found on this page couldn’t be read.</p>
      ) : issues.length === 0 ? (
        <p className="mt-3 text-14 text-text-secondary" role="status">
          Nothing wrong was found on this page.
        </p>
      ) : (
        <>
          <p className="mt-3 text-14 text-text-secondary" role="status">
            {issues.length === 1
              ? '1 thing was found on this page:'
              : `${issues.length} things were found on this page:`}
          </p>
          <ul aria-label={`Found on ${name}`} className="mt-1 flex flex-col">
            {issues.map((issue) => {
              const check = byId.get(issue.checkId);
              if (!check) return null;
              const said = issue.detail ? (plainDetail(check, { detail: issue.detail }) ?? null) : null;
              return (
                <li key={issue.checkId}>
                  <Link
                    to="/crawls/$crawl/issues/{-$check}"
                    params={{ crawl: crawlId, check: check.id }}
                    className="si-page-pick"
                  >
                    <span className="flex flex-wrap items-center gap-2">
                      <SeverityWord severity={check.severity} variant="inline" />
                      <span className="font-medium text-text">{plainTitle(check)}</span>
                    </span>
                    <span className="text-14 text-text-secondary">{said ?? plainSummary(check)}</span>
                  </Link>
                </li>
              );
            })}
          </ul>
        </>
      )}
    </div>
  );
}
