// The engine's `javascript-rendering` alert, read for both modes' words (docs/v2-sidecar-api.md, crawl options). The
// alert carries no data, only its sentence: "The browser couldn't start.", what the crawl is not getting, and why.
// The why is the engine's own words when it knows the fault (no download, the browser stopped, Linux libraries),
// and otherwise the fault as the browser's driver gave it ("Driver not found" and a path), which is never a sentence
// to show. Pure.
import type { AlertEvent } from '../api/types';

export interface RenderingAlert {
  /** The browser is being downloaded, which happens once: nothing is wrong. */
  downloading: boolean;
  /** Pages that were to be rendered are read as they were sent. */
  pagesNotRendered: boolean;
  /** A host that blocks plain HTTP stays blocked: the crawl had the browser as its way past a block. */
  blocksStay: boolean;
  /**
   * The computer's own browser, which a host that blocks plain HTTP is tried in although the crawl's browser couldn't
   * start: "Chrome", "Edge", "browser". Null when there is none, and the block stays.
   */
  ownBrowser: string | null;
  /** Why, in the engine's own words for people. Null when all there is is the driver's fault. */
  reason: string | null;
  /** The fault as the browser's driver gave it, for a tooltip. Null when the engine said why in its own words. */
  fault: string | null;
}

const NOT_RENDERED = 'No page of this crawl is rendered: every page is read as it was sent';
const BLOCKS_STAY = /a host that blocks plain HTTP stays blocked/i;
const OWN_BROWSER = /A host that blocks plain HTTP is tried in the computer's own ([^.]+)\.\s*/;
/** A crawl file of 2.0.3 and before says it this way. */
const FROM_2_0_3 = /^JavaScript rendering couldn't start, so pages are read as they were sent\.\s*/;
/** What the engine says before the why: the older sentence, then this version's (its second part varies). */
const BEFORE_WHY = [
  FROM_2_0_3,
  /^The browser couldn't start\.\s*/,
  /^No page of this crawl is rendered: every page is read as it was sent(, and a host that blocks plain HTTP stays blocked)?\.\s*/,
  /^A host that blocks plain HTTP stays blocked\.\s*/,
  new RegExp(`^${OWN_BROWSER.source}`),
  /^Nothing is read in it\.\s*/,
];

export function renderingAlert(alert: Pick<AlertEvent, 'level' | 'message'>): RenderingAlert {
  if (alert.level === 'info') {
    return {
      downloading: true,
      pagesNotRendered: false,
      blocksStay: false,
      ownBrowser: null,
      reason: null,
      fault: null,
    };
  }
  const blocksStay = BLOCKS_STAY.test(alert.message);
  const why = BEFORE_WHY.reduce((rest, sentence) => rest.replace(sentence, ''), alert.message).trim();
  // The engine's own reasons all start "The browser …"; anything else is an exception's message.
  const plain = why.startsWith('The browser ');
  return {
    downloading: false,
    pagesNotRendered: alert.message.includes(NOT_RENDERED) || FROM_2_0_3.test(alert.message),
    blocksStay,
    ownBrowser: OWN_BROWSER.exec(alert.message)?.[1] ?? null,
    reason: plain ? why : null,
    fault: plain || why === '' ? null : why,
  };
}
