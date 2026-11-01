# Shouting Iguana engine (v2)

The cross-platform crawl engine that replaces the WPF app in `src/`. It is headless .NET 10: today it runs from
the command line, and it becomes the backend for the desktop app (Tauri + React) and the Search Console /
Analytics / Ads integrations later.

## Projects

| Project | What it is |
| --- | --- |
| `src/ShoutingIguana.Engine` | Crawler library: fetching, robots.txt, sitemaps, parsing, storage, SEO analysis |
| `src/ShoutingIguana.Cli` | `si` command-line tool: `crawl`, `analyze`, `issues`, `stats`, `testsite`, `bench` |
| `src/ShoutingIguana.Sidecar` | `si-sidecar`: the engine as a localhost HTTP API for the desktop app (see [Sidecar](#sidecar)) |
| `src/ShoutingIguana.TestSite` | Deterministic synthetic shop with planted SEO issues, for tests and benchmarks |
| `tests/ShoutingIguana.Engine.Tests` | xUnit: unit tests plus end-to-end crawls and analyses of the synthetic site |
| `tests/ShoutingIguana.Cli.Tests` | xUnit: the `si` commands run in-process against the synthetic site, exit codes and output included |
| `tests/ShoutingIguana.Sidecar.Tests` | xUnit: the API hosted in-process against the synthetic site, plus the real `si-sidecar` process |

## Build, test, run

```bash
cd engine
dotnet test ShoutingIguana.Engine.slnx

dotnet run --project src/ShoutingIguana.Cli -- crawl https://example.com --db example.sicrawl   # crawls, then analyses
dotnet run --project src/ShoutingIguana.Cli -- issues example.sicrawl                           # what was found
dotnet run --project src/ShoutingIguana.Cli -- issues example.sicrawl title-too-long            # the URLs for one issue
dotnet run --project src/ShoutingIguana.Cli -- issues example.sicrawl --csv issues.csv          # everything, for Excel
dotnet run --project src/ShoutingIguana.Cli -- analyze example.sicrawl                          # re-run the checks
dotnet run --project src/ShoutingIguana.Cli -- stats example.sicrawl
dotnet run --project src/ShoutingIguana.Cli -- crawl --resume --db example.sicrawl              # after Ctrl+C
dotnet run --project src/ShoutingIguana.Cli -c Release -- bench --pages 100000
```

`si crawl --help` lists every option: concurrency and rate cap, user agent presets, extra headers (for firewall
allowlisting; they are only sent to the site being crawled, and never over plain HTTP when it starts on HTTPS),
proxy, depth and URL limits, robots.txt, subdomains, start-folder scope, nofollow, stored HTML, and `--no-analyze`.

The start URL and any `--sitemap` can't carry a user name or password (`https://user:pass@host/`): the crawler never
sends them, and the URL is stored in the crawl file, listed and logged as it is, so they would leak. Send credentials
in an `Authorization` header (`--header`), whose value a crawl file doesn't keep. A `--proxy` may carry credentials.

Exit codes, for scripts and schedulers:

| Code | Meaning |
| --- | --- |
| 0 | Done |
| 1 | The crawl or analysis failed |
| 2 | Bad arguments, or a file that can't be used |
| 3 | The crawl stopped itself because the site stopped answering; continue it with `--resume` |
| 130 | Stopped with Ctrl+C |

## Sidecar

`si-sidecar` serves the engine to the desktop app over HTTP on `127.0.0.1`. The Tauri shell starts it, reads the one
line it writes to stdout, and calls it with a bearer token. The contract (endpoints, events, errors and security) is
[`docs/v2-sidecar-api.md`](../docs/v2-sidecar-api.md).

```bash
dotnet run --project src/ShoutingIguana.Sidecar -- --data-dir ./crawls --state-dir ./si-state
# {"event":"ready","port":53123,"version":"2.0.0-alpha.1","token":"<made up, as no SI_SIDECAR_TOKEN was set>"}
curl -H "Authorization: Bearer <token>" http://127.0.0.1:53123/api/health
```

| Flag | Default | Does |
| --- | --- | --- |
| `--data-dir <folder>` | `Documents/Shouting Iguana` | Where new crawls are saved. The library lists the `.sicrawl` files at its top level, plus files opened from elsewhere |
| `--state-dir <folder>` | `<local app data>/ShoutingIguana` | Holds `library.json`, `defaults.json`, `summaries.json` and `logs/`. One sidecar at a time may use it |
| `--port <number>` | `0`, a free port | A fixed port, for development. The ready line gives the port either way |
| `--stdin-lifeline` | Off | Shut down on a `shutdown` line on stdin, or when stdin ends because the shell has gone |
| `--dev` | Off | Development only: without `SI_SIDECAR_TOKEN`, make a token up and give it in the ready line |

| Variable | Is |
| --- | --- |
| `SI_SIDECAR_TOKEN` | The bearer token, at least 32 characters. The shell makes 32 random bytes as hex for each launch. The sidecar removes it from its environment once read. Required, unless `--dev` is given, when the sidecar makes one up and adds it to the ready line |
| `SI_SIDECAR_ORIGINS` | Comma-separated origins allowed to call the API from a webview: `http://tauri.localhost` (Windows), `tauri://localhost` (macOS, Linux), `http://localhost:<port>` (`tauri dev`). None by default |
| `SI_PARENT_PID` | The shell's process. When it ends, the sidecar shuts down |

- Every request needs the token (401), a `Host` of `127.0.0.1:<port>` or `localhost:<port>` (421), and an allowed
  `Origin` if it sends one (403). Bodies are capped at 1 MB.
- Logs go to stderr and to `<state-dir>/logs/sidecar-<date>.log` (14 days kept), never to stdout.
- A shutdown (`POST /api/shutdown`, Ctrl+C, SIGTERM, the stdin lifeline or the parent ending) stops every crawl,
  leaving it resumable, cancels every analysis, which then stores nothing, and exits 0. It exits 2 for arguments it
  can't use, 1 when it can't start (a port already taken, a state folder it can't create), and 3 when another sidecar
  is already using the state folder.

## How a crawl works

1. The start URL (and every sitemap URL) goes into the `urls` table of the crawl's SQLite file. So does the start URL's
   path on each of the site's other versions (bare domain or `www`, over http or https), claimed right behind it, so even
   a crawl stopped early knows how they answer.
   - These probes are status checks only: never parsed, never followed, and not counted toward the URL limit. Where one
     redirects is checked the same way. So a version that answers with the whole site never gets it crawled twice.
   - They go through robots.txt and the rate gate like any URL, but never hold up a crawl. A probe gets a single try,
     and isn't tried again at the end. Where its origin's robots.txt timed out, it isn't sent at all: it is recorded
     dead with that error.
   - Once the site itself links to one, lists it in a sitemap or redirects to it, it is one of the site's own URLs.
2. A dispatcher claims queued URLs breadth-first and hands them to two worker pools: one for the site itself and
   one for status checks on other sites.
3. Each worker checks robots.txt and waits on a per-site gate. `example.com` and `www.example.com` are one site,
   on http and https alike, so they share one: sites serve one host and redirect the other.
   - The gate caps concurrency and applies the optional rate cap.
   - It backs off automatically on 429/503 responses, block pages and repeated failures, honouring Retry-After. The
     backoff halves every half second, and a request waiting on it starts by the backoff as it stands when it is
     due.

   The worker then fetches over plain HTTP without following redirects (every hop is its own URL) and parses the
   HTML once into `PageFacts`.
   - A robots.txt that can't be read (5xx, 429, no answer) is retried like a page, then means "disallow
     everything" for two minutes, as RFC 9309 requires. An origin other than the start URL's that doesn't answer
     at all (another site, a dead www twin, a closed http port) gets no verdict, so links to it record their real
     network error. That departs from RFC 9309 on purpose, as `docs/v2-rebuild.md` explains.
   - Whether an origin answers is counted per scheme, host and port. After three URLs in a row on one get no
     response, another site's remaining URLs there fail at once ("Not checked"). On the site being crawled,
     robots.txt is asked first: if it answers, those URLs failed for reasons of their own. An origin that has never
     answered, while the site's last answering origin still does, is dead (a www twin, a closed port) and fails fast
     like another site; the URLs it was asked for keep their real error. Any other origin is down: it is waited out
     with one-minute pauses and a notice, never written off, and after ten minutes down (time paused aside) the
     crawl stops, to be resumed.
   - The site's URLs that got no response are tried once more when nothing else can be claimed (at most 10,000 are
     held back for it), handed to the workers as they have room. That comes before the URLs only sitemaps list. URLs
     refused because robots.txt couldn't be read wait for its two-minute cool-down first, as do the sitemaps it
     declares. Sitemaps aren't read while the crawl is paused.
4. A single writer applies results in batched transactions: the response row, a compressed detail blob, the
   links, and newly discovered URLs. It also owns the frontier, so "nothing queued and nothing in flight" is an
   exact completion test.
5. Stop at any point with Ctrl+C, then continue with `--resume`. Progress counts what was actually read:
   `BytesDownloaded` is the bodies fetched, not the sizes that status checks were told.

Memory stays flat because the only thing in memory that grows with the crawl is a 128-bit key per URL (about
40 bytes). The frontier, results and links all live on disk.

Sites that block crawlers are reported, not hidden. Cloudflare/Akamai/DataDome/Imperva/Sucuri/CAPTCHA
responses and rate limits are recorded as `Blocked`, with the reason, and raise a notice. Only challenge and block
pages count: a genuine 403 from a site behind one of those firewalls stays a 403.

## SEO analysis

When a crawl completes, `CrawlAnalyzer` runs 85 checks over what was stored and writes the issues into the same
file. It never refetches or reparses anything, so any crawl can be analysed again with new rules or thresholds
(`si analyze`). It refuses a crawl that is still running. A rule that fails on one page (page content is untrusted)
is logged with the URL and skipped for that page, and the analysis notes say which rule failed on how many pages.

- **One pass.** Page rules see each page's stored facts, headers and outgoing links, with the status of every
  link target looked up from an in-memory table indexed by URL id (a few dozen bytes per URL). Site rules
  collect what they need during the same pass (hashes, alternates, fragments) and report at the end:
  duplicates, orphans, redirect chains, hreflang return links, sitemaps, robots.txt.
- **Catalog.** `Analysis/IssueCatalog.cs` defines every issue: a stable id (`title-too-long`), category,
  severity (Error, Warning or Notice), title, why it matters, and how to fix it. Ids are stored in crawl files,
  so never change one.
- **Scope.** Content checks (titles, descriptions, headings, word count, duplicates, Open Graph, URL style,
  rich-result requirements, hreflang) look only at indexable pages. Technical checks look at every page the
  crawler parsed; status-checked URLs (outside the start folder, iframes) and Cloudflare's `/cdn-cgi/`
  endpoints aren't audited as pages. Site-wide settings (HTTPS, security headers, robots.txt, sitemap files) are
  reported once, on the home page, rather than on every URL.
- **Coverage.** A check the crawl can't support is skipped, not reported clean. Skipped checks are left out of
  `issue_types` and the analysis is partial (`analysis.is_partial`). Each skipped check is stored in `skipped_checks`
  with why: a stable code (`no-sitemap`, `depth-limit`, ... in `SkippedCheckReasons`) and a sentence to show beside
  it ("No sitemap could be read."), the first reason that applied. The analysis notes (`analysis_notes`,
  `AnalysisSummary.Notes`) say it for the analysis as a whole.
  - The orphan check needs every page that could link to a sitemap page, and a sitemap to compare with. So a
    stopped, depth- or URL-limited, or folder-scoped crawl skips it, as does one with sitemaps off or none read.
  - Pages missing from the sitemaps need every sitemap read. So that check is skipped while a declared sitemap
    fails for a reason that says nothing about its contents (a 5xx, 429, 403, timeout or bad XML), or is left
    unread at the crawler's limits. A 404 or 410 sitemap simply doesn't exist, so it doesn't stop the check, and
    neither failure counts as a sitemap error.
  - Checks of what the crawl was told not to fetch are skipped: broken external links with `--no-external`, broken
    and oversized images with `--no-resources`, and every sitemap check with `--no-sitemaps`.
  - The duplicate site version check needs to know how every version answered, and a home page to redirect to. A
    crawl stopped before the probes were answered, a crawl file from before the crawler probed, or a home page that
    isn't a page skips it.
  - A skipped check stores no rows, whatever its rule found on the way.
- **Not checked isn't broken.** A URL the crawler gave up on without asking ("Not checked", because its host had
  stopped answering) counts as never fetched. Links to it aren't broken, and the notes give the number.
- **As search engines see it.** An instant (0-second) meta refresh is a redirect, as Google treats it. A canonical
  whose target leads back to the page (through a redirect) or nowhere doesn't make the page non-indexable. Links and
  images are judged by where their redirects end, so a link to a redirect that ends at a 404 is broken. Where this
  differs from what the crawl recorded, the analysis stores it in `effective_indexability`, which `si stats` reads.
  A sitemap entry must be on the host of a sitemap that lists it, or on the start URL's or home page's host, so `www`
  pages are the site's own on a crawl that starts at the bare domain. A link without text takes its name the way
  screen readers name it (an image's alt, a labelled icon, `aria-labelledby`, `aria-label`, `title`), and a fragment
  matches an id as written or decoded, as browsers do. The pages of a paginated series (`rel=next/prev`) may share an
  H1; their titles and descriptions must still differ.
- **Other sites.** A status many sites only send crawlers (401, 403, 405, 429, 503, 999) doesn't make an external
  link, canonical, alternate or image "broken".
- **Site versions.** The bare domain and `www`, over http and https, are one site to the crawler but separate sites to
  search engines. Each version of the start URL must redirect to the main one, the home page's origin.
  - One that answers with a page, directly or at the end of its redirects on another version, is flagged
    `duplicate-site-version`.
  - One that is dead, fails, is blocked or is disallowed isn't: a dead `www` twin is normal.
  - The probes aren't the site's URLs, so the response-code checks leave them out, and so do the robots.txt checks
    for an origin only probes went to. Once the site links there, they count like any URL.
  - The redirect checks still look at them: a version that redirects temporarily, to http, in a loop or to a broken
    URL is a problem.
- **Thresholds.** `AnalysisSettings` holds every limit (title 30–60 characters, description 70–155, 200
  words, images over 200 KB, pages slower than 1 s...). Each analysis stores the values it used. Title and
  description lengths count full-width (CJK) characters and emoji as two, as they take about twice the room.
- **Atomic.** The issues are replaced in one transaction, so a failed or cancelled analysis keeps the
  previous results.
- **URL summary.** Each analysis rebuilds `url_summary`, the URL grids' table: one row per URL with what the analysis
  judged and the crawl's numbers the grids sort and filter on (see [Crawl file](#crawl-file-sicrawl)). It comes from
  the same URL table the checks use, so the grid and the checks never disagree, and it's written in one streamed pass
  after the issues: about 0.7 s and 14 MB (130 bytes per URL, indexes included) on the 100k crawl.
  `UrlSummaryReader` reads it. A crawl analysed by an earlier engine has none (`IsAvailable` is false) until it's
  analysed again.
- **Out of date.** `AnalysisFreshness.Check` (or `CrawlFileSummary.OutOfDate`) says why an analysis no longer stands:
  - `CrawlChanged`: the crawl row was stamped again (`updated_at`) since the stamp the analysis kept, because the
    crawl was continued. Exact, whatever the clocks did.
  - `EngineChanged`: the analysis ran with another `AnalysisFreshness.VERSION`, or with other checks (the catalog's
    ids, categories and severities, hashed). Bump `VERSION` with any change to what analysing the same crawl stores: a
    rule that judges differently, a default threshold, a column of the URL summary. Every analysis by an engine from
    before the summary is out of date this way.
  - `SettingsChanged`: the stored settings differ from the ones an analysis would run with now. A threshold the
    analysing engine didn't store counts as different.

Categories: response codes, redirects, links, indexability, canonicals, sitemaps, titles, meta descriptions,
headings, content, images, hreflang, structured data, security, URLs, performance, HTML.

## Crawl file (`.sicrawl`)

One SQLite file per crawl. It is a single self-contained file once the crawl closes.

- **One writer at a time.** A crawl or an analysis holds `<crawl file>-lock` while it writes. A second one is
  refused, and an analysis also refuses a crawl that is running or was interrupted. Readers can open the file at
  any time, a running crawl included. Windows deletes the lock file with the lock. On Linux and macOS it stays:
  deleting it there would let two writers lock different files.
- **No secrets.** People share crawl files, so the stored options keep header names without their values, and the
  proxy without its credentials. Resuming asks for them again.

| Table | Holds |
| --- | --- |
| `crawl` | Start URL, options (so a resume behaves the same), status, engine version |
| `urls` | Every URL seen once: scope, depth, crawl state, why it was skipped, where it was found (`source` 3 marks a probe of the site's other versions) |
| `responses` | One row per fetched URL: status, timings, sizes, title, description, H1, canonical, indexability, content hashes |
| `response_details` | Brotli-compressed JSON of the headers and full `PageFacts` (headings, meta tags, images, hreflang, JSON-LD, ids) |
| `response_html` | Compressed HTML, only with `--store-html` |
| `links` | Every link: source, target, kind (link/image/script/canonical/hreflang/redirect...), rel flags, page region, anchor text |
| `robots`, `sitemaps`, `sitemap_urls` | robots.txt per origin, sitemaps read (under the URL they were found at, which may redirect elsewhere; `is_probe` marks a guess at `/sitemap.xml` when nothing declared one; the first one left unread, past 1,000 sitemaps or nested more than 3 deep, has an error starting "Not read"), and which URLs each listed |
| `issues` | One row per flagged URL and issue: a short detail, and the other URL involved (broken target, duplicate, canonical) |
| `issue_types` | Every check that ran: its title, category, severity, description, fix, and how many URLs it flagged |
| `analysis`, `analysis_notes` | When the last analysis ran, with which engine version and settings, whether it was partial, what it ran against (the crawl's `updated_at`, the analysis version, the checks' fingerprint), and what it couldn't do and why |
| `skipped_checks` | Each check the last analysis skipped, with a reason code and a sentence |
| `effective_indexability` | Indexability as analysed, for URLs where it differs from the crawl's (an instant meta refresh, a canonical leading back to the page) |
| `url_summary` | One row per URL for the grids, rebuilt by each analysis: indexable or not and why, where redirects end and the status there, the type (page, image, script, stylesheet, font, media, PDF, other), in a sitemap, inlinks (all, followed, nofollow, linking pages), outlinks, issues by severity, and copies of the URL's depth, status, size, time and word count |

URLs that only appear in sitemaps keep depth `1000000` (`CrawlDepth.UNLINKED`), which is how orphan pages show up.

The analysis tables are created when a crawl is first analysed, so a crawl file from an earlier engine opens as it
always did and gets what it lacks on its next analysis. The crawl's own schema version doesn't change.

### URL summary

What its columns mean:

- **Null is unknown**: a URL the crawl never fetched, or never asked for because its host had stopped answering.
- **Indexability** is the analysis's (`effective_indexability` over the crawl's): `indexable` is 1 when the URL
  answered 2xx and nothing keeps it out of search, otherwise `indexability_reason` says why.
- **Inlinks** are hyperlinks to the URL on the pages audited, each one counted, a page's links to itself left out.
  Nofollow ones are marked rel=nofollow, or are on a page whose robots directives say nofollow. `linking_pages`
  counts the pages.
  Nothing is credited through redirects: these are the links as found. `outlinks` is every hyperlink on the page.
- **Type** is by the content type of a successful response, otherwise by the extension, as the checks tell files from
  pages.

Nine indexes serve the grids' sorts and selective filters: status, indexability reason, type, depth, linking pages,
issues, response time, size and word count. Every index ends with the URL id, which keyset paging needs. The rest
are cheap to scan in a table this narrow.

Measured on the 100k crawl's summary, and on 1M rows made by copying it nine times, at p95, for the first page of
100 rows and for a page half way down (with SQLite 3.49; the engine ships 3.53):

| Query | 100k | 1M |
| --- | --- | --- |
| Sorted by an indexed column, a common filter kept off its index (slowest pages, thinnest pages, most issues) | under 1 ms | under 3 ms |
| A selective filter's index, then sorted (4xx by linking pages, status 500 by response time) | about 1 ms | 6–22 ms |
| Sorted by a column without an index (followed inlinks, outlinks) | 13–24 ms | 100–210 ms |
| Counting the rows a filter matches | up to 15 ms | up to 130 ms |
| Overview breakdowns (indexability, depth, status, response times) | 21–40 ms | 200–390 ms |
| Left to the planner: a common value's index, then sorted (slowest internal pages) | 12–25 ms | 110–330 ms |
| Left to the planner: a rare match found by walking another index (over 500 ms, by size) | 110–220 ms | 1.2–2.0 s |

This SQLite has no STAT4 histograms, so its planner can't tell a common value from a rare one, and stats from
`ANALYZE` didn't change that. So the grids' readers count the matching rows first (the grid shows that count anyway)
and choose. When few rows match, they sort them, keeping the sort's index out of the plan (`ORDER BY +size_bytes`).
Otherwise they walk the sort's index, keeping a common value's index out (`+resource_type = 0`). Both brought the last
two rows under 1 ms.

## Synthetic site and benchmark

The synthetic site is clean by construction: every page passes every check unless an issue is planted on it.
Products carry issues at fixed intervals (duplicate titles, broken links, redirect chains, noindex,
canonicalised pages...). `/plants/` holds a test page for each check that a product can't carry (missing title,
conflicting canonicals, broken hreflang, invalid JSON-LD, a slow page, a firewall block, a redirect loop...).
`GetExpectations()` reports exactly what a correct crawl and analysis must find, down to the URLs for every issue id.

The e2e tests also crawl these variants:

- **HTTPS**, with a self-signed certificate. Plain HTTP answers on the same port, as port 80 does on a real site. It
  serves the home page there instead of redirecting: the planted duplicate version of the site.
- **No sitemaps**, where the checks that need them are skipped.

A second origin serves a partner site, so links, images and sitemap entries on another host are tested too.

The one allowance is for response times, which depend on the machine. Up to 5% of pages (at least 25) may be
flagged slow besides the planted one, and the e2e test checks the stored times directly.

To add a check: write the rule, add the issue to the catalog, and plant it on the synthetic site. The end-to-end
test and the benchmark compare the whole issues table row by row, so a check that flags anything unplanted fails
them.

`si bench` serves the synthetic site from a child process, crawls it, analyses it, and checks both against the
expectations. It can fail on thresholds, and CI runs it on every push (`.github/workflows/engine.yml`).

On a 32-core Windows machine, at concurrency 16, against the local synthetic site:

| Pages | URLs | Links | Crawl | Throughput | Peak memory | Growth over the crawl | Analysis | File |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 101,107 | 110,251 | 5.05 M | 35 s | 2,916 pages/s | 168 MB | none (−1 MB) | 7 s, 157 MB peak | 400 MB |

This was measured on `68d55a2`. Since then, the crawl also probes the site's other versions, which adds one URL
(110,252).

Every planted page and issue was found, and nothing else was flagged. The planted slow page, firewall block and
unreachable URL make the crawler back off briefly, as it would on a real site. On real sites, speed is set by the
network and your rate settings, not by the crawler.

## Not yet

- The desktop UI (step 3).
- JavaScript rendering (Playwright, with HTTP / Rendered / Auto modes) and browser fallback for blocked sites,
  plus custom extraction, list mode and include/exclude rules (step 4). `Blocked` responses are already detected.
- Search Console, Analytics, Bing and Ads data (step 5).
