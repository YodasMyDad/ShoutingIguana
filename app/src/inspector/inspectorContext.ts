// What the inspector's sections share that isn't a component: where they are (the crawl, the URL, how to walk), the
// catalog's words for a check, and the URL's issues by category.
import { useQuery } from '@tanstack/react-query';
import { createContext, use, useMemo } from 'react';
import type { UrlIssue } from '../api/inspector';
import { checksQuery } from '../api/queries';
import type { Check } from '../api/types';

/** What every section needs to know about where it is: the crawl, its origin, and how to walk to another URL. */
export interface InspectorContextValue {
  crawlId: string;
  /** The URL the inspector shows. */
  urlId: number;
  /** The crawl's own scheme and host: URLs on it show their path. */
  siteOrigin: string | null;
  /** A link inside the inspector: a step of the walk, a new place in history. */
  walk: (urlId: number) => void;
  /** A crawl is running: what's shown may change, in place. */
  crawling: boolean;
}

/** Set by the inspector around its sections. */
export const InspectorContext = createContext<InspectorContextValue | null>(null);

export function useInspectorContext(): InspectorContextValue {
  const value = use(InspectorContext);
  if (!value) throw new Error('An inspector section is rendered outside the inspector');
  return value;
}

/** The catalog, by check id: titles and how each check groups. Read once for the app. */
export function useChecks(): { byId: ReadonlyMap<string, Check>; ready: boolean } {
  const checks = useQuery(checksQuery());
  const byId = useMemo(() => new Map((checks.data?.checks ?? []).map((check) => [check.id, check])), [checks.data]);
  return { byId, ready: checks.data !== undefined || checks.isError };
}

/** A check's title from the catalog, or plain words for one this version doesn't have. */
export function checkTitle(check: Check | undefined, ready: boolean): string {
  if (check) return check.title;
  return ready ? 'A check this version doesn’t have' : 'Looking up the check…';
}

/** The URL's issues from checks in the given categories: what a section says the analysis found about it. */
export function useIssuesIn(issues: readonly UrlIssue[] | null, categories: readonly string[]): UrlIssue[] {
  const { byId } = useChecks();
  return useMemo(
    () => (issues ?? []).filter((issue) => categories.includes(byId.get(issue.checkId)?.category ?? '')),
    [issues, categories, byId],
  );
}
