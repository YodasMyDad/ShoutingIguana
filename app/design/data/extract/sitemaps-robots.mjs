// The Sitemaps and robots.txt fixtures, as the sidecar answers them over the analysed copies (run copy-crawls.mjs and
// urls.mjs first: a copy this engine hasn't analysed is analysed here). For each crawl, in <out>/api/<crawl>/:
//   sitemaps.json         GET /sitemaps
//   sitemap-urls.json     { "<sitemap id>": [ids of the URL fixture's rows that sitemap lists] }: the mock's URL grid
//                         for one sitemap is those rows (the 100k crawl's fixture is a sample, so its counts are
//                         the sample's)
//   robots.json           POST /robots, every origin (up to MAX_ORIGINS)
//   robots-files.json     { "<origin>": POST /robots/file }, for each of those origins
//   robots-blocked.json   { "<origin>": the rows of POST /robots/blocked (up to MAX_BLOCKED each) }
//
//   node sitemaps-robots.mjs [--sidecar <si-sidecar.exe>] [--work <dir>] [--out <dir>]
import { existsSync } from 'node:fs';
import { basename, join } from 'node:path';
import { readJson, writeJson } from './lib/json.mjs';
import { crawlCopy, settings as readSettings } from './lib/options.mjs';
import { startSidecar } from './lib/sidecar.mjs';

const MAX_ORIGINS = 2_000;
const MAX_BLOCKED = 1_000;
const PAGE = 1_000; // the API's maximum limit
/** The URL fixture each crawl's grid is served from: every row of the garden crawl, a sample of the 100k one. */
const URL_FIXTURES = { garden: join('garden', 'urls.json'), synthetic: join('synthetic', 'urls-sample.json') };

const settings = readSettings();
const copies = { garden: crawlCopy(settings, 'garden'), synthetic: crawlCopy(settings, 'synthetic') };
// A state folder of this step's own, so it can run beside another step's sidecar.
const sidecar = await startSidecar(settings, join(settings.work, 'state-sitemaps-robots'));
console.log(`sidecar ${sidecar.version}`);

let saved = 0;
const save = (which, name, value) => {
    saved += writeJson(join(settings.out, 'api', which, name), value);
};

try {
    const library = await sidecar.get('/api/crawls');
    for (const which of Object.keys(copies)) {
        const item = library.find(crawl => crawl.fileName === basename(copies[which]));
        if (!item) throw new Error(`The sidecar doesn't list ${copies[which]}.`);
        const crawl = `/api/crawls/${item.id}`;

        // The counts are the analysis's judgement only with a URL summary this engine wrote.
        let sitemaps = await sidecar.get(`${crawl}/sitemaps`);
        if (!sitemaps.summary.available) {
            console.log(`${which}: analysing (${sitemaps.summary.reason})`);
            await sidecar.analyse(item.id);
            sitemaps = await sidecar.get(`${crawl}/sitemaps`);
        }
        save(which, 'sitemaps.json', sitemaps);

        // Which of the URL fixture's rows each sitemap lists: read from the sitemap's own grid, by id.
        const fixture = join(settings.out, URL_FIXTURES[which]);
        if (!existsSync(fixture)) throw new Error(`${fixture} doesn't exist. Run urls.mjs first (or run-all.mjs).`);
        const sampleIds = new Set(readJson(fixture).rows.map(row => row.id));
        const maxId = Math.max(0, ...sampleIds);
        const listed = {};
        for (const sitemap of sitemaps.sitemaps) {
            const ids = [];
            for (let offset = 0; sitemap.urls.listed > 0; offset += PAGE) {
                const page = await sidecar.post(`${crawl}/sitemaps/urls`, {
                    sitemapId: sitemap.id,
                    columns: ['url'],
                    filters: [{ column: 'id', op: 'lte', value: maxId }],
                    sort: [{ column: 'id', desc: false }],
                    offset,
                    limit: PAGE
                });
                ids.push(...page.rows.map(row => row.id).filter(id => sampleIds.has(id)));
                if (offset + page.rows.length >= page.matching || page.rows.length === 0) break;
            }

            listed[sitemap.id] = ids;
        }

        save(which, 'sitemap-urls.json', listed);

        const first = await sidecar.post(`${crawl}/robots`, { offset: 0, limit: PAGE });
        const origins = [...first.origins];
        while (origins.length < Math.min(first.total, MAX_ORIGINS)) {
            const page = await sidecar.post(`${crawl}/robots`, { offset: origins.length, limit: PAGE });
            if (page.origins.length === 0) break;
            origins.push(...page.origins);
        }

        save(which, 'robots.json', { ...first, total: origins.length, origins: origins.slice(0, MAX_ORIGINS) });

        const files = {};
        const blocked = {};
        for (const { origin } of origins.slice(0, MAX_ORIGINS)) {
            files[origin] = await sidecar.post(`${crawl}/robots/file`, { origin });
            if (files[origin].blocked > 0) {
                blocked[origin] = (await sidecar.post(`${crawl}/robots/blocked`, { origin, offset: 0, limit: MAX_BLOCKED })).rows;
            }
        }

        save(which, 'robots-files.json', files);
        save(which, 'robots-blocked.json', blocked);
        console.log(`${which}: ${sitemaps.sitemaps.length} sitemaps, ${origins.length} origins, ${first.blocked} URLs blocked`);
    }
} finally {
    await sidecar.stop();
}

console.log(`saved 10 files (${saved.toLocaleString('en-GB')} bytes) in ${join(settings.out, 'api')}`);
