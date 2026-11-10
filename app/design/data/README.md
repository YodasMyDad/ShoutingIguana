# Design fixtures from real crawls

Data for stage 3 of [the UI brief](../../../docs/v2-ui-brief.md#how-to-work) (static mockups) and the mock data layer
of stage 4 (the clickable prototype). Every number comes from two real crawl files, read by the engine's own sidecar
or straight from SQLite. Nothing is made up, rounded for looks or filled in.

- `extract/` holds the scripts, and is committed.
- `fixtures/` is what they write. It's ignored by git: regenerate it, never edit it.
- The logo copy is at `../assets/logo.png`.

## The crawls

| Fixture name | File | What |
| --- | --- | --- |
| `garden` | `www.gardenfurnituresets.co.uk-20261031.sicrawl` (17 MB) | A real UK shop: 4,521 URLs fetched, 265 pages, HTML stored. It has no sitemap: `/sitemap.xml` answers 404. The main fixture |
| `synthetic` | `synthetic-100k.sicrawl` (798 MB) | The generated 100k-page site the engine benchmarks on: 110,251 URLs fetched, 101,107 pages. For scale |

Both files live in `%LOCALAPPDATA%\ShoutingIguana\dev-crawls\`, outside git. The scripts only ever read them: they
copy them into a work folder and open the copies. Both copies are analysed again by today's engine (85 checks) before
anything is saved:

- the garden file was analysed on 31 Oct by an older build;
- the 100k file was analysed before the 85th check (`duplicate-site-version`) existed.

## Regenerate

You need Node 22.5 or later (tested on 24.22: `node:sqlite` is built in, so there are no npm packages) and a build
of the sidecar.

```powershell
dotnet build engine/src/ShoutingIguana.Sidecar -c Release
node app/design/data/extract/run-all.mjs
```

It takes under 20 s here, about 10 s of it the 100k analysis in `sidecar-api.mjs`. Every step also runs on its own,
in this order. Each later step needs the ones before it.

| Step | Does |
| --- | --- |
| `copy-crawls.mjs` | Copies both crawl files, with any `-wal` and `-shm`, into `<work>/crawls` |
| `sidecar-api.mjs` | Starts `si-sidecar` on the copies, as the contract's process section says: a token in `SI_SIDECAR_TOKEN`, the ready line on stdout, a `127.0.0.1:<port>` Host, and `--data-dir`/`--state-dir` in the work folder, so your real library and defaults are never touched. It analyses both copies, records the event stream meanwhile, saves `fixtures/api/`, spot-checks a few contract rules, then `POST /api/shutdown` |
| `urls.mjs` | `garden/urls.json` and `synthetic/urls-sample.json`, from the analysed copies with `node:sqlite` |
| `sitemaps-robots.mjs` | Starts `si-sidecar` on the copies again (a state folder of its own) and saves, per crawl, `api/<crawl>/sitemaps.json`, `sitemap-urls.json`, `robots.json`, `robots-files.json` and `robots-blocked.json`. It needs `urls.mjs`'s fixtures, and analyses a copy this engine hasn't |
| `inspector.mjs` | `garden/inspector/*.json` |
| `live.mjs` | `garden/live.json` |
| `brand.mjs` | `brand.json` and `app/design/assets/logo.png` |
| `summary.mjs` | Checks the fixtures against each other, and exits 1 on any mismatch. Then prints the summary below, and writes it to `fixtures/summary.md` |

Options, all optional, are the same for every step (`--help` lists them):

| Option | Environment variable | Default |
| --- | --- | --- |
| `--source <dir>` | `SI_DESIGN_SOURCE` | `%LOCALAPPDATA%\ShoutingIguana\dev-crawls` |
| `--work <dir>` | `SI_DESIGN_WORK` | `%TEMP%\si-design-data` |
| `--out <dir>` | `SI_DESIGN_OUT` | `app/design/data/fixtures` |
| `--sidecar <path>` | `SI_SIDECAR_EXE` | `engine/src/ShoutingIguana.Sidecar/bin/Release` (or `Debug`) `/net10.0/si-sidecar.exe` |
| `--garden <file>`, `--synthetic <file>` | | the two file names above |
| `--no-analyze-synthetic` | | Skips the 100k analysis (`sidecar-api.mjs`) |

The work folder also gets `sidecar-stderr.log` and `sidecar-findings.json`, where contract mismatches are noted. The
last run found none.

Values that change from run to run:

- `analyzedAt` and `durationMs`;
- the port and `pid`;
- `path`, `dataDir` and `stateDir`, which are the work folder's;
- crawl ids, which hash the copy's path.

Everything else is stable.

## Fixtures

About 11.2 MB in 122 files.

| Path | Size | What |
| --- | ---: | --- |
| `api/health.json` | 0.4 KB | `GET /api/health` |
| `api/checks.json` | 25 KB | `GET /api/checks`: 17 categories, 85 checks with title, description and fix |
| `api/crawl-options-defaults.json` | 1 KB | `GET /api/crawl-options/defaults` with a fresh state folder, so the engine's defaults and the 3 speed presets |
| `api/crawls.json` | 1.3 KB | `GET /api/crawls`: the library, both crawls, newest first |
| `api/events-sample.txt` | 0.4 KB | The raw `GET /api/events` stream while both copies were analysed |
| `api/<crawl>/crawl.json` | 1 KB | `GET /api/crawls/{id}` |
| `api/<crawl>/analysis.json` | 4 KB | `GET /api/crawls/{id}/analysis` |
| `api/<crawl>/stats.json` | 1 KB | `GET /api/crawls/{id}/stats` |
| `api/<crawl>/options.json` | 1 KB | `GET /api/crawls/{id}/options` |
| `api/<crawl>/alerts.json` | | `GET /api/crawls/{id}/alerts`: `[]` (see the live replay) |
| `api/<crawl>/sitemaps.json` | 0.4 KB, 3.5 KB | `GET /sitemaps`: the garden crawl's one sitemap (404), the 100k crawl's index and the 12 under it |
| `api/<crawl>/sitemap-urls.json` | 18 KB | Per sitemap, the ids of the URL fixture's rows it lists: the mock's grid for one sitemap is those rows (the 100k crawl's URL fixture is a sample, so the mock counts over the sample) |
| `api/<crawl>/robots.json` | 3 KB, 0.5 KB | `POST /robots`, every origin: 17 and 2 |
| `api/<crawl>/robots-files.json` | 189 KB, 1 KB | `POST /robots/file` for each of those origins, by origin: the files as the crawl kept them, line by line |
| `api/<crawl>/robots-blocked.json` | 0.4 KB, 1.3 KB | `POST /robots/blocked` for each origin that blocked URLs, by origin (up to 1,000 rows each) |
| `api/garden/issues/<checkId>.json` | 134 KB | 19 files, one per check with issues, with **every** row (paged at the API's limit of 1,000 and joined) |
| `api/synthetic/issues/<checkId>.json` | 327 KB | 76 files, the first 200 rows of each, with the real `total` |
| `garden/urls.json` | 5.9 MB | One row per fetched URL: all 4,521 |
| `synthetic/urls-sample.json` | 2.6 MB | The same columns for the first 2,000 fetched URLs by id, with `total` 110,251 |
| `garden/inspector/<slug>.json` | 1.2 MB | Everything the inspector shows, for six URLs. `index.json` lists them |
| `garden/live.json` | 0.9 MB | The crawl replayed second by second: 2,246 snapshots |
| `brand.json` | 1 KB | The logo's blue and the iguana's dark, sampled from `Assets/logo.png` |
| `summary.md` | 4 KB | The summary below |

### `api/`: real sidecar responses

These are saved exactly as the contract ([v2-sidecar-api.md](../../../docs/v2-sidecar-api.md)) shapes them, from a
Release build of `si-sidecar` at `e5223b6`. They're laid out one array item per line, which changes only whitespace.
The prototype can serve them as they are.

| Crawl | Checks run | Skipped, with the analysis's note | Checks with issues |
| --- | --- | --- | --- |
| Garden | 82 of 85 | `orphan-page` and `not-in-sitemap`: no sitemap could be read. `duplicate-site-version`: the site's other versions weren't all checked. The crawl stored no probes of them: it predates the probe | 19: 2 errors, 6 warnings, 11 notices |
| 100k | 84 of 85 | `duplicate-site-version`, for the same reason | 76: 14 errors, 34 warnings, 28 notices |

The analysis used the engine's default settings, which are stored with it:

- titles from 30 to 60 characters, and descriptions from 70 to 155;
- at least 200 words;
- images up to 200 KB;
- responses up to 1,000 ms;
- URLs up to 115 characters.

What to know when reading the responses:

- **`urlCount` means two things.**
  - In `crawls.json` and `crawl.json` it's URLs fetched: 4,521. That's the library's number.
  - In `stats.json` it's every URL in the file, fetched or not: 6,286. Show the first as "URLs".
  - `stats.internalUrlCount` (6,246) also counts unfetched URLs.
- **`stats.json`'s page numbers count pages only.** `htmlPageCount`, `indexablePageCount`, `averageResponseMs` (ms)
  and `totalBytes` (bytes transferred) are over the 265 pages. `nonIndexableReasons` counts internal URLs.
- **`issues` in the library counts checks, not issues.** It's checks with at least one issue, by severity. The issue
  rows (`{ url, detail, relatedUrl }`) are in crawl order. `detail` is the engine's text, for example
  `"5,476 ms (first byte after 5,475 ms)"` or `"1 broken URL"`. `relatedUrl` is the other URL involved, such as the
  broken target or the canonical.
- **Skipped checks have a `total` of `null`, never 0.** `analysis.json` lists them under `skipped`, with
  `reason: null` until E2. The notes explain them in plain words.
- **No alerts were stored.** Alerts are kept in memory from when the sidecar starts (E4 persists them), so a freshly
  started sidecar returns `[]`.
- **The event stream carried only analysis events.** `events-sample.txt` holds:
  - `hello` (`{"live":[]}`);
  - for each crawl in turn: `crawl` with `analyzing`, `crawl` with `completed`, then `library`.

  There are no `progress` events, because no crawl was live, and no `analysis` events (E4). To mock live progress,
  use `garden/live.json`, whose snapshots have the `progress` event's fields.

### `garden/urls.json` and `synthetic/urls-sample.json`: the URL grid

```text
{ "crawl": { "startUrl", "startedAt", "finishedAt" }, "total": 4521, "discovered": 6286, "notFetched": 1765, "pages": 265,
  "rows": [ { "id": 1, "url": "https://www.gardenfurnituresets.co.uk/", … } ] }
```

One row per **fetched** URL, in id order (the order the crawl found them). `total` is the library's `urlCount`.
The 1,765 URLs found but never fetched aren't rows: they're nofollow links the crawl didn't follow.

In the 100k sample, all of the first 2,000 rows are 2xx HTML pages, with 27 canonicalised and 19 noindex. That's how
the generator numbers its site. Use the API issue files for the 100k crawl's variety.

Where the values come from:

- **Stored:** the value is in the crawl file as the engine wrote it.
- **Engine logic:** the value is computed in the script, mirroring the engine file named.
- **Derived:** the value is computed in the script for display.

Times are in ms, sizes in bytes, timestamps in ISO 8601 UTC. `null` means not applicable or unknown, never 0.

| Field | Meaning | Source |
| --- | --- | --- |
| `id` | The URL's id in the crawl file | stored |
| `url` | The URL, normalised | stored |
| `finalUrl`, `finalStatusCode` | Where the redirect chain ends, and its status. Both are `null` when the URL doesn't redirect; the status is also `null` when the end wasn't fetched | derived, by following stored redirects hop by hop |
| `redirectUrl` | The first hop's target | stored |
| `redirectHops` | Redirects to reach `finalUrl`. 0 means not a redirect | derived |
| `isInternal` | On the crawled site | stored |
| `kind` | `page` or `resource`. Judged by the content type of a successful fetch, otherwise by the extension. PDFs and unknown files count as pages | engine logic (`Analysis/ResourceTypes.cs`) |
| `type` | `html`, `image`, `css`, `javascript`, `font`, `video`, `audio`, `pdf`, `xml`, `json`, `text` or `other`. `null` if unknown. Taken from the content type if the fetch succeeded, else the extension, else the error page's content type | derived |
| `isPage` | The library's page: internal HTML, fetched successfully and parsed. True for 265 rows, the `pageCount` | engine logic (`Storage/CrawlSummaryReader.cs`) |
| `fetchMode` | `crawl` (fetched, parsed, links followed) or `check` (status only: resources and externals) | stored |
| `statusCode` | HTTP status. `null` is "No response" | stored |
| `statusClass` | `2xx`, `3xx`, `4xx`, `5xx` or `noResponse` | derived |
| `outcome` | The engine's verdict: `success`, `redirect`, `clientError`, `serverError`, `blocked` (firewall or bot protection), `robotsDisallowed`, `timeout`, `networkError` or `processingError` | stored |
| `contentType` | The `Content-Type` header, as sent | stored |
| `depth` | Clicks from the start URL. `null` means "Not linked" (only in a sitemap) | stored |
| `responseMs`, `ttfbMs` | Total response time, and time to first byte | stored |
| `sizeBytes` | Bytes transferred (compressed), or the `Content-Length` of a status check. `null` if unknown | stored |
| `bodyBytes` | The body's size once decompressed. `null` when it wasn't downloaded | stored |
| `httpVersion` | `1.1` or `2.0` | stored |
| `title`, `titleLength`, `titleCount` | The first title, its length, and how many title elements there are | stored, with the length by engine logic (`Analysis/Rules/DisplayWidth.cs`) |
| `metaDescription`, `metaDescriptionLength`, `metaDescriptionCount` | The same, for the meta description | stored, with the length by engine logic |
| `h1`, `h1Count`, `h2`, `h2Count` | The first H1 and H2, and how many of each | stored (`h2` from the page facts) |
| `wordCount` | Words in the page's text | stored |
| `canonicalUrl`, `canonicalStatus` | The canonical from the head or a `Link` header (one in the body is ignored, as Google does). Status is `none`, `self` or `other` | stored |
| `indexable`, `indexabilityReason` | As the analysis judged it. The reason is one of: `none` (indexable), `noindex`, `canonicalised`, `redirected`, `clientError`, `serverError`, `blockedByRobots`, `blocked`, `unreachable` | stored (the engine's `COALESCE` of `effective_indexability` and `responses.indexability_reason`, as `CrawlStatsReader` reads it) |
| `noindexSource` | `meta`, `header` (X-Robots-Tag) or `both`. Only set when the reason is `noindex` | engine logic (`Parsing/RobotsMetaParser.cs`); the engine stores only that it's noindex |
| `isNoindex`, `isNofollow`, `robotsDirectives` | The robots directives that apply to the crawler and Googlebot, from meta tags and headers. Directives are `null` for external URLs | stored |
| `language` | The page's declared language | stored |
| `inlinks`, `uniqueInlinks` | Hyperlinks to this URL from other URLs: every link, and the distinct linking URLs | derived from the links table |
| `allInlinks` | Links of every kind to this URL: hyperlinks, images, scripts, stylesheets, icons, canonicals, redirects. Use this for resources, whose `inlinks` is usually 0 | derived from the links table |
| `outlinks`, `uniqueOutlinks`, `externalOutlinks` | Hyperlinks from this URL to other URLs: every link, distinct targets, and those to external URLs. Links within the same page aren't counted | derived from the links table |
| `allOutlinks` | Links of every kind from this URL to other URLs | derived from the links table |
| `imageCount`, `imagesMissingAlt` | Images on the page, and how many have no `alt` attribute | stored page facts |
| `structuredDataTypes` | JSON-LD `@type` values, such as `Product` or `Organization` | derived from the stored JSON-LD |
| `hreflangCount` | Hreflang annotations. 0 on every garden page | stored page facts |
| `issueCount`, `issues` | The checks that flagged this URL, by id. Titles and severities are in `api/checks.json` | stored |
| `inSitemap` | Listed in a sitemap the crawl read. False on every garden row: the site has no sitemap | stored |
| `source`, `foundOn`, `foundVia` | How the URL was found: `source` is `seed`, `link`, `sitemap` or `siteVersion`. Then where it was first found, and through which kind of link | stored |
| `blockReason`, `error` | Why a fetch was blocked or failed, in the engine's words | stored |
| `crawledAt` | When its fetch started | stored |

- Title, description, H1 and H2 values are `null` for anything that isn't parsed HTML.
- Lengths count characters as people see them (an emoji is one), after trimming. The engine judges length by display
  width, which is the same for Latin text: here, it flags a title over 60 or under 30, and a description over 155
  or under 70.
- Indexability is stored for every fetched URL, but the Overview's numbers count internal URLs only.

### `garden/inspector/<slug>.json`: the inspector

| Slug | URL | Why |
| --- | --- | --- |
| `home` | `/` | The start URL: 792 hyperlinks in from 264 pages, 110 links out of every kind, and 5 issues, one a broken link |
| `category` | `/rattan-furniture/rattan-fire-pit-sets/` | A category page that links to a 404 (`broken-internal-links`). Its source is truncated (see below) |
| `product` | `/harpswell-metal-patio-set/` | A product page with `Product` JSON-LD and 5 issues |
| `redirect` | `https://amzn.to/44j8xD6` | A two-hop chain: 301 to amazon.com, then 302 to amazon.co.uk, which answers 200. The crawl has **no internal redirects**. All 16 are external: 11 amzn.to short links, 4 amazon.com and 1 google.com |
| `client-error` | `/outdoor-pe-rattan-dining-set-with-fire-pit-table/` | A 404, linked only from the category page above, so you can walk from one to the other |
| `non-indexable` | `/blog/?p=2` | The crawl's one non-indexable page: canonicalised to `/blog/` |

Each file has:

- `slug`, `role` and `crawl: { startUrl }`.
- `summary`: the URL's row from `urls.json`, field for field.
- `issues: { total, rows: [{ checkId, title, severity, category, detail, relatedUrl }] }`, errors first.
  `issuesNamingThisUrl` lists issues on other URLs whose `relatedUrl` is this one, such as the page linking to the
  404.
- `inlinks: { total, uniqueUrls, hyperlinks, byKind, rows }` and
  `outlinks: { total, uniqueUrls, hyperlinks, external, linksToItself, byKind, rows }`. The rows are the first 100.
  Totals count links of every kind between different URLs.
  - Inlink rows: `fromId`, `fromUrl`, `kind`, `anchorText`, `position`, `flags`, `fragment`, `fromStatusCode` and
    `fromIndexable`.
  - Outlink rows: `toId`, `toUrl`, `isInternal`, `kind`, `anchorText`, `position`, `flags`, `fragment`, `fetched`,
    `statusCode`, `statusClass`, `outcome`, `indexable`, `indexabilityReason` and `notFetchedBecause` (such as
    `noFollow`).
  - `anchorText` is the engine's stored link text: the anchor text of a hyperlink, the alt text of an image, or the
    language code of an hreflang link.
  - `position` is `content`, `navigation`, `header`, `footer`, `sidebar` or `head`.
  - `flags` can include `noFollow`, `sponsored`, `ugc`, `imageLink`, `outsideHead`, `samePage` and
    `fromHttpHeader`.
- `redirects: { chain, redirectsHere }`.
  - `chain` is `null` unless the URL redirects. Otherwise it lists hop by hop: `{ hop, id, url, fetched, statusCode,
    outcome, redirectsVia: "http" | "metaRefresh" | null, loop }`.
  - `redirectsHere` lists the URLs that redirect to this one.
- `headers: { stored, rows: [{ name, value }] }`, as the engine stored them. It lowercases the names, and stores
  headers for internal URLs only, so the external redirect has none.
- `page`: the parsed page facts. That's titles, meta descriptions, every meta tag, canonicals, robots directives, meta
  refresh, language, charset, base href, word count, the heading outline (`{ level, text }`), and images:
  `{ total, missingAlt, rows }`, the first 100. It's `null` for anything that isn't a parsed page.
- `structuredData: [{ index, valid, types, json, raw }]`, one per JSON-LD block. The home page has `Organization`
  and the product page `Product`.
- `hreflang`: an empty list on all six. The site has no hreflang.
- `source: { stored, bytes, truncated, keptBytes, html }`.
  - The engine stores HTML for pages fetched successfully, so the 404 and the redirect have none.
  - **Sources over 300 KB are truncated to the first 307,200 bytes.** Only the category page is: 453,943 bytes cut to
    307,200, with `truncated: true`.
  - The others are 231,900 (home), 220,480 (product) and 179,515 (non-indexable) bytes, kept whole.

### `garden/live.json`: the live crawl, replayed

The crawl file stores, for every fetched URL, when its fetch started (`responses.fetched_at`, in ms) and how long it
took. It also stores which page each URL was first found on. From that, `live.mjs` rebuilds the crawl's progress for
every second of its 37 min 25 s.

`snapshots` are `progress` events as the sidecar sends them:
`{ status, elapsedMs, discovered, queued, inFlight, fetched, htmlPages, redirects, errors, blocked, robotsDisallowed,
skipped, bytesDownloaded, pagesPerSecond }`. Each also has:

- `at`: the wall-clock time;
- `pages`: the library's page count so far;
- `statusClasses`: `{ 2xx, 3xx, 4xx, 5xx, noResponse }` so far;
- `statusCodes`: so far, by exact code.

In the IA's words, crawled is `fetched` and found is `discovered`.

- **`fetched`** counts a URL when its fetch ended: `fetched_at + response_ms`. That's when the engine records it.
- **`discovered`** counts a URL when the page that first linked to it was recorded. The start URL counts at 0 s.
- **`inFlight`** is fetches started but not ended.
- **`queued`** is discovered, minus skipped, minus started. It never went below 0, so nothing was clamped.
- **`skipped`** counts the 1,765 nofollow URLs as they're found.
- **The counters** follow the engine's own: `Crawling/CrawlCounters.cs`, and the restore on resume in
  `Storage/CrawlWriter.cs`.
- **`pagesPerSecond`** is responses per second over the last 5 s, as `Crawling/CrawlSession.cs` computes it. Despite
  the name, it counts every URL fetched.
- **The final snapshot** matches the crawl exactly: 4,521 fetched, 6,286 found, 1,765 skipped, 265 pages, and the same
  status codes as `stats.json`. `summary.mjs` checks this.

Two things are approximate:

- the first fetch from each host waited for that host's robots.txt first, and that wait isn't stored, so it's
  counted as fetched a little early;
- a URL first found through a nofollow link and later followed counts as queued from when it was first found.

**Alerts are not stored in crawl files**, so `alerts` is empty and none are invented.

`markers` gives the snapshot index where 10%, 25%, 50%, 60%, 75% and 90% of the fetches were done. For a mockup
"60% through", use `snapshots[markers["60%"].index]`.

`options` holds the crawl's limits and pace:

- `maxUrls` 10,000, which the crawl never reached;
- `concurrency` 2;
- `maxRequestsPerSecond` 2, the Polite preset.

### `brand.json` and `app/design/assets/logo.png`

| Name | Hex | RGB | Where |
| --- | --- | --- | --- |
| `logoBlue` | `#2B93FF` | 43, 147, 255 | The hexagon outline |
| `iguanaDark` | `#162832` | 22, 40, 50 | The iguana |

The logo is an 895 × 895 RGBA PNG with an embedded sRGB IEC61966-2.1 profile, and its flat areas carry a little
noise: channels vary by a level or two. So each colour is the per-channel median of the solid pixels: opaque, and
within 8 levels of every neighbour within 2 px. That rules out the anti-aliased edges.

- **Blue:** 15,884 solid pixels. `#2B93FF` is also the most common exact value. The 5th to 95th percentile runs from
  `#2991FF` to `#2E95FF`.
- **Dark:** 86,506 solid pixels, from `#142630` to `#172A34`.

Outside the hexagon the logo is transparent, and inside it's white.

## The garden crawl at a glance

Generated by `summary.mjs` from the fixtures above, which it first checks against each other. It checks, for example:

- that the URL rows agree with `stats.json` on status codes, outcomes, indexability, average response and bytes;
- that every issue file has its full count;
- that the live replay ends where the crawl did.

The analysis time is this run's. After regenerating, paste `fixtures/summary.md` over the text below.

**`https://www.gardenfurnituresets.co.uk/`**, crawled 31 Oct 2026, 14:13 to 14:51 UTC (37 min 25 s) at the Polite pace: 2 at a time, up to 2 requests a second. Analysed 1 Nov 2026, 17:10 UTC by engine 2.0.0-alpha.1. 82 of 85 checks ran; 3 were skipped (`orphan-page`, `not-in-sitemap`, `duplicate-site-version`).

| URLs | Count |
| --- | ---: |
| Found (discovered) | 6,286 |
| Fetched: the library's "URLs" | 4,521 |
| Found but not fetched (noFollow: 1,765) | 1,765 |
| Pages (internal HTML, fetched and parsed) | 265 |
| Resources (4,203 image, 4 javascript, 1 css) | 4,208 |
| Fetched internal / external | 4,481 / 40 |
| Links stored | 21,914 |

| Status of fetched URLs | URLs | Codes |
| --- | ---: | --- |
| 2xx | 4,492 | 200 × 4,492 |
| 3xx | 16 | 301 × 12, 302 × 4: 16 external redirect |
| 4xx | 11 | 403 × 2, 404 × 9: 9 internal clientError, 2 external blocked |
| 5xx | 0 | |
| No response | 2 | 2 external robotsDisallowed |

**Indexability.** 264 of 265 pages are indexable. The one non-indexable page is `/blog/?p=2`, canonicalised to `/blog/`. Non-indexable internal URLs by reason: canonicalised 1, clientError 9.

**Issues.** 19 checks found issues: 2 errors, 6 warnings, 11 notices (the library's `issues`). There are 933 issues in all, one per URL and check: 14 error, 90 warning, 829 notice. 63 checks ran clean.

| # | Check | Severity | URLs |
| ---: | --- | --- | ---: |
| 1 | Internal links without anchor text (`empty-anchor-text`) | Notice | 265 |
| 2 | Images without width and height attributes (`image-missing-dimensions`) | Notice | 230 |
| 3 | Meta description too long (`description-too-long`) | Notice | 210 |
| 4 | Title too long (`title-too-long`) | Notice | 86 |
| 5 | Large images (`image-too-large`) | Warning | 61 |
| 6 | Missing meta description (`description-missing`) | Warning | 11 |
| 7 | Open Graph tags missing (`open-graph-missing`) | Notice | 11 |
| 8 | Internal URL returns 4xx (`internal-client-error`) | Error | 9 |
| 9 | Title too short (`title-too-short`) | Notice | 9 |
| 10 | Missing canonical (`canonical-missing`) | Notice | 8 |
| 11 | Duplicate meta description (`description-duplicate`) | Warning | 8 |
| 12 | Low word count (`low-word-count`) | Notice | 7 |
| 13 | Duplicate title (`title-duplicate`) | Warning | 6 |
| 14 | Links to broken internal URLs (`broken-internal-links`) | Error | 5 |
| 15 | Slow page (`slow-response`) | Warning | 3 |

"Fix first" order (severity, then URLs):

1. Internal URL returns 4xx (9)
2. Links to broken internal URLs (5)
3. Large images (61)
4. Missing meta description (11)
5. Duplicate meta description (8)
6. Duplicate title (6)
7. Slow page (3)
8. Missing H1 (1)
9. Then the notices.

The 4 checks outside the top 15, each with 1 URL, are:

- `h1-missing` (Warning);
- `canonicalised-page`, `security-headers-missing` and `sitemap-missing` (Notice).

**Response times (pages).** Average 198 ms, median 128 ms; 3 pages took over 1 s. Slowest:

| Page | Response | First byte |
| --- | ---: | ---: |
| `/login/?register=true` | 5,476 ms | 5,475 ms |
| `/burford-natural-table/` | 5,217 ms | 5,214 ms |
| `/bayworth-corner-sofa-set/` | 4,632 ms | 4,624 ms |
| `/broughton-2-seater-wooden-garden-bench/` | 405 ms | 401 ms |
| `/silverlake-garden-storage-shed/` | 400 ms | 395 ms |

**Depth.** The deepest URL is 5 clicks away (39 URLs, all images); the deepest page is 4. Pages by depth: 0: 1, 1: 52, 2: 180, 3: 29, 4: 3.

**Sitemaps.** None found, so the orphan and sitemap checks were skipped.

**Live replay.** The median rate was 2 URLs a second (the cap was 2), and the queue peaked at 3,410. At 60% (22 min 26 s in): 2,713 fetched, 5,722 found, 1,417 queued, 241 pages.

**The 100k crawl** (`http://127.0.0.1:50196/`, a generated site): 110,251 URLs fetched, 101,107 pages. 76 checks found issues: 14 errors, 34 warnings, 28 notices.
