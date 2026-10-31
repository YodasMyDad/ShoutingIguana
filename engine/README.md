# Shouting Iguana engine (v2)

The cross-platform crawl engine that replaces the WPF app in `src/`. It is headless .NET 10: today it runs from
the command line, and it becomes the backend for the desktop app (Tauri + React) and the Search Console /
Analytics / Ads integrations later.

## Projects

| Project | What it is |
| --- | --- |
| `src/ShoutingIguana.Engine` | Crawler library: fetching, robots.txt, sitemaps, parsing, storage |
| `src/ShoutingIguana.Cli` | `si` command-line tool: `crawl`, `stats`, `testsite`, `bench` |
| `src/ShoutingIguana.TestSite` | Deterministic synthetic shop with planted SEO issues, for tests and benchmarks |
| `tests/ShoutingIguana.Engine.Tests` | xUnit: unit tests plus end-to-end crawls of the synthetic site |

## Build, test, run

```bash
cd engine
dotnet test ShoutingIguana.Engine.slnx

dotnet run --project src/ShoutingIguana.Cli -- crawl https://example.com --db example.sicrawl
dotnet run --project src/ShoutingIguana.Cli -- stats example.sicrawl
dotnet run --project src/ShoutingIguana.Cli -- crawl --resume --db example.sicrawl     # after Ctrl+C
dotnet run --project src/ShoutingIguana.Cli -c Release -- bench --pages 100000
```

`si crawl --help` lists every option: concurrency and rate cap, user agent presets, extra headers (for firewall
allowlisting), proxy, depth and URL limits, robots.txt, subdomains, start-folder scope, nofollow, stored HTML.

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

URLs that only appear in sitemaps keep depth `1000000` (`CrawlDepth.UNLINKED`), which is how orphan pages show up.

## Benchmark

`si bench` serves the synthetic site from a child process, crawls it, and checks the result against what the site
says must be found. It can fail on thresholds, and CI runs it on every push (`.github/workflows/engine.yml`).

The first run on a 32-core Windows machine, at concurrency 16, against the local synthetic site:

| Pages | URLs | Links | Time | Throughput | Peak memory | Growth over the crawl | File |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 101,023 | 110,126 | 5.05 M | 32 s | 3,114 pages/s | 166 MB | none (−3 MB) | 384 MB |

On real sites, speed is set by the network and your rate settings, not by the crawler.

## Not in this step

- SEO rules and the issue catalog (step 2). The facts they need are already extracted and stored.
- The desktop UI (step 3).
- JavaScript rendering (Playwright, with HTTP / Rendered / Auto modes) and browser fallback for blocked sites
  (step 4). `Blocked` responses are already detected.
