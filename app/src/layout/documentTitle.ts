// Each screen names the window (gap 26 of the closing audit): "Issues · garden.co.uk · Shouting Iguana". The shell
// mirrors document.title onto the window, so the taskbar, Alt+Tab and a screen reader's window list say where you are.
// The same words as the breadcrumb, in the mode's words: "What to fix" in Simple where Advanced says "Issues".
import { useQuery } from '@tanstack/react-query';
import { useLocation, useParams } from '@tanstack/react-router';
import { useEffect } from 'react';
import { crawlQuery } from '../api/queries';
import { crawlSectionEntry, sectionOf } from '../app/sections';
import { crawlName } from '../crawl/status';
import type { Mode } from '../mode/mode';
import { useMode } from '../mode/mode';

export const APP_TITLE = 'Shouting Iguana';

/** Where you are outside a crawl, as the breadcrumb names it. */
export function pageName(pathname: string, mode: Mode): string | null {
  if (pathname === '/') return 'Home';
  if (pathname === '/new') return mode === 'simple' ? 'Check a website' : 'New crawl';
  if (pathname === '/settings' || pathname.startsWith('/settings/')) return 'Settings';
  return null;
}

/** The window's title for a place: its section or page, then the site, then the app. */
export function documentTitle({
  pathname,
  mode,
  site,
}: {
  pathname: string;
  mode: Mode;
  /** The crawl's name, when the place is in a crawl and the crawl is known. */
  site: string | null;
}): string {
  const section = sectionOf(pathname);
  const entry = section ? crawlSectionEntry(section) : null;
  const here = entry ? ((mode === 'simple' ? entry.simpleTitle : undefined) ?? entry.title) : null;
  const inCrawl = pathname.startsWith('/crawls/');
  const parts = inCrawl ? [here, site] : [pageName(pathname, mode)];
  return [...parts.filter((part): part is string => Boolean(part)), APP_TITLE].join(' · ');
}

/** Keeps document.title on the place shown. */
export function useDocumentTitle(): void {
  const mode = useMode();
  const pathname = useLocation({ select: (location) => location.pathname });
  const { crawl: crawlId } = useParams({ strict: false });
  const crawl = useQuery({ ...crawlQuery(crawlId ?? ''), enabled: crawlId !== undefined }).data;
  const site = crawlId && crawl ? crawlName(crawl) : null;
  const title = documentTitle({ pathname, mode, site });
  useEffect(() => {
    document.title = title;
  }, [title]);
}
