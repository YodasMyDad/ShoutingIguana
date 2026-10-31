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
allowlisting), proxy, depth and URL limits, robots.txt, subdomains, start-folder scope, nofollow, stored HTML, and
`--no-analyze`.

## How a crawl works

1. The start URL (and every sitemap URL) goes into the `urls` table of the crawl's SQLite file.
2. A dispatcher claims queued URLs breadth-first and hands them to two worker pools: one for the site itself and
   one for status checks on other sites.
3. Each worker checks robots.txt and waits on a per-host gate. The gate caps concurrency, applies the optional
   rate cap, and backs off automatically on 429/503 responses and block pages, honouring Retry-After. The worker
   then fetches over plain HTTP without following redirects (every hop is its own URL) and parses the HTML once
   into `PageFacts`.
4. A single writer applies results in batched transactions: the response row, a compressed detail blob, the
   links, and newly discovered URLs. It also owns the frontier, so "nothing queued and nothing in flight" is an
   exact completion test.
5. Stop at any point with Ctrl+C, then continue with `--resume`.

Memory stays flat because the only thing in memory that grows with the crawl is a 128-bit key per URL (about
40 bytes). The frontier, results and links all live on disk.

Sites that block crawlers are reported, not hidden. Cloudflare/Akamai/DataDome/Imperva/Sucuri/CAPTCHA
responses and rate limits are recorded as `Blocked`, with the reason, and raise a notice.

## SEO analysis

When a crawl completes, `CrawlAnalyzer` runs 81 checks over what was stored and writes the issues into the same
file. It never refetches or reparses anything, so any crawl can be analysed again with new rules or thresholds
(`si analyze`). It refuses a crawl that is still running.

- **One pass.** Page rules see each page's stored facts, headers and outgoing links, with the status of every
  link target looked up from an in-memory table indexed by URL id (a few dozen bytes per URL). Site rules
  collect what they need during the same pass (hashes, alternates, fragments) and report at the end:
  duplicates, orphans, redirect chains, hreflang return links, sitemaps, robots.txt.
- **Catalog.** `Analysis/IssueCatalog.cs` defines every issue: a stable id (`title-too-long`), category,
  severity (Error, Warning or Notice), title, why it matters, and how to fix it. Ids are stored in crawl files,
  so never change one.
- **Scope.** Content checks (titles, descriptions, headings, word count, duplicates, Open Graph, URL style,
  rich-result requirements) look only at indexable pages. Technical checks look at every crawled page.
  Site-wide settings (HTTPS, security headers, robots.txt, sitemap files) are reported once, on the home page,
  rather than on every URL.
- **Thresholds.** `AnalysisSettings` holds every limit (title 30–60 characters, description 70–155, 200
  words, images over 200 KB, pages slower than 1 s...). Each analysis stores the values it used.
- **Atomic.** The issues are replaced in one transaction, so a failed or cancelled analysis keeps the
  previous results.

Categories: response codes, redirects, links, indexability, canonicals, sitemaps, titles, meta descriptions,
headings, content, images, hreflang, structured data, security, URLs, performance, HTML.

## Crawl file (`.sicrawl`)

One SQLite file per crawl. It is a single self-contained file once the crawl closes.

| Table | Holds |
| --- | --- |
| `crawl` | Start URL, options (so a resume behaves the same), status, engine version |
| `urls` | Every URL seen once: scope, depth, crawl state, why it was skipped, where it was found |
| `responses` | One row per fetched URL: status, timings, sizes, title, description, H1, canonical, indexability, content hashes |
| `response_details` | Brotli-compressed JSON of the headers and full `PageFacts` (headings, meta tags, images, hreflang, JSON-LD, ids) |
| `response_html` | Compressed HTML, only with `--store-html` |
| `links` | Every link: source, target, kind (link/image/script/canonical/hreflang/redirect...), rel flags, page region, anchor text |
| `robots`, `sitemaps`, `sitemap_urls` | robots.txt per origin, sitemaps read, and which URLs each listed |
| `issues` | One row per flagged URL and issue: a short detail, and the other URL involved (broken target, duplicate, canonical) |
| `issue_types` | Every check that ran: its title, category, severity, description, fix, and how many URLs it flagged |
| `analysis` | When the last analysis ran, with which engine version and settings |

URLs that only appear in sitemaps keep depth `1000000` (`CrawlDepth.UNLINKED`), which is how orphan pages show up.

## Synthetic site and benchmark

The synthetic site is clean by construction: every page passes every check unless an issue is planted on it.
Products carry issues at fixed intervals (duplicate titles, broken links, redirect chains, noindex,
canonicalised pages...). `/plants/` holds a test page for each check that a product can't carry (missing title,
conflicting canonicals, broken hreflang, invalid JSON-LD, a slow page, a firewall block, a redirect loop...).
`GetExpectations()` reports exactly what a correct crawl and analysis must find, including the count for every
issue id.

To add a check: write the rule, add the issue to the catalog, and plant it on the synthetic site with its
expected count. The end-to-end test and the benchmark compare the whole issues table, so a check that flags
anything unplanted fails them.

`si bench` serves the synthetic site from a child process, crawls it, analyses it, and checks both against the
expectations. It can fail on thresholds, and CI runs it on every push (`.github/workflows/engine.yml`).

On a 32-core Windows machine, at concurrency 16, against the local synthetic site:

| Pages | URLs | Links | Crawl | Throughput | Peak memory | Growth over the crawl | Analysis | File |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 101,084 | 110,197 | 5.05 M | 46 s | 2,202 pages/s | 173 MB | none (−3 MB) | 6 s, 166 MB peak | 406 MB |

Every planted page and issue was found, and nothing else was flagged. The planted slow page, firewall block and
unreachable URL make the crawler back off briefly, as it would on a real site. On real sites, speed is set by the
network and your rate settings, not by the crawler.

## Not yet

- The desktop UI (step 3).
- JavaScript rendering (Playwright, with HTTP / Rendered / Auto modes) and browser fallback for blocked sites,
  plus custom extraction, list mode and include/exclude rules (step 4). `Blocked` responses are already detected.
- Search Console, Analytics, Bing and Ads data (step 5).
