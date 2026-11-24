// New crawl's title and first line (the audit of 2026-11-19, H-21). Pure.
import type { JavaScriptRendering } from '../api/types';
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

/**
 * What the crawl reads, said once where it's started (H-09), by the rendering mode the form holds: it said "their
 * JavaScript isn't run" whatever the mode, from before the engine could render.
 */
export function renderingNote(rendering: JavaScriptRendering): string {
  switch (rendering) {
    case 'off':
      return 'Pages are read as the server sends them: their JavaScript isn’t run, so what a page adds with scripts isn’t seen.';
    case 'javaScript':
      return 'Every HTML page is rendered in a browser, so what its scripts add is seen. It is slower than reading the HTML alone.';
    case 'matching':
      return 'Only the URLs listed under JavaScript and filters are rendered in a browser. The rest are read as the server sends them.';
    default:
      return 'Pages are read as the server sends them. One that is only a shell for its scripts is rendered in a browser.';
  }
}
