# v2 rebuild: plan, decisions and status

The single place to pick up the rebuild. Technical detail lives in [engine/README.md](../engine/README.md) and the
rules for working in the code in [CLAUDE.md](../CLAUDE.md). Update the status and open items here at the end of every
step.

## Why

The WPF app in `src/` was hard to build on, its UI was clunky, and large sites caused memory problems. An audit
traced the problems to the design, not to C#:

- every page went through Chromium;
- the HTML was stored and reloaded about nine times per page, and each page was parsed ten or more times;
- plugins held static state;
- the default 1.5 s delay per host meant 40+ hours for 100k URLs.

v2 is a fresh solution that ports the useful logic.

## Plan

| Step | What | Status |
| --- | --- | --- |
| 1 | Headless engine, storage and CLI (`si`), proven on a generated 100k-page site, with the benchmark in CI | Done: `3e556be` |
| 2 | SEO rules and issue catalog (81 checks), `si analyze` / `si issues`, exact-match proof on the synthetic site | Done: `1489adb`, review fixes `90589c6` |
| 3 | Desktop UI: React + TypeScript in a Tauri 2 shell, with the engine as a sidecar behind a token-protected localhost API | Next |
| 4 | Missing basics: JavaScript rendering, a browser fallback for blocked sites, include/exclude rules, auth and cookies, list mode, custom extraction | Later |
| 5 | Integrations: Search Console, then GA4, Bing Webmaster, Google Ads and Microsoft Advertising | Later |

**Step 3 starts with a design conversation with the owner, not with code.** The owner wants to rethink the UI/UX
and see mockups before anything is built.

## Decisions (and why)

- **C# / .NET 10 for the engine.** Official .NET SDKs exist for every integration on the roadmap (Search
  Console, GA4, Google Ads, Microsoft Advertising). The old app's problems were design, not language.
- **Cross-platform.** The engine targets `net10.0` and CI tests on Windows, macOS and Linux. The UI shell is Tauri
  for the same reason.
- **The NuGet plugin marketplace is gone.** It never worked (plugins never loaded) and the owner agreed. Checks are
  built-in rules in `engine/src/ShoutingIguana.Engine/Analysis/Rules`.
- **HTTP-first fetching.** Each page is parsed once into `PageFacts`. A browser is used only where needed, in step 4.
- **Blocked sites are reported, not hidden.** The crawler uses realistic headers, a polite rate with automatic
  backoff, explicit `Blocked` detection (Cloudflare, Akamai, DataDome...) and custom headers so a site's firewall
  can allowlist it. There will be no proxy rotation or fingerprint spoofing.
- **One SQLite file per crawl (`.sicrawl`).** Only `Storage/` writes to it: `CrawlWriter` during a crawl,
  `IssueWriter` for analysis. Memory stays flat because the frontier, results and links live on disk.
- **Analysis runs after the crawl, from stored data.** Any crawl can be re-analysed with new rules or thresholds
  without recrawling. Issue ids are stored in crawl files, so they never change.
- **The proof is exact.**
  - The synthetic site (`engine/src/ShoutingIguana.TestSite`) is clean by construction: problems appear only where
    they are planted.
  - The e2e test and the 100k benchmark require the exact count for every check, so extra flags fail as well as
    misses.
  - Every new check needs a planted case.
- **Thresholds follow common SEO tool defaults.** For example: titles 30–60 characters, descriptions 70–155,
  200 words, images over 200 KB. All are configurable in `AnalysisSettings`.

## Open items and owner decisions

- **Legacy fix waiting:** branch `fix/link-queue` (`d8b12f1`, from `master`) fixes the old app's bug where links found
  while crawling were never queued, so crawls only covered the start URL and sitemaps.
  - Build and tests pass, with 3 new regression tests.
  - It is not pushed or merged: **a push to `master` triggers an automatic release** (`.github/workflows/release.yml`).
    Merging it ships 1.0.4, so it's the owner's call.
- **Proposed, not yet confirmed:** crawl modes HTTP / Rendered / Auto (Auto samples raw against rendered HTML and
  switches per site), raw-vs-rendered diffs as a feature, and a per-host browser fallback when a site blocks plain
  HTTP.
- **Not yet done:** a crawl of a real live site. Everything so far is proven on the synthetic site only.

## Known limitations

- Near-duplicate detection compares at most 1,000 neighbours per fingerprint bucket, so past roughly 500k pages a
  few pairs can be missed.
- Links are capped at 10,000 per page, so pages linked only from a huge HTML sitemap can look orphaned.
- A stopped crawl skips the orphan and sitemap-coverage checks, and the analysis output says so.
- Word counts for Chinese, Japanese and Thai count characters, which is close enough for thin-content checks.

## How to verify

```bash
dotnet test engine/ShoutingIguana.Engine.slnx
dotnet run --project engine/src/ShoutingIguana.Cli -c Release -- bench --pages 100000 --concurrency 16 \
  --max-peak-mb 400 --max-growth-mb 100 --min-pages-per-sec 250
```

CI (`.github/workflows/engine.yml`) runs both on every push to `v2`.

Latest results, on commit `90589c6`:

| Run | Tests | Crawl | Peak memory | Analysis |
| --- | --- | --- | --- | --- |
| Local, 32 cores | 135/135 | 42 s (2,424 pages/s) | 172 MB | 6 s |
| GitHub runner, 4 cores | 135/135 on all three OSes | 90 s (1,128 pages/s) | 192 MB | 12 s |

Both runs found every planted issue and flagged nothing else.

## Working with the owner

- Keep reports short and to the point: the answer first, no essays.
- The owner delegates freely ("get it done", "do whatever is needed next"). Pushing to `master` or releasing still
  needs an explicit yes.
- `src/` is legacy: critical fixes only. New work goes in `engine/` (and `app/` for the UI).
