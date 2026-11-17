// The redirect chain, hop by hop (docs/v2-sidecar-api.md, The inspector: `redirects`). The engine lists where each
// step arrives (`hops`, the last being `final`); the URL itself isn't among them, so the layout puts it first with its
// own answer. A hop's `via` is how the chain reached it. Loops and chains past the engine's 20 hops are called out.
import type { UrlOutcome } from '../api/grids';
import type { UrlDetail } from '../api/inspector';
import { formatCountOf } from '../lib/format';
import { fetchState } from './fetchState';
import { noStatusWords } from './words';

export interface ChainStep {
  /** A key that's unique in the chain, a loop's repeat included. */
  key: string;
  id: number;
  url: string;
  statusCode: number | null;
  outcome: UrlOutcome | null;
  /** What to say where there's no status code: how it went instead, as far as the crawl knows. Null with a code. */
  noCode: string | null;
  /** How the chain reached it: null for the URL itself. */
  via: 'http' | 'metaRefresh' | null;
  role: 'start' | 'hop' | 'end';
  /** It's the URL the inspector shows. */
  current: boolean;
  /** It was already in the chain: where a loop closes. */
  repeat: boolean;
}

export interface ChainLayout {
  steps: ChainStep[];
  /** Hops followed: 0 for a URL that doesn't redirect. */
  hops: number;
  loop: boolean;
  tooLong: boolean;
  /** The chain's end is somewhere that works: it answered 2xx. */
  resolves: boolean;
  /** "Redirects once", "Redirects 3 times", "Doesn’t redirect". */
  title: string;
  /** What to know about where it ends, in a sentence. */
  summary: string;
}

type Subject = Pick<UrlDetail, 'id' | 'url' | 'type' | 'discovery' | 'response' | 'content' | 'redirects'>;

/** A step without a status code: what became of it, in the inspector's words for the URL itself (words.ts). */
const noCodeWords = (statusCode: number | null, outcome: UrlOutcome | null) =>
  statusCode !== null ? null : noStatusWords(outcome);

/** Where a chain ends when its last URL has no status: only what its outcome says, never a guess. */
function endWithoutCode(outcome: UrlOutcome | null): string {
  if (outcome === 'robotsDisallowed') {
    return 'The chain ends at a URL robots.txt disallows, so the crawl didn’t request it.';
  }
  if (outcome === 'timeout' || outcome === 'networkError') return 'The chain ends at a URL that didn’t answer.';
  if (outcome === 'blocked') return 'The chain ends at a URL whose server refused the crawler.';
  if (outcome === 'notChecked') {
    return 'The chain ends at a URL the crawl couldn’t check: its host had stopped answering, asked the crawler to slow down, or is on a private network.';
  }
  // Queued or left alone: a hop comes without its state, so only that it wasn't fetched.
  if (outcome === null) return 'The chain ends at a URL the crawl didn’t fetch.';
  return 'The chain ends at a URL the crawl has no answer from.';
}

const times = (count: number) => (count === 1 ? 'once' : count === 2 ? 'twice' : formatCountOf(count, 'time'));

/**
 * The chain as steps: the URL itself, then each hop, the last where it ends. `crawling`: the crawl is running, for
 * what a URL it hasn't reached says (fetchState).
 */
export function redirectChainLayout(subject: Subject, crawling: boolean): ChainLayout {
  const { hops, loop, tooLong } = subject.redirects;
  const seen = new Set<number>([subject.id]);
  const ownCode = subject.response?.statusCode ?? null;
  const ownOutcome = subject.response?.outcome ?? null;
  const steps: ChainStep[] = [
    {
      key: `0-${subject.id}`,
      id: subject.id,
      url: subject.url,
      statusCode: ownCode,
      outcome: ownOutcome,
      // The URL itself: no response stored is not fetched, and its own state says whether yet (fetchState).
      noCode:
        ownCode !== null
          ? null
          : subject.response === null
            ? (fetchState(subject, crawling).title ?? 'Not fetched')
            : noCodeWords(ownCode, ownOutcome),
      via: null,
      role: hops.length === 0 ? 'end' : 'start',
      current: true,
      repeat: false,
    },
  ];
  hops.forEach((hop, index) => {
    const repeat = seen.has(hop.id);
    seen.add(hop.id);
    steps.push({
      key: `${index + 1}-${hop.id}`,
      id: hop.id,
      url: hop.url,
      statusCode: hop.statusCode,
      outcome: hop.outcome,
      // A hop comes with its status and outcome: with neither, it wasn't fetched (yet, while the crawl runs).
      noCode: noCodeWords(hop.statusCode, hop.outcome),
      via: hop.via,
      // A chain too long to follow has no end the crawl saw: its last hop is only as far as the crawl went.
      role: index === hops.length - 1 && !tooLong ? 'end' : 'hop',
      current: hop.id === subject.id,
      repeat,
    });
  });

  const end = steps[steps.length - 1];
  const endCode = end?.statusCode ?? null;
  const resolves = !loop && !tooLong && endCode !== null && endCode >= 200 && endCode < 300;
  const count = hops.length;

  if (count === 0) {
    const state = fetchState(subject, crawling);
    // A URL that never answered (not fetched, blocked by robots.txt, unreachable) can't be said not to redirect.
    if (!state.answered && state.title !== null && state.why !== null) {
      return {
        steps,
        hops: 0,
        loop,
        tooLong,
        resolves,
        title: state.title,
        summary: `${state.why} So the crawl doesn’t know whether it redirects.`,
      };
    }
    return {
      steps,
      hops: 0,
      loop,
      tooLong,
      resolves,
      title: 'Doesn’t redirect',
      summary: 'This URL answers for itself: nothing sends it on.',
    };
  }
  const title = `Redirects ${times(count)}`;
  let summary: string;
  if (loop) summary = 'The redirects come back on themselves, so they never reach a page.';
  else if (tooLong) summary = `The chain goes on past ${count} hops, further than the crawl follows.`;
  else if (endCode === null) summary = endWithoutCode(end?.outcome ?? null);
  else if (endCode >= 300 && endCode < 400) summary = 'The chain stops at a redirect the crawl couldn’t follow.';
  else if (!resolves) summary = `The chain ends at a URL that answers ${endCode}.`;
  else if (count > 1) summary = `It reaches a working page after ${count} hops: link straight to the last one.`;
  else summary = 'It reaches a working page in one hop.';
  return { steps, hops: count, loop, tooLong, resolves, title, summary };
}
