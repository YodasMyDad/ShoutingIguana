// New crawl's title and first line (the audit of 2026-11-19, H-21). Pure.
import type { Mode } from '../mode/mode';

/** The crawl the form's settings come from: "Crawl again…". */
export interface Again {
  site: string;
  date: string | null;
}

/** The page's title and first line: a new crawl, or another crawl of a site with the settings of its last. */
export function pageWords(mode: Mode, again: Again | null): { title: string; subtitle: string } {
  if (again) {
    const from = again.date ? ` of ${again.date}` : '';
    return mode === 'simple'
      ? {
          title: `Check ${again.site} again`,
          subtitle: `These are the choices of the check${from}. Change what you want, then start it.`,
        }
      : {
          title: `Crawl ${again.site} again`,
          subtitle: `These are the settings of the crawl${from}. Change what this crawl needs, then start it.`,
        };
  }
  return mode === 'simple'
    ? { title: 'Check a website', subtitle: 'The address is all it needs. Change the rest only if you want to.' }
    : { title: 'New crawl', subtitle: 'Every setting has a default. Change what this crawl needs, then start it.' };
}
