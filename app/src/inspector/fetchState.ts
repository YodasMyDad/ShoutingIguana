// What the crawl did with a URL, as the inspector says it (docs/v2-ui-brief.md, Every view has every state: a URL not
// fetched says why; one that answered with no body says so). Read from how it was found and how it answered. A URL
// that robots.txt disallows has a response stored with no status: its `outcome` says what happened, so this goes by
// the outcome, never by `fetched`.
import type { UrlDetail } from '../api/inspector';
import { NOT_FETCHED_WORDS, TYPE_IN_A_SENTENCE, reasonPhrase } from './words';

export type FetchKind =
  /** Found, not reached yet. */
  | 'queued'
  | 'fetching'
  /** Found and left alone, for a reason the crawl stored. */
  | 'skipped'
  /** robots.txt disallows it: never requested. */
  | 'robots'
  /** The server refused the crawler. */
  | 'blocked'
  /** Its host had stopped answering. */
  | 'notChecked'
  /** No answer: a timeout, a network error, or one that couldn't be read. */
  | 'failed'
  /** Only its status was checked: the crawl never read its content. */
  | 'statusOnly'
  /** It redirects: there's no page of its own. */
  | 'redirected'
  /** It answered 4xx or 5xx. */
  | 'errorStatus'
  /** It answered, and it isn't a page (an image, a PDF). */
  | 'notPage'
  /** A page that answered with nothing to read. */
  | 'noBody'
  /** A page fetched and read. */
  | 'ok';

export interface FetchState {
  kind: FetchKind;
  /** Whether there's an answer to show: a status, timings. */
  answered: boolean;
  /** Whether the page's content was read: titles, words, headings. */
  hasContent: boolean;
  /** In a few words: "Not fetched yet", "Blocked by robots.txt". Null for a page fetched and read. */
  title: string | null;
  /** Why, in a sentence. Null for a page fetched and read. */
  why: string | null;
  /** The crawl is still going to get to it: it may change while the crawl runs. Never set for a crawl that stopped. */
  pending: boolean;
}

type Subject = Pick<UrlDetail, 'type' | 'discovery' | 'response' | 'content'>;

const state = (
  kind: FetchKind,
  title: string | null,
  why: string | null,
  flags: { answered?: boolean; hasContent?: boolean; pending?: boolean } = {},
): FetchState => ({
  kind,
  title,
  why,
  answered: flags.answered ?? false,
  hasContent: flags.hasContent ?? false,
  pending: flags.pending ?? false,
});

/** What a URL the crawl had still to fetch says once the crawl isn't running: nothing is on its way to it. */
export const NEVER_REACHED = 'The crawl stopped before it reached this URL.';

/**
 * What the crawl did with the URL. `crawling`: the crawl is running (or paused), so a URL it hasn't reached is still
 * on its way; in a crawl that stopped, it never was fetched and won't be until the crawl is continued.
 */
export function fetchState(subject: Subject, crawling: boolean): FetchState {
  const { discovery, response, content } = subject;
  if (response === null) {
    if (discovery.state === 'queued' || discovery.state === 'fetching') {
      if (!crawling) return state(discovery.state, 'Not fetched', NEVER_REACHED);
      return discovery.state === 'queued'
        ? state('queued', 'Not fetched yet', 'It’s in the queue: the crawl hasn’t reached it.', { pending: true })
        : state('fetching', 'Being fetched', 'The crawl is fetching it now.', { pending: true });
    }
    if (discovery.state === 'skipped') {
      const reason = discovery.notFetchedBecause;
      return state('skipped', 'Not fetched', NOT_FETCHED_WORDS[reason ?? 'none']);
    }
    return state('skipped', 'Not fetched', NOT_FETCHED_WORDS.none);
  }

  const code = response.statusCode;
  switch (response.outcome) {
    case 'robotsDisallowed':
      return state('robots', 'Blocked by robots.txt', 'robots.txt disallows it, so the crawl didn’t request it.');
    case 'blocked':
      return state(
        'blocked',
        'Blocked by the server',
        response.blockReason
          ? `The server refused the crawler: ${response.blockReason}.`
          : 'The server refused the crawler.',
        { answered: code !== null },
      );
    case 'notChecked':
      return state('notChecked', 'Not checked', 'Its host had stopped answering, so the crawl didn’t request it.');
    case 'timeout':
      return state('failed', 'Timed out', 'It didn’t answer in the time the crawl allows.');
    case 'networkError':
      return state('failed', 'Couldn’t be reached', 'The connection failed before it answered.');
    case 'processingError':
      return state('failed', 'Couldn’t be read', 'It answered, but what it sent couldn’t be read.', {
        answered: code !== null,
      });
    default:
      break;
  }
  if (code === null) return state('failed', 'No response', 'The crawl has no answer stored for it.');
  if (content !== null) return state('ok', null, null, { answered: true, hasContent: true });

  const answered = { answered: true };
  if (response.outcome === 'redirect' || (code >= 300 && code < 400)) {
    return state('redirected', 'Redirects', 'It sends requests on, so it has no page of its own to read.', answered);
  }
  if (code >= 400) {
    const phrase = reasonPhrase(code);
    return state(
      'errorStatus',
      `Answered ${code}${phrase ? ` ${phrase}` : ''}`,
      'It answered with an error, so there’s no page to read.',
      answered,
    );
  }
  if (discovery.fetchMode === 'check') {
    return state(
      'statusOnly',
      'Status checked only',
      'The crawl checked that it answers, and didn’t read its content.',
      answered,
    );
  }
  if (subject.type !== 'page') {
    return state(
      'notPage',
      'Not a page',
      `It’s ${TYPE_IN_A_SENTENCE[subject.type]}: the crawl reads the content of pages only.`,
      answered,
    );
  }
  return state('noBody', 'Answered with no body', `It answered ${code} with nothing to read.`, answered);
}
