# v2 rebuild: plan, decisions and status

The single place to pick up the rebuild. Technical detail lives in [engine/README.md](../engine/README.md) and the
rules for working in the code in [CLAUDE.md](../CLAUDE.md). Update the status and open items here at the end of every
step. For step 3, **Where step 3 stands** below is the only status. `v2`'s copy of this doc only points here. Update
this section before you stop, then push `ui/step-3`.

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
| 2 | SEO rules and issue catalog (84 checks), `si analyze` / `si issues`, exact-match proof on the synthetic site | Done: `1489adb`, then a real-site crawl and two full reviews ([Status](#status)) |
| 3 | Desktop UI: React + TypeScript in a Tauri 2 shell, with the engine as a sidecar behind a token-protected localhost API. Enterprise-grade UX: the bar is in [the UI brief](v2-ui-brief.md) | In progress on `ui/step-3` at `e9edd0c`. Pick up at [Where step 3 stands](#where-step-3-stands-2026-11-04) |
| 4 | Missing basics: JavaScript rendering, a browser fallback for blocked sites, include/exclude rules, auth and cookies, list mode, custom extraction | Later |
| 5 | Integrations: Search Console, then GA4, Bing Webmaster, Google Ads and Microsoft Advertising | Later |

**Step 3 starts with a design conversation with the owner, not with code.** The owner wants a "super slick,
enterprise level" UI and UX. The old app was "clunky and a bit all over the place". **[The UI brief](v2-ui-brief.md)
sets the bar and the process**: discovery, sign-off on mockups and a clickable prototype, vertical slices, and a
definition of done for every screen. It also covers the performance budgets and the engine work the UI needs.
Discovery and the visual direction were settled on 2026-11-01 (direction B, "clarity"). Do not restart them.

## Where step 3 stands (2026-11-04)

Work on branch `ui/step-3`, worktree `.claude/worktrees/ui-step-3`, pushed to `origin/ui/step-3` at `e9edd0c`.
`v2` and `master` were not moved. The temp checkpoint under
`%LOCALAPPDATA%\Temp\claude\...\c67f90ea-...\scratchpad\checkpoint.md` is history. This section is the place to resume.

Running since 2026-11-04, each on its own branch cut from `5fdcbd8`, unmerged: items 1 and 3 below, the design-system
pass on `ui/design-system` (worktree `.claude/worktrees/design-system`) and the forced-colours Tab test on
`ui/forced-tab` (worktree `.claude/worktrees/forced-tab`). If a session ends before they report, relaunch each from its
brief (the session's checkpoint, named in the owner's memory file); don't redo what the branch already committed.

Done on this branch, on top of `v2`:

- The grid readers, the inspector, live crawl data, and the app shell and front-end review fixes are merged.
- The client uses `POST /urls/inspect`, `/urls/inlinks`, `/urls/outlinks` and `/urls/source`.
- Near-duplicates use four SimHash lanes. `AnalysisFreshness.VERSION` is 6.
- The catalog has 86 checks. FAQPage no longer requires `mainEntity`. `es-419` is an invalid hreflang. JSON-LD is
  strict. A remote JobPosting needs `jobLocation` or `jobLocationType`. New check `html-past-crawl-limit`.
  `blocked-by-robots`, `description-missing` and `h1-missing` are Notices. `duplicate-site-version` is a Warning.
  Each check has `why` and `learnMore`, and `GET /api/checks` returns them.
- Inter and JetBrains Mono are bundled (roman 400, 500 and 600) in `app/src/styles/fonts/`.

Left, in this order:

1. The rest of the design system. Tokens exist. Storybook has the shell, feedback and the grid, not every component
   state.
2. The Issues screen. It is still a stub. The why and learn-more texts are already on each check.
3. One end-to-end test is still red and has not been re-run: under forced colours, Tab from the URL search does not
   focus the grid (`app/e2e/states.spec.ts`). That screen's code did not change. A stale dev crawl can put an Analyse
   button between the search box and the grid. Do not edit the files in `%LOCALAPPDATA%\ShoutingIguana\dev-crawls`.
4. The clickable prototype, then the vertical slices in the UI brief.
5. Later engine work the UI still needs: the links grid, sitemaps and robots (E3b), export (E5), full-text search and
   favicons (E6).

The catalog branch was verified before its merge: 1,104 tests, and the 100k bench found every planted issue. The
merge itself was clean. CI on `v2` does not run this branch.

## Status

As of 2026-10-31:

- **Real site.** `www.gardenfurnituresets.co.uk` (behind Cloudflare) was crawled at 2 requests/s on 2 connections:
  4,521 URLs in 37 minutes, never blocked. An audit checked every issue it reported against the stored HTML of all 265
  pages. 17 of the 20 issues it reported were right. The rest are fixed or documented below:
  - duplicate H1s within a paginated series;
  - images sized by CSS;
  - one-off slow responses.
- **Reviews.** Three rounds of parallel reviews covered storage, rules, fetching, and the CLI and tests. They found
  about 60 defects, then about 35, then about 20. Every one is fixed, except those listed under
  [Known limitations](#known-limitations). Each fix has a test that was seen to fail with the fix reverted.
- **Legacy app.** The old app's link-queue fix shipped as 1.0.4 from `master`.

## Decisions (and why)

- **C# / .NET 10 for the engine.** Official .NET SDKs exist for every integration on the roadmap (Search
  Console, GA4, Google Ads, Microsoft Advertising). The old app's problems were design, not language.
- **Cross-platform.** The engine targets `net10.0` and CI tests on Windows, macOS and Linux. The UI shell is Tauri
  for the same reason.
- **The NuGet plugin marketplace is gone.** It never worked (plugins never loaded) and the owner agreed. Checks are
  built-in rules in `engine/src/ShoutingIguana.Engine/Analysis/Rules`.
- **HTTP-first fetching.** Each page is parsed once into `PageFacts`. A browser is used only where needed, in step 4.
- **Blocked sites are reported, not hidden.** The crawler uses:
  - realistic headers;
  - a polite rate with automatic backoff;
  - explicit `Blocked` detection (Cloudflare, Akamai, DataDome...);
  - custom headers, so a site's firewall can allowlist it.

  There will be no proxy rotation or fingerprint spoofing.
- **`www` and the bare domain are one site.** Both are internal, and they share one rate limit and connection limit.
  Sites serve one and redirect the other, and separate limits would double the load on the server. The owner confirmed
  this on 2026-11-01.
  - Crawling them as one site stays. A version that answers instead of redirecting to the main one is
    `duplicate-site-version`, a Warning: Google treats the versions as duplicate URLs and picks one, preferring
    HTTPS. Probes aren't parsed, so a canonical on the other version isn't seen.
  - Nothing links to the other versions as a rule, so the crawler probes each one at the start of a crawl: a status
    check of the start URL's path there, never parsed or followed, so the site is never crawled twice.
  - A dead version isn't flagged, and neither are its unreachable URLs or robots.txt: a dead `www` twin is normal.
  - A probe never holds up a crawl: one try, none at the end, and none at all where its origin's robots.txt timed
    out. A version behind a firewall that drops packets then costs one robots.txt cycle, not three minutes.
- **A site that stops answering stops the crawl, which can be resumed.**
  - The crawler pauses a minute between tries, and after 10 minutes without an answer it stops: status Stopped,
    and `si crawl` exits 3.
  - `--resume` continues from there.
  - Whether something answers is judged per origin, with its robots.txt as the probe. So one part of the site
    going quiet (a dead `www` twin, a closed port 80, a section that never answers) doesn't stop a healthy crawl.
  - An origin that has answered before is waited out, never written off. Only one that has never answered can be
    written off, and only while the site's last answering origin still answers. A start URL that only redirects
    (`http://` to `https://`) therefore can't make the real site look dead.
  - Time spent paused doesn't count toward the 10 minutes.
  - A URL the crawler gave up on without asking is "Not checked", never broken.
- **A check that can't be accurate is skipped, not reported clean.**
  - The orphan and sitemap coverage checks need a complete crawl and readable sitemaps.
  - The external-link, image and sitemap checks need what the crawl options may switch off.
  - When a crawl doesn't support a check, it is left out of the results with nothing stored for it, and a note says
    why.
  - Not checked is not the same as 0, and the UI must show the difference.
- **A robots.txt that gets no answer at all blocks only the start URL's origin.**
  - RFC 9309 says to assume a full disallow when robots.txt can't be fetched, and the crawler does that for a 5xx or
    a 429 anywhere.
  - On any other origin with no answer at all (another site, a dead `www` twin, a closed port 80), the URL is fetched
    anyway. An origin that can't answer robots.txt almost never answers anything else, and fetching shows the real
    network error. Without it, a dead link would be reported as "blocked by robots.txt".
- **Crawl files hold no secrets**, since people share them. Header values and proxy credentials aren't stored, and
  resuming a crawl asks for them again.
- **One SQLite file per crawl (`.sicrawl`).**
  - Only `Storage/` writes to it: `CrawlWriter` during a crawl, `IssueWriter` for analysis.
  - Memory stays flat because the frontier, results and links live on disk.
- **Analysis runs after the crawl, from stored data.** Any crawl can be re-analysed with new rules or thresholds
  without recrawling. Issue ids are stored in crawl files, so they never change.
- **Analysis judges what search engines will see.**
  - An instant meta refresh is a redirect.
  - A canonical whose target leads back to the page, or nowhere, leaves the page indexable. A broken canonical is
    ignored, and a redirect is the stronger signal.
  - A sitemap may list URLs on its own host, the start URL's host and the home page's host. A `www` sitemap listing
    `www` pages is fine on a crawl that starts at the bare domain.
  - A link is named the way a screen reader names it: an image's alt text, a labelled icon, `aria-labelledby`,
    `aria-label` or `title`.
  - The pages of a paginated series (`rel=next/prev`) may share an H1, but each still needs its own title and
    description.
  - The crawl's own record stays as fetched. The analysis stores where it differs in `effective_indexability`,
    which the stats and the UI read.
- **The proof is exact.**
  - The synthetic site (`engine/src/ShoutingIguana.TestSite`) is clean by construction: problems appear only where
    they are planted.
  - The e2e test and the 100k benchmark require the exact URLs for every check, so extra flags fail as well as
    misses.
  - Every new check needs a planted case.
  - The one allowance is for response times, which depend on the machine. Up to 5% of pages (at least 25) may be
    flagged slow besides the planted one. The e2e test also checks the stored times directly, so the crawler's own
    timing can't drift unnoticed.
- **Thresholds follow common SEO tool defaults.** For example: titles 30–60 characters, descriptions 70–155,
  200 words, images over 200 KB. All are configurable in `AnalysisSettings`. Googlebot's 2 MB HTML limit is not:
  it is Google's, named in the check, and not a setting.
- **Check severities (November 2026).** Severity is part of the checks fingerprint, so analyses from before these
  changes are out of date. That is intended.
  - `blocked-by-robots` is a Notice, not a Warning. Blocking is usually deliberate (baskets, internal search,
    filters). Search Console lists it as excluded, not an error, the same as `noindex-page`.
  - `description-missing` is a Notice. Google writes most snippets from the page, and a missing description has no
    documented ranking effect.
  - `h1-missing` is a Notice. Google says it rarely relies on headings' HTML semantics; the gap is mainly
    accessibility.
  - `duplicate-site-version` is a Warning, not an Error. Nothing is broken: Google treats the versions as duplicate
    URLs and picks one, preferring HTTPS. Probes aren't parsed, so the check can't see a canonical on the other
    version and can't tell a harmless copy from a harmful one.
  - `meta-refresh` stays a Warning. One severity per id. Google treats an instant refresh as a permanent redirect,
    but a server redirect is what Google recommends, and every client follows it.

## Handoff to step 3 (the UI)

The engine is a library, and the CLI is a thin layer over it. The UI needs the same thin layer as a localhost API.
What the UI must be like is in [the UI brief](v2-ui-brief.md). This section covers what the engine gives it.

Work on a branch off `v2` (for example `ui/step-3`) and merge `v2` in often. Engine changes keep landing on `v2`
meanwhile, starting with the check for duplicate site versions (bare domain or `www`, http or https).

### What exists

Everything below is public in `ShoutingIguana.Engine`.

| Need | API |
| --- | --- |
| Start, pause, resume or stop a crawl | `new CrawlEngine(loggerFactory).Start(new CrawlRequest(options, path, Resume), observer)` returns a `CrawlJob`: `Pause()`, `Resume()`, `StopAsync()`, `Progress`, and `Completion` (a `CrawlSummary`) |
| Live progress and warnings | `ICrawlObserver.OnProgress(CrawlProgress)` every 500 ms. `OnNotice(CrawlNotice)` carries a stable `Code` to key on, such as `blocked`, `site-down` or `robots-unreachable`; the message text may change |
| Crawl settings | `CrawlOptions` and its `Validate()`. `CrawlDatabase.ReadOptions(path)` returns a stored crawl's settings, secrets removed (`HasRedactedProxyCredentials`; header names are kept, values aren't) |
| Analyse | `new CrawlAnalyzer(loggerFactory).AnalyzeAsync(path, settings, cancellationToken)` returns an `AnalysisSummary`: counts per check, `IsPartial` and `Notes` |
| Issues | `IssueReader`: `ReadAnalysis`, `ReadCounts`, `ReadNotes`, `ReadIssues(path, id, limit, offset)` (paged) and `ReadAll` (streamed, for export). A check missing from `ReadCounts` wasn't run |
| What each check means | `IssueCatalog.All` and `Find(id)`: id, category, severity, title, description (what), why, fix and an optional learn-more link |
| Headline numbers | `CrawlStatsReader.Read(path)` |
| Anything else | `CrawlDatabase.OpenReader(path)` is a read-only connection. `BlobCodec` decodes `response_details` (headers and page facts) and `response_html`. The tables are defined in `Storage/CrawlSchema.cs` |

### Rules the UI must keep

- **One writer at a time.**
  - A crawl or an analysis holds `<crawl file>-lock`. On Linux and macOS the file stays after the lock is released.
  - An analysis refuses a crawl that is running or was interrupted.
  - Readers are safe at any time, including during a crawl.
- **Show indexability as the analysis judged it**:
  `COALESCE(effective_indexability.indexability_reason, responses.indexability_reason)`.
- **Show skipped checks and the analysis notes.** A skipped check is not a 0.
- **Ask again for secrets on resume.** Header values and proxy credentials are not in the file.
- **Never write to a crawl file outside `Storage/`.**

### What's missing

1. **The sidecar.** An ASP.NET Core minimal API on localhost, started by the Tauri shell, with a random port and a
   per-launch token. It needs:
   - crawl control;
   - analysis;
   - the readers;
   - progress and notices, pushed over server-sent events.
2. **Readers for the grids.** These must be paged, filtered and sorted, and live in `Storage/`:
   - URLs: status, indexability, depth, type, size and time;
   - links in and out of a URL: anchor, position and flags;
   - one URL in detail: headers, facts, redirect chain, inlinks, issues and HTML;
   - sitemaps and robots.txt.

   Only issues are paged today. Anything else means SQL against `OpenReader`.
3. **Export.** Today only the CLI's issues CSV exists. Still to do: URL lists and Excel.
4. **Grouping.** A sitewide template fault is one row per page. On the real site, one logo link without text was 265
   rows of `empty-anchor-text`, which buried the 4 that differed. The grid should group a check's rows by detail and
   related URL.
5. **A list of crawls.** Today a crawl is just a file path.

## Open items and owner decisions

- **Proposed, not yet confirmed:**
  - crawl modes HTTP / Rendered / Auto (Auto samples raw against rendered HTML and switches per site);
  - raw-vs-rendered diffs as a feature;
  - a per-host browser fallback when a site blocks plain HTTP.
- **Decided this round under delegation, easy to revisit:**
  - stopping after 10 minutes without an answer;
  - the checks skipped on crawls that can't support them.

## Known limitations

- Near-duplicate detection estimates similarity from a 256-bit fingerprint. Pages 70% to 96% alike (a heavy shared
  template) can be flagged on a large enough site, and a bucket of pages sharing much of their text is searched within
  a bound, so a near copy in one could be missed (`engine/README.md`, Near duplicates). Crawls made by engines before
  the 256-bit fingerprint skip the check until crawled again.
- Links are capped at 10,000 per page, so pages linked only from a huge HTML sitemap can look orphaned.
- Some crawls skip the orphan check: stopped, depth-limited, URL-limited, folder-scoped, or with sitemaps off or none
  read. A declared sitemap that failed (other than with 404/410) skips the not-in-sitemap check. The analysis notes
  say why, and `si analyze` and `si issues` print them.
- A redirect chain is reported once, where it's entered. But if it ends at a broken URL, every hop is flagged
  `redirect-broken`.
- An hreflang alternate that redirects to a 404 is reported as non-indexable (it redirects), not as broken.
- Block pages served with status 200 (Imperva, the Sucuri JavaScript challenge) aren't recognised; their HTML is
  analysed as the page.
- robots.txt: a `Crawl-delay` line ends a run of `User-agent` lines, as a rule does. So `User-agent` lines after it
  start a new group.
- An external host that stops answering is written off for the rest of the crawl. Its remaining URLs are "Not
  checked", and links to them aren't reported as broken.
- On a depth-limited crawl, a sitemap URL that redirects has its target fetched, but not if that URL is also linked
  from beyond the limit.
- Two robots.txt checks, `robots-txt-unreachable` and `robots-txt-missing`, are proven by loopback crawl tests, not
  by plants on the synthetic site, whose own robots.txt has to work. `blocked-by-robots` and
  `robots-txt-unreachable-other-origin` are planted.
- `slow-response` reports a single measurement, so one stall during the crawl flags a page. The detail shows how much
  of the time was spent waiting for the first byte.
- `image-missing-dimensions` reads the HTML attributes. An image whose box CSS sizes is still flagged, as the
  crawler doesn't read the CSS.
- A sitemap is stored under the URL it was found at, not the URL it redirects to.
- Word counts for Chinese, Japanese and Thai count characters, which is close enough for thin-content checks.
- Title and description widths count full-width characters and emoji as two, an estimate of the room they take,
  not a pixel measurement.
- A version of the site that answers 200 is flagged `duplicate-site-version` even if its page has a canonical or an
  instant meta refresh pointing to the main site, because probes aren't parsed.
- `charset-missing` doesn't treat a byte-order mark as a charset declaration. W3C says a BOM wins over every other
  declaration, but the crawl stores decoded text and `PageFacts` doesn't record that a BOM was there. The check
  doesn't re-parse the HTML to look for one.
- Hreflang script subtags aren't checked against ISO 15924: any four letters pass. Rare enough that it wasn't added.
- A sitemap URL on a different protocol from the sitemap (an http URL in an https sitemap) isn't reported.
  sitemaps.org drops it; the check only compares hosts. Left on the backlog.

## How to verify

```bash
dotnet test engine/ShoutingIguana.Engine.slnx
dotnet run --project engine/src/ShoutingIguana.Cli -c Release -- bench --pages 100000 --concurrency 16 \
  --max-peak-mb 400 --max-growth-mb 100 --min-pages-per-sec 250
```

CI (`.github/workflows/engine.yml`) runs both on every push to `v2`.

Latest results:

| Run | Commit | Tests | Crawl | Peak memory | Analysis |
| --- | --- | --- | --- | --- | --- |
| Local, 32 cores, quiet machine | `68d55a2` | 417 engine + 16 CLI | 35 s (2,916 pages/s) | 168 MB | 7 s |
| GitHub runner, 4 cores | `530733b` | 464 + 16 on Windows, macOS and Linux | 91 s (1,114 pages/s) | 204 MB | 12 s |

Both runs found every planted issue and flagged nothing else, with no check skipped or failing. Runner speed varies:
on `68d55a2` the runner crawled in 76 s and analysed in 7 s, and the analysis does almost the same work on both
commits.

## Working with the owner

- Keep reports short and to the point: the answer first, no essays.
- The owner delegates freely ("get it done", "do whatever is needed next"). Pushing to `master` or releasing still
  needs an explicit yes.
- `src/` is legacy: critical fixes only. New work goes in `engine/` (and `app/` for the UI).
- The owner's bar for the UI is high: show them mockups and a prototype, and get a sign-off before building each
  screen ([the UI brief](v2-ui-brief.md)).
