import type { Meta, StoryObj } from '@storybook/react-vite';
import type { QueryClient } from '@tanstack/react-query';
import { createMemoryHistory } from '@tanstack/react-router';
import { useEffect, useState } from 'react';
import type { GridFilter, GridQuery, LinkFacetsPage, LinkRow } from '../api/grids';
import { answerLinkFacets } from '../api/mock/linkGrid';
import { liveGridPagesOf } from '../api/queries';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { liveStore } from '../live/liveStore';
import { setDirectConnection } from '../shell/connection';
import { TEST_CRAWL_ID, fakeSidecar, json, type FakeRoute } from '../test/fakeSidecar';
import {
  LINKS_FACTS,
  LINKS_URLS,
  linksRoutes,
  testLinkRows,
  type LinksSidecarOptions,
  type LinksSidecarState,
} from '../test/linksSidecar';

const meta = { title: 'Screens/Links' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

/** The garden crawl in the design fixtures, as the mock sidecar serves it: the links around the URLs it holds. */
const GARDEN = '/crawls/62cb879ef60639d5/links';
/** The crawl of the stories' own sidecars: 13 links over 8 URLs, one of each thing the screen has words for. */
const LINKS = `/crawls/${TEST_CRAWL_ID}/links`;

const connect = () => setDirectConnection({ baseUrl: 'mock://sidecar', token: 'mock', generation: 1, version: 'mock' });
const fresh = () => liveStore.reset();
const withFilters = (path: string, filters: GridFilter[], rest = '') =>
  `${path}?filter=${encodeURIComponent(JSON.stringify(filters))}${rest}`;

/** What a story's sidecar was started with, for a story that changes it as it goes. */
let current: LinksSidecarState | null = null;

/** A sidecar of the story's own, for the states the design fixtures can't show. */
function own(options: LinksSidecarOptions = {}, first: FakeRoute[] = []) {
  fresh();
  const { routes, state } = linksRoutes(options);
  current = state;
  setTransport(fakeSidecar([...first, ...routes]));
  connect();
  return state;
}

function LinksApp({
  path,
  everyMs,
  tick,
}: {
  path: string;
  /** Runs `tick` this often: a crawl that stores more links, a refresh the user asked for. */
  everyMs?: number;
  tick?: (queryClient: QueryClient, state: LinksSidecarState | null, count: number) => void;
}) {
  const [app] = useState(() => {
    const queryClient: QueryClient = createQueryClient();
    return { queryClient, router: createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] })) };
  });
  useEffect(() => {
    if (everyMs === undefined || !tick) return undefined;
    let count = 0;
    const timer = setInterval(() => tick(app.queryClient, current, ++count), everyMs);
    return () => clearInterval(timer);
  }, [app, everyMs, tick]);
  return (
    <div className="h-screen">
      <App router={app.router} queryClient={app.queryClient} />
    </div>
  );
}

/** A promise that never settles: a read that's still on its way for as long as the story is looked at. */
const never = new Promise<void>(() => undefined);

/** The garden crawl over the mock: every link the fixtures hold, in crawl order. */
export const Garden: Story = {
  loaders: [fresh],
  render: () => <LinksApp path={GARDEN} />,
};

/** The "Broken links" preset on the garden crawl: its filter as a chip, the count it leaves. */
export const GardenBrokenLinks: Story = {
  loaders: [fresh],
  render: () => (
    <LinksApp path={withFilters(GARDEN, [{ column: 'to.statusClass', op: 'in', values: ['4xx', '5xx'] }])} />
  ),
};

/**
 * One of everything the grid has words for: a 404, a 500 and a redirect, a nofollow link, a link with no text, a
 * target robots.txt refused ("Blocked by robots.txt") and a link to a URL never fetched ("Not fetched").
 */
export const EveryKindOfLink: Story = {
  loaders: [() => own()],
  render: () => <LinksApp path={LINKS} />,
};

/** A link to a URL the crawl never fetched: no status, and no guess at one. The inspector says why. */
export const LinkToAUrlNeverFetched: Story = {
  loaders: [() => own()],
  render: () => <LinksApp path={withFilters(LINKS, [{ column: 'to.statusClass', op: 'isEmpty' }])} />,
};

/** Sorted by the target's status, most severe first, with more of the target's columns chosen. */
export const SortedByTargetStatus: Story = {
  loaders: [() => own()],
  render: () => (
    <LinksApp
      path={`${LINKS}?sort=${encodeURIComponent(JSON.stringify([{ column: 'to.statusCode', desc: true }]))}&cols=${encodeURIComponent(
        JSON.stringify(['from.url', 'to.url', 'text', 'to.statusCode', 'to.indexability', 'to.type', 'to.isInternal']),
      )}`}
    />
  ),
};

/** The first load: the frame from the start, then the skeleton once it has taken 150 ms. */
export const FirstLoad: Story = {
  loaders: [
    () => {
      const state = own();
      state.hold = never;
    },
  ],
  render: () => <LinksApp path={LINKS} />,
};

/** A refresh the user asked for: the rows stay, under the thin progress line. */
export const Refreshing: Story = {
  loaders: [() => own()],
  render: () => (
    <LinksApp
      path={LINKS}
      everyMs={600}
      tick={(queryClient, state, count) => {
        if (count !== 1 || !state) return;
        state.hold = never;
        void queryClient.invalidateQueries(liveGridPagesOf(TEST_CRAWL_ID));
      }}
    />
  ),
};

/** A link the running crawl stores, from a page it has just read. */
const arriving = (count: number): (typeof LINKS_FACTS)[number] => ({
  ...LINKS_FACTS[1]!,
  fromId: 8,
  toId: (count % 5) + 1,
  text: `Link ${count} from a page just read`,
});

/**
 * A crawl running: links arrive, the count and the facets move with them once a second, nothing jumps and the
 * cursor stays where it is. No skeleton and no progress line: the Live mark says it.
 */
export const Live: Story = {
  loaders: [
    () =>
      own({
        item: { status: 'running', analyzedAt: null, finishedAt: null },
        summary: { available: false, reason: 'crawling' },
      }),
  ],
  render: () => (
    <LinksApp
      path={LINKS}
      everyMs={1_000}
      tick={(queryClient, state, count) => {
        if (!state) return;
        const facts = [...LINKS_FACTS, ...Array.from({ length: count * 3 }, (_, index) => arriving(index + 1))];
        state.links = testLinkRows(facts, LINKS_URLS);
        void queryClient.invalidateQueries(liveGridPagesOf(TEST_CRAWL_ID), { cancelRefetch: false });
      }}
    />
  ),
};

/** A running crawl with a column that waits for the analysis: said once, with nothing to press. */
export const LiveWithAnAnalysisColumn: Story = {
  loaders: [
    () =>
      own({
        item: { status: 'running', analyzedAt: null, finishedAt: null },
        summary: { available: false, reason: 'crawling' },
      }),
  ],
  render: () => (
    <LinksApp
      path={`${LINKS}?cols=${encodeURIComponent(JSON.stringify(['from.url', 'to.url', 'text', 'to.inlinks']))}`}
    />
  ),
};

/** Never analysed: every link is here; what's slower and what's empty is said, with Analyse it. */
export const NeverAnalysed: Story = {
  loaders: [
    () => own({ item: { status: 'stopped', analyzedAt: null }, summary: { available: false, reason: 'notAnalyzed' } }),
  ],
  render: () => <LinksApp path={LINKS} />,
};

/** Continued after its last analysis: the same, with Analyse again. */
export const OutOfDate: Story = {
  loaders: [() => own({ item: { outOfDate: true }, summary: { available: false, reason: 'crawlChanged' } })],
  render: () => (
    <LinksApp
      path={`${LINKS}?cols=${encodeURIComponent(JSON.stringify(['from.url', 'to.url', 'text', 'to.inlinks']))}`}
    />
  ),
};

/** Analysed by an earlier version of the engine: the crawl's own columns work, the analysis's wait. */
export const EarlierEngine: Story = {
  loaders: [() => own({ item: { outOfDate: true }, summary: { available: false, reason: 'engineChanged' } })],
  render: () => (
    <LinksApp
      path={`${LINKS}?cols=${encodeURIComponent(JSON.stringify(['from.url', 'to.url', 'text', 'to.statusCode', 'to.inlinks', 'to.issues']))}`}
    />
  ),
};

/** A filter on a column the analysis fills, with no analysis: the engine's reason, and what can work. */
export const FilterNeedsAnAnalysis: Story = {
  loaders: [
    () => own({ item: { status: 'stopped', analyzedAt: null }, summary: { available: false, reason: 'notAnalyzed' } }),
  ],
  render: () => <LinksApp path={withFilters(LINKS, [{ column: 'to.inlinks', op: 'gte', value: 100 }])} />,
};

/** A crawl that stored no links at all. */
export const NoLinks: Story = {
  loaders: [() => own({ links: [], item: { fetchedUrlCount: 1, pageCount: 0 } })],
  render: () => <LinksApp path={LINKS} />,
};

/** A running crawl that hasn't stored a link yet. */
export const NoLinksYet: Story = {
  loaders: [
    () =>
      own({
        links: [],
        item: { status: 'running', analyzedAt: null, finishedAt: null },
        summary: { available: false, reason: 'crawling' },
      }),
  ],
  render: () => <LinksApp path={LINKS} />,
};

/** Filtered to nothing: what was asked for, and one press to clear it. */
export const NoLinksMatching: Story = {
  loaders: [() => own()],
  render: () => (
    <LinksApp
      path={withFilters(
        LINKS,
        [
          { column: 'to.statusClass', op: 'in', values: ['5xx'] },
          { column: 'position', op: 'in', values: ['footer'] },
        ],
        '&q=rattan',
      )}
    />
  ),
};

/**
 * A read that passes over every link (a search): after 150 ms the grid says what it's doing, over its skeleton, and
 * the rest of the screen stays usable. Another view gives the read up.
 */
export const SlowRead: Story = {
  loaders: [
    () => {
      const state = own();
      state.hold = never;
      state.holds = (query) => query.search !== undefined;
    },
  ],
  render: () => <LinksApp path={`${LINKS}?q=garden`} />,
};

/** The read failed: the plain cause, the detail behind Details, and Try again. */
export const ReadError: Story = {
  loaders: [
    () =>
      own({
        linksProblem: {
          type: 'internal',
          title: 'Something went wrong in the engine.',
          status: 500,
          detail: 'SQLite Error 14: unable to open database file.',
        },
        facetsFail: true,
      }),
  ],
  render: () => <LinksApp path={LINKS} />,
};

/** The engine's list of columns couldn't be read: the links are still here, and what can't be done says so. */
export const ColumnsUnreadable: Story = {
  loaders: [() => own({ columnsFail: true })],
  render: () => <LinksApp path={LINKS} />,
};

const LONG_TEXT =
  'Shop our complete range of rattan garden furniture, including corner sofas, dining sets, bistro sets, sun loungers and day beds, all with free delivery to mainland UK and a five-year guarantee against fading, cracking and unravelling. '.repeat(
    14,
  );
const LONG_URL = `https://example.com/garden-furniture/rattan/${'corner-sofa-sets-with-rising-tables-and-rain-covers/'.repeat(9)}?colour=natural-brown&utm_source=newsletter&utm_campaign=${'spring-'.repeat(30)}sale`;

/** An anchor text of 3,000 characters and URLs of 600: each cut to its cell, whole in its tooltip and when copied. */
export const HugeAnchorText: Story = {
  loaders: [
    () => {
      const urls = [...LINKS_URLS, { ...LINKS_URLS[1]!, id: 9, url: LONG_URL }];
      const facts = [
        { ...LINKS_FACTS[1]!, text: LONG_TEXT, toId: 9 },
        { ...LINKS_FACTS[1]!, fromId: 9, toId: 9, text: 'Unbroken'.repeat(120) },
        ...LINKS_FACTS,
      ];
      own({ urls, links: testLinkRows(facts, urls) });
    },
  ],
  render: () => <LinksApp path={LINKS} />,
};

const MILLIONS = 5_053_909;
const base = testLinkRows();

/** A page of a crawl of five million links, made as it's asked for: the grid scrolls all of it. */
function bigPage(query: GridQuery, matching: number) {
  const offset = query.offset ?? 0;
  const rows: LinkRow[] = Array.from({ length: Math.max(0, Math.min(query.limit, matching - offset)) }, (_, index) => ({
    ...base[(offset + index) % base.length]!,
    id: offset + index + 1,
  }));
  return {
    columns: ['id', ...(query.columns ?? [])],
    rows,
    offset,
    matching,
    total: MILLIONS,
    next: null,
    summary: { available: true, reason: null },
    unavailable: [],
  };
}

/** The facets of that crawl: the small crawl's shares, at five million links. */
function bigFacets(matching: number): LinkFacetsPage {
  const small = answerLinkFacets(base, {}, { available: true, reason: null });
  if (!small.ok) throw new Error('The facets couldn’t be counted.');
  const facets = Object.fromEntries(
    Object.entries(small.page.facets).map(([id, buckets]) => {
      const scaled = buckets.map((bucket) => ({
        ...bucket,
        count: Math.floor((bucket.count * matching) / base.length),
      }));
      const first = scaled[0];
      // The shares are rounded down: the first value takes what's left, so the counts add up to the links in view.
      if (first) first.count += matching - scaled.reduce((sum, bucket) => sum + bucket.count, 0);
      return [id, scaled];
    }),
  );
  return { ...small.page, matching, total: MILLIONS, facets };
}

/**
 * Seven-digit counts: the header's count, the facets' and the grid's own scroll space at five million rows, with
 * every page read as it comes into view.
 */
export const FiveMillionLinks: Story = {
  loaders: [
    () => {
      const matching = (query: { filters?: GridFilter[] }) => ((query.filters ?? []).length > 0 ? 4_446_616 : MILLIONS);
      own({ item: { site: '127.0.0.1', fetchedUrlCount: 110_251, pageCount: 101_107 } }, [
        [
          'POST',
          /^\/api\/crawls\/[0-9a-f]{16}\/links$/,
          (request) => json(bigPage(request.body as GridQuery, matching(request.body as GridQuery))),
        ],
        [
          'POST',
          /^\/api\/crawls\/[0-9a-f]{16}\/links\/facets$/,
          (request) => json(bigFacets(matching(request.body ?? {}))),
        ],
      ]);
    },
  ],
  render: () => <LinksApp path={withFilters(LINKS, [{ column: 'kind', op: 'in', values: ['hyperlink'] }])} />,
};
