// What the results say of a Shopify store's pages that Shopify refused (step 3b, lane shopify): how many weren't
// checked, why, and what to do, in each mode's words. Simple's say "signature" because Shopify's button does, and
// explain it in a line; Advanced's say the status and the engine's terms. Pure.
import type { RecheckAnswers } from '../api/fixLoop';
import type { ShopifyAccess } from '../api/types';
import { formatCount } from '../lib/format';
import { reasonSentence, simpleReason } from './refusals';

export interface ShopifyPromptWords {
  title: string;
  body: string;
  /** The button that opens the form for a signature. */
  action: string;
  /** Whether the steps in Shopify admin follow the body. */
  steps: boolean;
}

function pages(count: number, mode: 'simple' | 'advanced'): string {
  const noun = mode === 'simple' ? (count === 1 ? 'page' : 'pages') : count === 1 ? 'URL' : 'URLs';
  return `${formatCount(count)} ${noun}`;
}

/** Null when the store refused nothing: no prompt. */
export function shopifyPromptWords(access: ShopifyAccess, mode: 'simple' | 'advanced'): ShopifyPromptWords | null {
  const words = promptWords(access, mode);
  if (!words) return null;
  // Which problem the signature had, where the crawl could tell, in each mode's words.
  const reasons = access.reasons.map((reason) => (mode === 'simple' ? simpleReason(reason) : reasonSentence(reason)));
  return reasons.length === 0 ? words : { ...words, body: `${words.body} ${[...new Set(reasons)].join(' ')}` };
}

function promptWords(access: ShopifyAccess, mode: 'simple' | 'advanced'): ShopifyPromptWords | null {
  const unsigned = access.notCheckedUnsigned;
  const signed = access.notCheckedSigned;
  if (unsigned + signed === 0) return null;
  const was = (count: number) => (count === 1 ? 'was' : 'were');
  const wasnt = (count: number) => (count === 1 ? 'wasn’t' : 'weren’t');
  if (signed > 0 && unsigned === 0) {
    return mode === 'simple'
      ? {
          title: `${pages(signed, mode)} ${was(signed)} turned away even with the signature`,
          body: 'Try again in a while, or make a new one.',
          action: 'Change the signature',
          steps: true,
        }
      : {
          title: `${pages(signed, mode)} ${was(signed)} refused even with the Shopify signature`,
          body: `${access.host} answered 429 Too Many Requests to signed requests. They’re listed as not checked, not broken.`,
          action: 'Change the signature',
          steps: true,
        };
  }
  const count = unsigned + signed;
  return mode === 'simple'
    ? {
        title: `${pages(count, mode)} couldn’t be read: Shopify turns away checks without a signature`,
        body: `${access.host} is a Shopify shop. A signature is a pass the shop’s owner makes in Shopify, so a check can read every page. Add one, then check these pages again.`,
        action: 'Add a Shopify signature',
        steps: true,
      }
    : {
        title: `${pages(count, mode)} ${wasnt(count)} checked: Shopify limits crawlers without a signature`,
        body: `${access.host} is a Shopify store, and Shopify refuses crawlers that don’t send a signature (429 Too Many Requests), so these are listed as not checked, not broken. Add a Shopify signature, then recheck them.`,
        action: 'Add a Shopify signature',
        steps: true,
      };
}

export interface ShopifyRecheckWords {
  /** How many of the pages the store refused were read: "All 59 pages Shopify turned away were read". */
  title: string;
  /** That the results include them, and what became of the rest: "Shopify turned 5 away again, and …". */
  body: string;
}

/**
 * What the recheck the prompt's form started read of the pages the store refused, from how the site answered each:
 * Simple's "5 of the 8 pages Shopify turned away were read. The results include them now. Shopify turned 3 away
 * again."; Advanced's "Recheck: 5 of 8 refused URLs read". Null for a recheck from before answers were kept.
 */
export function shopifyRecheckWords(
  judged: number,
  answers: RecheckAnswers | null,
  mode: 'simple' | 'advanced',
): ShopifyRecheckWords | null {
  if (!answers || judged === 0) return null;
  const { read, refused, notAsked } = answers;
  if (mode === 'advanced') {
    const rest = [
      refused > 0 ? `${formatCount(refused)} refused again` : null,
      notAsked > 0 ? `${formatCount(notAsked)} not asked` : null,
    ].filter(Boolean);
    const included = read === 0 ? null : read === 1 ? 'It’s in the results now.' : 'They’re in the results now.';
    return {
      title: `Recheck: ${formatCount(read)} of ${formatCount(judged)} refused ${judged === 1 ? 'URL' : 'URLs'} read`,
      body: [included, rest.length > 0 ? `${rest.join(', ')}.` : null].filter(Boolean).join(' '),
    };
  }
  const title =
    read === judged
      ? judged === 1
        ? 'The page Shopify turned away was read'
        : `All ${formatCount(judged)} pages Shopify turned away were read`
      : read > 0
        ? `${formatCount(read)} of the ${formatCount(judged)} pages Shopify turned away ${read === 1 ? 'was' : 'were'} read`
        : judged === 1
          ? 'The page Shopify turned away couldn’t be read'
          : `None of the ${formatCount(judged)} pages Shopify turned away could be read`;
  const notAskedWords = notAsked === 0 ? '' : notAsked === judged ? 'them' : `the other ${formatCount(notAsked)}`;
  const rest =
    refused > 0
      ? `Shopify turned ${formatCount(refused)} away again${notAsked > 0 ? `, and the check stopped before asking for ${notAskedWords}` : ''}.`
      : notAsked > 0
        ? `The check stopped before asking for ${notAskedWords}.`
        : null;
  const included = read === 0 ? null : `The results include ${read === 1 ? 'it' : 'them'} now.`;
  return { title, body: [included, rest].filter(Boolean).join(' ') };
}

/** The dialog's button, as the fix loop says it: Simple's "Recheck these 8 pages", Advanced's "Recheck these 8 URLs". */
export function recheckLabel(count: number, mode: 'simple' | 'advanced'): string {
  if (count === 1) return mode === 'simple' ? 'Recheck this page' : 'Recheck this URL';
  return `Recheck these ${formatCount(count)} ${mode === 'simple' ? 'pages' : 'URLs'}`;
}
