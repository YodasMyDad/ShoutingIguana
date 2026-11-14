// Checks the user hid (docs/v2-ui-discovery.md, decision 2: "a check can be hidden, for one crawl or for all, from the
// Issues screen. The analysis still runs it, and exports leave it out"): those hidden in every crawl
// (`settings.hiddenChecks`) and those hidden in one crawl only (`settings.hiddenChecksByCrawl`). Every count, list and
// export that leaves hidden checks out reads them from here, for the crawl it shows.
import { useMemo } from 'react';
import type { Settings } from './schema';
import { getSettings, updateSettings, useSettings } from './settingsStore';

/** Where a check is hidden: in this crawl only, in every crawl, or not at all. */
export type HiddenScope = 'crawl' | 'all' | null;

/** The checks hidden in a crawl: those hidden in every crawl, then its own. */
export function hiddenChecksOf(settings: Settings, crawlId: string | null | undefined): string[] {
  const own = crawlId ? (settings.hiddenChecksByCrawl[crawlId] ?? []) : [];
  return own.length === 0 ? settings.hiddenChecks : [...new Set([...settings.hiddenChecks, ...own])];
}

/** Where a check is hidden in a crawl: every crawl wins over the crawl's own. */
export function hiddenScopeOf(settings: Settings, crawlId: string | null | undefined, checkId: string): HiddenScope {
  if (settings.hiddenChecks.includes(checkId)) return 'all';
  return crawlId && settings.hiddenChecksByCrawl[crawlId]?.includes(checkId) ? 'crawl' : null;
}

/** The checks hidden in a crawl, as they change; the same array while they don't. */
export function useHiddenChecks(crawlId: string | null | undefined): string[] {
  const key = useSettings((settings) => hiddenChecksOf(settings, crawlId).join(' '));
  return useMemo(() => (key === '' ? [] : key.split(' ')), [key]);
}

/** The checks hidden in a crawl now, outside React (the live bridge's notices). */
export function currentHiddenChecks(crawlId: string | null | undefined): string[] {
  return hiddenChecksOf(getSettings(), crawlId);
}

/** The settings with a check hidden in a crawl only, in every crawl, or shown again (`null`) in both. */
export function withHiddenScope(
  settings: Settings,
  crawlId: string,
  checkId: string,
  scope: HiddenScope,
): Pick<Settings, 'hiddenChecks' | 'hiddenChecksByCrawl'> {
  const all = settings.hiddenChecks.filter((id) => id !== checkId);
  const own = (settings.hiddenChecksByCrawl[crawlId] ?? []).filter((id) => id !== checkId);
  const others = Object.fromEntries(
    Object.entries(settings.hiddenChecksByCrawl).filter(([crawl]) => crawl !== crawlId),
  );
  const crawlIds = scope === 'crawl' ? [...own, checkId] : own;
  return {
    hiddenChecks: scope === 'all' ? [...all, checkId] : all,
    // The crawl's own list goes last, so the crawls kept are the ones most recently changed.
    hiddenChecksByCrawl: crawlIds.length > 0 ? { ...others, [crawlId]: crawlIds } : others,
  };
}

/** Hides a check in a crawl only or in every crawl, or shows it again. */
export function setCheckHidden(crawlId: string, checkId: string, scope: HiddenScope): void {
  updateSettings((settings) => withHiddenScope(settings, crawlId, checkId, scope));
}

/** Shows every check hidden in a crawl again: its own and those hidden in every crawl. */
export function showAllHidden(crawlId: string): void {
  updateSettings((settings) => ({
    hiddenChecks: [],
    hiddenChecksByCrawl: Object.fromEntries(
      Object.entries(settings.hiddenChecksByCrawl).filter(([crawl]) => crawl !== crawlId),
    ),
  }));
}
