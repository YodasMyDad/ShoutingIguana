// The inspector's sections (docs/v2-ui-ia.md, URL inspector tabs): their order, their names, the count each tab
// shows, and the one last chosen, remembered for the session so walking from URL to URL stays on the same section
// (inlink to inlink stays on Inlinks). The memory is the session's: a new window starts on Summary.
import type { UrlDetail } from '../api/inspector';

export const SECTION_IDS = [
  'summary',
  'issues',
  'inlinks',
  'outlinks',
  'redirects',
  'headers',
  'structuredData',
  'hreflang',
  'source',
] as const;

export type SectionId = (typeof SECTION_IDS)[number];

export const SECTION_TITLES: Record<SectionId, string> = {
  summary: 'Summary',
  issues: 'Issues',
  inlinks: 'Inlinks',
  outlinks: 'Outlinks',
  redirects: 'Redirects',
  headers: 'Headers',
  structuredData: 'Structured data',
  hreflang: 'Hreflang',
  source: 'Source',
};

export function isSectionId(value: unknown): value is SectionId {
  return typeof value === 'string' && (SECTION_IDS as readonly string[]).includes(value);
}

/**
 * The count each tab shows: what the section lists. Nothing for a section that lists nothing, or whose count isn't
 * known (no analysis: no issues, no link counts), so a tab never reads 0 for "not known".
 */
/**
 * A URL's issues as the inspector lists them: the checks the user hid from the Issues list come last (they're still
 * the URL's, and say "Hidden"), and aren't counted, as the sidebar and Overview leave them out of theirs.
 */
export function splitHidden<Issue extends { checkId: string }>(
  issues: readonly Issue[],
  hiddenChecks: readonly string[],
): { counted: Issue[]; hidden: Issue[] } {
  const hidden = new Set(hiddenChecks);
  return {
    counted: issues.filter((issue) => !hidden.has(issue.checkId)),
    hidden: issues.filter((issue) => hidden.has(issue.checkId)),
  };
}

/** `hiddenChecks`: the checks hidden from the Issues list (settings), left out of the Issues tab's count. */
export function sectionCounts(
  detail: Pick<UrlDetail, 'issues' | 'links' | 'redirects' | 'headers' | 'structuredData' | 'hreflang'>,
  hiddenChecks: readonly string[] = [],
): Partial<Record<SectionId, number>> {
  const counts: Partial<Record<SectionId, number>> = {};
  const set = (id: SectionId, value: number | null | undefined) => {
    if (value !== null && value !== undefined && value > 0) counts[id] = value;
  };
  set('issues', detail.issues ? splitHidden(detail.issues, hiddenChecks).counted.length : null);
  set('inlinks', detail.links?.inlinks);
  set('outlinks', detail.links?.outlinks);
  set('redirects', detail.redirects.hops.length);
  set('headers', detail.headers.length);
  set('structuredData', detail.structuredData.length);
  set('hreflang', detail.hreflang.length);
  return counts;
}

const STORAGE_KEY = 'si.inspector.section';

let remembered: SectionId | null = null;

function storage(): Storage | null {
  try {
    return typeof sessionStorage === 'undefined' ? null : sessionStorage;
  } catch {
    // Storage refused (a locked-down profile): the memory lasts while the page does.
    return null;
  }
}

/** The section last chosen in this session, or Summary. */
export function rememberedSection(): SectionId {
  if (remembered !== null) return remembered;
  try {
    const stored = storage()?.getItem(STORAGE_KEY);
    if (isSectionId(stored)) remembered = stored;
  } catch {
    // As above.
  }
  return remembered ?? 'summary';
}

export function rememberSection(section: SectionId): void {
  remembered = section;
  try {
    storage()?.setItem(STORAGE_KEY, section);
  } catch {
    // As above.
  }
}

/** Whether the source viewer wraps long lines: on until the session says otherwise. */
let wrapLines = true;

export function rememberedWrapLines(): boolean {
  return wrapLines;
}

export function rememberWrapLines(wrap: boolean): void {
  wrapLines = wrap;
}

/** Tests: forget the session's section and choices. */
export function resetRememberedSection(): void {
  remembered = null;
  wrapLines = true;
  try {
    storage()?.removeItem(STORAGE_KEY);
  } catch {
    // As above.
  }
}
