// The crawl the app's own Settings were opened from (the audit of 2026-11-19, H-10): Settings is outside every crawl,
// and used to take the crawl's switcher and sections off the sidebar, so the only way back was Back. The sidebar now
// keeps the crawl it was on while Settings shows. Home and New crawl forget it: from there, no crawl is open.
// Kept for the window (sessionStorage), so a reload on Settings keeps the crawl's block too.
import { useLocation, useParams } from '@tanstack/react-router';
import { useEffect, useSyncExternalStore } from 'react';
import { CRAWL_ID } from '../app/sections';

const STORAGE_KEY = 'si.contextCrawl';

function read(): string | null {
  try {
    const stored = window.sessionStorage.getItem(STORAGE_KEY);
    return stored !== null && CRAWL_ID.test(stored) ? stored : null;
  } catch {
    return null;
  }
}

let contextCrawl: string | null = typeof window === 'undefined' ? null : read();
const listeners = new Set<() => void>();

function set(next: string | null): void {
  if (next === contextCrawl) return;
  contextCrawl = next;
  try {
    if (next === null) window.sessionStorage.removeItem(STORAGE_KEY);
    else window.sessionStorage.setItem(STORAGE_KEY, next);
  } catch {
    // No storage (a private window): the crawl is still kept until a reload.
  }
  listeners.forEach((listener) => listener());
}

/** Whether a path is the app's own Settings, which keeps the crawl it was opened from on the sidebar. */
export function isAppSettings(pathname: string): boolean {
  return pathname === '/settings' || pathname.startsWith('/settings/');
}

/**
 * Notes where the app is: a crawl's screen makes that crawl the one Settings keeps; Settings changes nothing; any
 * other place (Home, New crawl) forgets it.
 */
export function noteLocation(pathname: string, crawlId: string | undefined): void {
  if (crawlId !== undefined) set(crawlId);
  else if (!isAppSettings(pathname)) set(null);
}

const subscribe = (listener: () => void) => {
  listeners.add(listener);
  return () => listeners.delete(listener);
};

/** The crawl Settings was opened from, or null when it was opened from outside every crawl. */
export function useContextCrawl(): string | null {
  return useSyncExternalStore(
    subscribe,
    () => contextCrawl,
    () => null,
  );
}

/**
 * The crawl the sidebar shows, and the keys Ctrl+1 to Ctrl+8 go to: the one whose screen is open, or on the app's
 * Settings the one they were opened from. Undefined when neither.
 */
export function useSidebarCrawl(): string | undefined {
  const { crawl: routeCrawl } = useParams({ strict: false });
  const pathname = useLocation({ select: (location) => location.pathname });
  const kept = useContextCrawl();
  useEffect(() => noteLocation(pathname, routeCrawl), [pathname, routeCrawl]);
  return routeCrawl ?? (isAppSettings(pathname) ? (kept ?? undefined) : undefined);
}

/** Tests only: no crawl is kept. */
export function resetContextCrawlForTests(): void {
  set(null);
}
