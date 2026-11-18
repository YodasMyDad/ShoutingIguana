// The engine's synthetic site with revisions, for the fix loop's specs (docs/v2-fix-loop.md, section 9, "The synthetic
// site gets revisions"): a site of the spec's own on loopback (never a real site), served first at revision 0 and
// switched to revision 1 ("after the fixes") as its owner publishing a fix would, its URLs and ports staying the same.
// Its crawls go through the sidecar the tests run against. One a spec file (beforeAll), stopped after it (afterAll).
import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { buildCli } from '../scripts/lib/engine.mjs';
import { crawlTestSite, type SeededCrawl } from './support';

export interface RevisionSite {
  /** The site's start URL: `http://127.0.0.1:{port}/`. */
  url: string;
  /** Serves `revision` from the next request on: 0 as it has always been, 1 after the fixes. */
  setRevision: (revision: number) => Promise<void>;
  /** Crawls it to its end at the Fast preset and analyses it: a library item of the sidecar's. */
  crawl: () => Promise<SeededCrawl>;
  stop: () => void;
}

/**
 * Starts the site with `pages` pages at `revision` (0 by default). Enough pages that revision 1's plants are there
 * (products 184 to 186 make the renamed duplicate cluster), few enough that a crawl ends in seconds.
 */
export async function startRevisionSite({
  pages = 400,
  revision = 0,
}: { pages?: number; revision?: number } = {}): Promise<RevisionSite> {
  const site = spawn(
    buildCli(),
    ['testsite', '--pages', String(pages), '--port', '0', '--revision', String(revision)],
    { stdio: ['ignore', 'pipe', 'inherit'], windowsHide: true },
  );
  // The READY line: the site's URL, the external site's, and the token the revision route takes.
  const [url, , token] = await new Promise<string[]>((resolve, reject) => {
    const timer = setTimeout(() => {
      site.kill();
      reject(new Error('testsite wrote no READY line within 30 s'));
    }, 30_000);
    createInterface({ input: site.stdout }).on('line', (line) => {
      if (!line.startsWith('READY ')) return;
      clearTimeout(timer);
      resolve(line.slice('READY '.length).split(' '));
    });
    site.once('exit', (code) => reject(new Error(`testsite exited with code ${code}`)));
  });
  if (!url || !token) throw new Error('testsite’s READY line had no URL or no token');

  return {
    url,
    setRevision: async (next) => {
      const response = await fetch(new URL(`/__si/revision/${next}`, url), {
        method: 'POST',
        headers: { 'X-SI-Token': token },
      });
      if (!response.ok) throw new Error(`Switching the test site to revision ${next}: ${response.status}`);
    },
    crawl: () => crawlTestSite(url),
    stop: () => {
      site.kill();
    },
  };
}
