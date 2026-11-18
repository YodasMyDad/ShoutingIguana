// The One fix callout's words (docs/v2-ui-simple.md, "One problem"): what the pages share, said plainly and exactly
// ("The same image, /img/hero.jpg, is on 12 of these 14 pages."), then the one change that fixes them all ("Make it
// smaller once and all 12 are fixed."). The engine's One fix sentence, where the check has one, goes between them: it
// says where in the site such a thing usually is. Pure.
import type { IssueTarget } from '../api/inspector';
import type { Check } from '../api/types';
import { formatCount } from '../lib/format';
import { plainPosition } from './plainEvidence';

export interface OneFixCause {
  check: Pick<Check, 'id' | 'evidence'>;
  /** The pages (or addresses) the cause is on, and the problem's count. */
  on: number;
  of: number;
  noun: 'pages' | 'addresses';
  /** The cause's address as the problem page names it ("/img/hero.jpg"), or null for a cause without one. */
  path: string | null;
  /** What a duplicate cluster's pages share: their title, search description or main heading. */
  sharedText: string | null;
  /** The pages' link to the cause: what kind of link, and where on the page. */
  link: Pick<IssueTarget, 'kind' | 'position'> | null;
  /** Where a forwarding address ends, as the page names it: what a link to it should point at instead. */
  finalPath: string | null;
}

/**
 * What One fix is said to do (the callout's title, and the cause's in Where it is): "One fix clears all 14 of these
 * pages" when the shared cause is on every page the check flagged; "One fix covers 12 of 14 pages" when it isn't, so it
 * never reads as clearing the whole problem (the fixit audit: 195 of 225 pages was "One fix").
 */
export function oneFixTitle(on: number, of: number, noun: 'pages' | 'addresses'): string {
  return on >= of
    ? `One fix clears all ${formatCount(of)} of these ${noun}`
    : `One fix covers ${formatCount(on)} of ${formatCount(of)} ${noun}`;
}

/** "12 of these 14 pages", "all 14 of these pages". */
function ofThese(on: number, of: number, noun: string): string {
  return on >= of
    ? `all ${formatCount(of)} of these ${noun}`
    : `${formatCount(on)} of these ${formatCount(of)} ${noun}`;
}

const capital = (text: string) => text.charAt(0).toUpperCase() + text.slice(1);

/** What the fix is, for an image or a link the pages share, by the check. */
const FIXES: Record<string, string> = {
  'image-too-large': 'Make it smaller',
  'image-missing-alt': 'Give it alt text',
  'image-alt-too-long': 'Shorten its alt text',
  'image-missing-dimensions': 'Give it a width and height',
  'image-broken': 'Fix or replace it',
  'broken-internal-links': 'Fix or remove it',
  'broken-external-links': 'Fix or remove it',
  'empty-anchor-text': 'Give it some words',
  'internal-nofollow-links': 'Let search engines follow it',
  'broken-fragment-links': 'Point it at a section that’s there',
  'mixed-content': 'Load it over https',
};

/** Where on the pages it is, for a sentence: "in the footer " (a trailing space), or nothing for the main content. */
function placeOf(link: OneFixCause['link']): string {
  const where = link?.position && link.position !== 'content' ? plainPosition(link.position) : null;
  return where ? `${where} ` : '';
}

/** What a duplicate cluster's pages share, by the check's evidence. */
function sharedNoun(check: OneFixCause['check']): string {
  const evidence = check.evidence ?? [];
  if (evidence.includes('title')) return 'title';
  if (evidence.includes('description')) return 'search description';
  if (evidence.includes('h1')) return 'main heading';
  return 'words';
}

/**
 * The callout's sentences: `lead`, what the pages share and on how many of them; `fix`, the one change and what it
 * fixes.
 */
export function oneFixSentences(cause: OneFixCause): { lead: string; fix: string } {
  const { check, on, of, noun, path, sharedText, link, finalPath } = cause;
  const these = ofThese(on, of, noun);
  const all = `all ${formatCount(on)} are fixed`;
  if (sharedText !== null) {
    return {
      lead: `${capital(these)} share the same ${sharedNoun(check)}, “${sharedText}”.`,
      fix: `It’s probably set in one place for all of them: change it there and ${all}.`,
    };
  }
  if (path === null && noun === 'addresses') {
    // Addresses (a forwarding address, one the rules file keeps out) aren't in a page's design: one cause, one fix.
    return { lead: `${capital(these)} have the same cause.`, fix: `Fix it once and ${all}.` };
  }
  if (path === null) {
    return {
      lead: `${capital(these)} have the same fault, in the part of the site every page shares.`,
      fix: `Fix it there once and ${all}.`,
    };
  }
  if (check.id.startsWith('image-') || link?.kind === 'image') {
    return {
      lead: `The same image, ${path}, is ${placeOf(link)}on ${these}.`,
      fix: `${FIXES[check.id] ?? 'Fix it'} once and ${all}.`,
    };
  }
  if (link?.kind === 'canonical') {
    return { lead: `${capital(these)} name the same main address, ${path}.`, fix: `Fix it once and ${all}.` };
  }
  if (link?.kind === 'hreflang') {
    return { lead: `${capital(these)} list the same language version, ${path}.`, fix: `Fix it once and ${all}.` };
  }
  if (link && link.kind !== 'hyperlink') {
    return {
      lead: `The same file, ${path}, is loaded on ${these}.`,
      fix: `${FIXES[check.id] ?? 'Fix it'} once and ${all}.`,
    };
  }
  if (link) {
    const fix =
      check.id === 'links-to-redirects'
        ? finalPath
          ? `Point it at ${finalPath}`
          : 'Point it at the address it ends at'
        : (FIXES[check.id] ?? 'Fix it');
    return {
      lead: `The same link, to ${path}, is ${placeOf(link)}on ${these}.`,
      fix: `${fix} once there and ${all}.`,
    };
  }
  return { lead: `${capital(these)} have the same cause, ${path}.`, fix: `Fix it once and ${all}.` };
}

/** The callout's sentences for a check that lists pages, not causes, on nearly every page the check read. */
export function flatOneFixSentences(count: number, pagesChecked: number): { lead: string; fix: string } {
  return {
    lead: `It’s on ${formatCount(count)} of the ${formatCount(pagesChecked)} pages checked.`,
    fix: `Fix it there once and all ${formatCount(count)} are fixed.`,
  };
}
