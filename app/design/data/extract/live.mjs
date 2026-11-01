// Step 5: <out>/garden/live.json, the garden crawl replayed second by second from its stored fetch times, shaped like the
// sidecar's `progress` events (docs/v2-sidecar-api.md, Live events) plus the status-code breakdown so far. It lets a
// mockup show the crawl honestly at any point, such as 60% through.
//
// What is stored and what is reconstructed (see the README):
//   - responses.fetched_at is when each fetch started (ms). It ended response_ms later; that is when the engine
//     recorded it and counted it as fetched. The URLs a page links to were discovered when that page was recorded.
//   - The counters follow Crawling/CrawlCounters.cs and the resume restore in Storage/CrawlWriter.cs: htmlPages are
//     internal HTML fetched successfully, errors are 4xx, 5xx, timeouts, network and processing errors, bytes are the
//     sizes of bodies read. pagesPerSecond is responses per second over the last 5 s, as Crawling/CrawlSession.cs has it.
//   - Alerts are not stored in a crawl file (the sidecar keeps them in memory; E4 persists them), so there are none.
import { join } from 'node:path';
import { CRAWL_STATUS, FETCH, PAGE_SQL, SKIP_REASON, iso, openCrawl, statusClass } from './lib/crawl-db.mjs';
import { writeJson } from './lib/json.mjs';
import { crawlCopy, settings as readSettings } from './lib/options.mjs';

const STEP_MS = 1_000;
const RATE_WINDOW_MS = 5_000;
const MARKERS = [0.1, 0.25, 0.5, 0.6, 0.75, 0.9];
const SKIPPED = 3;
const ERRORS = new Set([FETCH.clientError, FETCH.serverError, FETCH.timeout, FETCH.networkError, FETCH.processingError]);

const settings = readSettings();
const db = openCrawl(crawlCopy(settings, 'garden'));
const crawl = db.prepare('SELECT start_url, status, started_at, finished_at, options_json FROM crawl WHERE id = 1').get();
const options = JSON.parse(crawl.options_json);
const start = crawl.started_at;
const end = crawl.finished_at ?? db.prepare('SELECT MAX(fetched_at + response_ms) AS last FROM responses').get().last;

// Each response: when its fetch started and when it was recorded.
const responses = db.prepare(`
    SELECT r.url_id, r.fetched_at, r.response_ms, r.outcome, r.status_code,
           CASE WHEN r.body_bytes IS NOT NULL THEN COALESCE(r.size_bytes, 0) ELSE 0 END AS bytes,
           (u.is_internal = 1 AND r.is_html = 1 AND r.outcome = 0) AS html_page,
           (${PAGE_SQL}) AS page
    FROM responses r JOIN urls u ON u.id = r.url_id`).all().map(response => ({
    ...response,
    started: response.fetched_at,
    done: Math.min(Math.max(response.fetched_at + response.response_ms, response.fetched_at), end)
}));
const doneAt = new Map(responses.map(response => [response.url_id, response.done]));

// Each URL: discovered when the page that first linked to it was recorded; the seed (and URLs found without a page:
// sitemap entries, site-version probes) at the start.
const skipReasons = Object.fromEntries(db.prepare(`SELECT skip_reason, COUNT(*) AS n FROM urls WHERE state = ${SKIPPED} GROUP BY skip_reason`).all()
    .map(row => [SKIP_REASON[row.skip_reason], row.n]));
const urls = db.prepare('SELECT id, state, found_on_id FROM urls').all().map(url => ({
    skipped: url.state === SKIPPED,
    at: url.found_on_id == null ? start : doneAt.get(url.found_on_id) ?? start
}));
db.close();

const byTime = (field, list) => [...list].sort((a, b) => a[field] - b[field]);
const finished = byTime('done', responses);
const started = byTime('started', responses);
const discovered = byTime('at', urls);

const totals = {
    discovered: 0, skipped: 0, started: 0, fetched: 0, htmlPages: 0, pages: 0, redirects: 0, errors: 0, blocked: 0,
    robotsDisallowed: 0, bytesDownloaded: 0, statusClasses: { '2xx': 0, '3xx': 0, '4xx': 0, '5xx': 0, noResponse: 0 }, statusCodes: {}
};
let nextDone = 0;
let nextStart = 0;
let nextFound = 0;
const fetchedAt = []; // fetched count at each snapshot, for the rate window
const snapshots = [];
let negativeQueue = 0;

function snapshot(elapsedMs, status) {
    const time = start + elapsedMs;
    while (nextFound < discovered.length && discovered[nextFound].at <= time) {
        totals.discovered++;
        totals.skipped += discovered[nextFound++].skipped ? 1 : 0;
    }

    while (nextStart < started.length && started[nextStart].started <= time) {
        totals.started++;
        nextStart++;
    }

    while (nextDone < finished.length && finished[nextDone].done <= time) {
        const response = finished[nextDone++];
        totals.fetched++;
        totals.htmlPages += response.html_page;
        totals.pages += response.page;
        totals.bytesDownloaded += response.bytes;
        if (response.outcome === FETCH.redirect) totals.redirects++;
        if (response.outcome === FETCH.blocked) totals.blocked++;
        if (response.outcome === FETCH.robotsDisallowed) totals.robotsDisallowed++;
        if (ERRORS.has(response.outcome)) totals.errors++;
        totals.statusClasses[statusClass(response.status_code)]++;
        if (response.status_code != null) {
            totals.statusCodes[response.status_code] = (totals.statusCodes[response.status_code] ?? 0) + 1;
        }
    }

    const inFlight = totals.started - totals.fetched;
    const queued = totals.discovered - totals.skipped - totals.started;
    negativeQueue = Math.min(negativeQueue, queued);
    fetchedAt.push({ elapsedMs, fetched: totals.fetched });
    const windowStart = fetchedAt.find(sample => sample.elapsedMs >= elapsedMs - RATE_WINDOW_MS);
    const seconds = (elapsedMs - windowStart.elapsedMs) / 1_000;
    snapshots.push({
        status,
        elapsedMs,
        at: iso(time),
        discovered: totals.discovered,
        queued: Math.max(queued, 0),
        inFlight,
        fetched: totals.fetched,
        htmlPages: totals.htmlPages,
        redirects: totals.redirects,
        errors: totals.errors,
        blocked: totals.blocked,
        robotsDisallowed: totals.robotsDisallowed,
        skipped: totals.skipped,
        bytesDownloaded: totals.bytesDownloaded,
        pagesPerSecond: seconds > 0 ? Math.round(((totals.fetched - windowStart.fetched) / seconds) * 100) / 100 : 0,
        pages: totals.pages,
        statusClasses: { ...totals.statusClasses },
        statusCodes: { ...totals.statusCodes }
    });
}

const durationMs = end - start;
for (let elapsedMs = 0; elapsedMs < durationMs; elapsedMs += STEP_MS) {
    snapshot(elapsedMs, 'running');
}
snapshot(durationMs, CRAWL_STATUS[crawl.status]);

const last = snapshots.at(-1);
if (last.fetched !== responses.length || last.discovered !== urls.length || last.queued !== 0 || last.inFlight !== 0) {
    throw new Error(`The replay doesn't end where the crawl did: ${JSON.stringify(last)}`);
}

const markers = Object.fromEntries(MARKERS.map(share => {
    const index = snapshots.findIndex(item => item.fetched >= share * last.fetched);
    return [`${Math.round(share * 100)}%`, { index, elapsedMs: snapshots[index].elapsedMs, fetched: snapshots[index].fetched }];
}));

const live = {
    startUrl: crawl.start_url,
    status: CRAWL_STATUS[crawl.status],
    startedAt: iso(start),
    finishedAt: iso(end),
    durationMs,
    intervalMs: STEP_MS,
    options: {
        maxUrls: options.maxUrls, maxDepth: options.maxDepth, concurrency: options.concurrency,
        maxRequestsPerSecond: options.maxRequestsPerSecond, respectRobotsTxt: options.respectRobotsTxt
    },
    method: [
        'Replayed from the stored per-URL fetch times; nothing here is invented.',
        'fetched (crawled) counts a response once its fetch ended: responses.fetched_at + response_ms.',
        'discovered (found) counts a URL once the page that first linked to it was recorded; the start URL at 0 s.',
        'inFlight = fetches started and not yet ended; queued = discovered - skipped - started.',
        `skipped: URLs found but never fetched, counted when found. By reason: ${JSON.stringify(skipReasons)}.`,
        'pagesPerSecond: responses per second over the last 5 s, as the engine reports it. pages: the library\'s page count so far.',
        'Field names match the sidecar\'s progress event; statusClasses and statusCodes are the breakdown so far.'
    ],
    alerts: [],
    alertsNote: 'Alerts are not stored in crawl files: the sidecar keeps them in memory while it runs (E4 persists them), so this crawl\'s alerts are lost.',
    markers,
    final: last,
    snapshots
};

if (negativeQueue < 0) {
    live.method.push(`queued was clamped at 0 where fetches overlapped the recording of the page that found them (lowest raw value ${negativeQueue}).`);
}

const size = writeJson(join(settings.out, 'garden', 'live.json'), live);
console.log(`garden/live.json: ${snapshots.length.toLocaleString('en-GB')} snapshots over ${(durationMs / 60_000).toFixed(1)} min, ${size.toLocaleString('en-GB')} bytes`);
console.log('final', JSON.stringify(last));
console.log('markers', JSON.stringify(markers));
