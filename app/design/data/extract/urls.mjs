// Step 3: the URL grid fixtures, straight from the analysed copies (run sidecar-api.mjs first).
//   <out>/garden/urls.json            every fetched URL of the garden crawl
//   <out>/synthetic/urls-sample.json  the first 2,000 fetched URLs of the 100k crawl by id, with { total }
import { join } from 'node:path';
import { PAGE_SQL, iso, openCrawl } from './lib/crawl-db.mjs';
import { writeJson } from './lib/json.mjs';
import { crawlCopy, settings as readSettings } from './lib/options.mjs';
import { createUrlRows } from './lib/url-rows.mjs';

const SYNTHETIC_SAMPLE = 2_000;
const settings = readSettings();

function extract(which, limit) {
    const db = openCrawl(crawlCopy(settings, which));
    try {
        const crawl = db.prepare('SELECT start_url, started_at, finished_at FROM crawl WHERE id = 1').get();
        const counts = db.prepare(`
            SELECT (SELECT COUNT(*) FROM responses) AS fetched,
                   (SELECT COUNT(*) FROM urls) AS discovered,
                   (SELECT COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id WHERE ${PAGE_SQL}) AS pages`).get();
        const started = Date.now();
        const rows = createUrlRows(db).rows(limit);
        console.log(`${which}: ${rows.length.toLocaleString('en-GB')} rows in ${Date.now() - started} ms`);
        return {
            crawl: { startUrl: crawl.start_url, startedAt: iso(crawl.started_at), finishedAt: iso(crawl.finished_at) },
            total: counts.fetched,
            discovered: counts.discovered,
            notFetched: counts.discovered - counts.fetched,
            pages: counts.pages,
            rows
        };
    } finally {
        db.close();
    }
}

const garden = extract('garden', -1);
let size = writeJson(join(settings.out, 'garden', 'urls.json'), garden);
console.log(`garden/urls.json: ${size.toLocaleString('en-GB')} bytes`);

const synthetic = extract('synthetic', SYNTHETIC_SAMPLE);
size = writeJson(join(settings.out, 'synthetic', 'urls-sample.json'), synthetic);
console.log(`synthetic/urls-sample.json: ${size.toLocaleString('en-GB')} bytes`);
