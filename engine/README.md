# Shouting Iguana engine (v2)

The cross-platform crawl engine that replaces the WPF app in `src/`. It is headless .NET 10: today it runs from
the command line, and it becomes the backend for the desktop app (Tauri + React) and the Search Console /
Analytics / Ads integrations later.

## Projects

| Project | What it is |
| --- | --- |
| `src/ShoutingIguana.Engine` | Crawler library: fetching, robots.txt, sitemaps, parsing, storage, SEO analysis |
| `src/ShoutingIguana.Cli` | `si` command-line tool: `crawl`, `analyze`, `issues`, `stats`, `testsite`, `bench` |
| `src/ShoutingIguana.TestSite` | Deterministic synthetic shop with planted SEO issues, for tests and benchmarks |
| `tests/ShoutingIguana.Engine.Tests` | xUnit: unit tests plus end-to-end crawls and analyses of the synthetic site |
| `tests/ShoutingIguana.Cli.Tests` | xUnit: the `si` commands run in-process against the synthetic site, exit codes and output included |

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

Exit codes, for scripts and schedulers:

| Code | Meaning |
| --- | --- |
| 0 | Done |
| 1 | The crawl or analysis failed |
| 2 | Bad arguments, or a file that can't be used |
| 3 | The crawl stopped itself because the site stopped answering; continue it with `--resume` |
| 130 | Stopped with Ctrl+C |

## How a crawl works

1. The start URL (and every sitemap URL) goes into the `urls` table of the crawl's SQLite file.
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

When a crawl completes, `CrawlAnalyzer` runs 84 checks over what was stored and writes the issues into the same
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
  `issue_types`, the summary is partial, and the analysis notes (`analysis_notes`, `AnalysisSummary.Notes`) say why.
  - The orphan check needs every page that could link to a sitemap page, and a sitemap to compare with. So a
    stopped, depth- or URL-limited, or folder-scoped crawl skips it, as does one with sitemaps off or none read.
  - Pages missing from the sitemaps need every sitemap read. So that check is skipped while a declared sitemap
    fails for a reason that says nothing about its contents (a 5xx, 429, 403, timeout or bad XML), or is left
    unread at the crawler's limits. A 404 or 410 sitemap simply doesn't exist, so it doesn't stop the check, and
    neither failure counts as a sitemap error.
  - Checks of what the crawl was told not to fetch are skipped: broken external links with `--no-external`, broken
    and oversized images with `--no-resources`, and every sitemap check with `--no-sitemaps`.
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
- **Thresholds.** `AnalysisSettings` holds every limit (title 30–60 characters, description 70–155, 200
  words, images over 200 KB, pages slower than 1 s...). Each analysis stores the values it used. Title and
  description lengths count full-width (CJK) characters and emoji as two, as they take about twice the room.
- **Atomic.** The issues are replaced in one transaction, so a failed or cancelled analysis keeps the
  previous results.

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
| `urls` | Every URL seen once: scope, depth, crawl state, why it was skipped, where it was found |
| `responses` | One row per fetched URL: status, timings, sizes, title, description, H1, canonical, indexability, content hashes |
| `response_details` | Brotli-compressed JSON of the headers and full `PageFacts` (headings, meta tags, images, hreflang, JSON-LD, ids) |
| `response_html` | Compressed HTML, only with `--store-html` |
| `links` | Every link: source, target, kind (link/image/script/canonical/hreflang/redirect...), rel flags, page region, anchor text |
| `robots`, `sitemaps`, `sitemap_urls` | robots.txt per origin, sitemaps read (under the URL they were found at, which may redirect elsewhere; `is_probe` marks a guess at `/sitemap.xml` when nothing declared one; the first one left unread, past 1,000 sitemaps or nested more than 3 deep, has an error starting "Not read"), and which URLs each listed |
| `issues` | One row per flagged URL and issue: a short detail, and the other URL involved (broken target, duplicate, canonical) |
| `issue_types` | Every check that ran: its title, category, severity, description, fix, and how many URLs it flagged |
| `analysis`, `analysis_notes` | When the last analysis ran, with which engine version and settings, and what it couldn't do and why |
| `effective_indexability` | Indexability as analysed, for URLs where it differs from the crawl's (an instant meta refresh, a canonical leading back to the page) |

URLs that only appear in sitemaps keep depth `1000000` (`CrawlDepth.UNLINKED`), which is how orphan pages show up.

## Synthetic site and benchmark

The synthetic site is clean by construction: every page passes every check unless an issue is planted on it.
Products carry issues at fixed intervals (duplicate titles, broken links, redirect chains, noindex,
canonicalised pages...). `/plants/` holds a test page for each check that a product can't carry (missing title,
conflicting canonicals, broken hreflang, invalid JSON-LD, a slow page, a firewall block, a redirect loop...).
`GetExpectations()` reports exactly what a correct crawl and analysis must find, down to the URLs for every issue id.

The e2e tests also crawl these variants:

- **HTTPS**, with a self-signed certificate. Plain HTTP answers on the same port, as port 80 does on a real site.
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
| 101,088 | 110,203 | 5.05 M | 42 s | 2,424 pages/s | 172 MB | none (−8 MB) | 6 s, 161 MB peak | 406 MB |

Every planted page and issue was found, and nothing else was flagged. The planted slow page, firewall block and
unreachable URL make the crawler back off briefly, as it would on a real site. On real sites, speed is set by the
network and your rate settings, not by the crawler.

## Not yet

- The desktop UI (step 3).
- JavaScript rendering (Playwright, with HTTP / Rendered / Auto modes) and browser fallback for blocked sites,
  plus custom extraction, list mode and include/exclude rules (step 4). `Blocked` responses are already detected.
- Search Console, Analytics, Bing and Ads data (step 5).
