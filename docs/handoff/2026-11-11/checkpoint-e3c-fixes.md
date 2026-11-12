# E3c fixes checkpoint

Brief: `S2\brief-e3c-fixes.md`. Findings: `S1\e3c-review\findings.md` (7: one medium, six low).
Worktree `C:\Projects\ShoutingIguana\.claude\worktrees\e3c`, branch `ui/e3c`, started at HEAD `4400814` (clean).
Rules: one commit per finding, never push/merge, never rename a public function, every fix gets a test SEEN failing
without it (restore with fresh timestamp), stop only my own processes by pid, never `dotnet build-server shutdown`,
no bare `git stash`, stay out of `app/` except `app/src/api/{types,grids,client,inspector}.ts`.
The Edit tool keeps CRLF; never `sed -i` or Python text-mode writes on repo files (binary read/replace/write is fine).

## Tools (S2\e3c-fixes)

- `base\sidecar`, `base\cli`: Release builds of 4400814 (the "before").
- `wal.py <base|new> <pages> <readers>:<links|stats|urls> ... [--analyse]`: WAL peak, crawl rate, read waits per row
  (starts and stops its own test site and sidecar). Logs in `logs\`.
- `mut.py <spec.py>`: applies a mutation, runs tests, restores byte for byte + fresh timestamp; `logs\mutations.txt`.
  Specs: `m1.py` (finding 1), `m1-everywhere.py` (LOG_LIMIT_BYTES = 16 KB, whole engine + sidecar suites must pass).
- DOTNET_PROCESSOR_COUNT=2 in the sidecar's environment gives it two read slots (finding 7's measurement).

## Status

- [ ] 1 WAL (MEDIUM): fix written, NOT committed yet.
  - Before (40k pages, base build, `logs\wal-before-1.log`): no reader 22.6 MB / 1,643 pages/s; 1 links client 205.8 MB
    (file then 99 MB) / 1,798; 6 links clients 80.0 MB / 1,463; 2 stats clients 24.5 MB / 1,677. Longest read 0.96 / 1.77 / 0.19 s.
  - Fix: `CrawlDatabase.FoldLog` (TRUNCATE checkpoint under `CrawlReadGate.Exclusive`, busy timeout 250 ms, never throws),
    `CrawlDatabase.LogLimitBytes` (64 MB; AsyncLocal so a test lowers it for its own writers), `CrawlWriter.FoldLargeLog`
    after each commit; back-off: next fold not before 2x the longest read it stopped had been reading
    (`CrawlReadGate.Exclusive(path, out longestStopped)`), and 5 s after a fold another program's reader blocked.
  - Tests (RunningCrawlReadsTests): `TheLogOfACrawlWhoseLinksAreReadAllTheWhileStaysSmall` (relay readers, 4,000 pages,
    limit 512 KB, bound 16 MB; without the fix the log reached 50.6 MB of a 16.1 MB file) and
    `EveryReadOfACrawlWhoseLogIsFoldedOverAndOverAgreesWithItself` (the hammer at a 64 KB limit: ~350 folds seen, >= 50
    asserted; without the fix: 1). Both SEEN FAILING with the fold removed (mut.py m1.py), passing with it.
  - Running now: whole engine + sidecar suites with the limit at 16 KB (m1-everywhere.py).
  - TODO: after-measurements (wal.py new), IssueWriter measure (wal.py --analyse), `si bench --read-links --max-log-mb`,
    CI step, docs (API doc E3c section, engine README), commit.
- [ ] 2 stats-pass misses 300 ms; kept read needs own gate
- [ ] 3 first run with no stretch -> crawlingMs null for good
- [ ] 4 clock set back between stretches; Stopwatch through sleep
- [ ] 5 eight mutations no test notices (M12, M17, M18, M22, M09, M15, M16, N08) + truth check in the hammer
- [ ] 6 three doc sentences
- [ ] 7 /stats queues behind other crawls' grid reads (measure first)
- [ ] Verify: Release build, dotnet test, readbench --gate on fresh 100k, bench 100k

## Decisions

- TRUNCATE, not RESTART: the log's file size is then an honest measure (a restarted log keeps its size), and the disk is given back.
- Reads are stopped and read again (the gate's existing way), not waited for: the crawl never waits on a read. To keep
  a read longer than the fold interval from starving, the writer leaves reads alone for twice the longest stopped read's age.

## Finding 1 measurements: what is and isn't usable (written before re-measuring)

- `logs\wal-after-1.log` is NOT to be quoted: one run per row, taken minutes after the before rows under different load
  (no-reader crawl 2,720 vs 1,643 pages/s, where the fix does nothing), and from a build since changed (held-retry doubling).
- Six links clients: 80.0 MB before, 79.6 MB after: no difference shown. The unfixed build alone ranges 80 to 221 MB for
  that row (mine, the reviewer's). One run a side cannot separate them. My "lucky reset" and "64 + r x 2A" explanations
  were made after seeing the numbers and are untested.
- What does stand: the engine tests seen both ways (relay: 50.6 MB of a 16.1 MB file without the fold, under 7 MB with;
  hammer: ~350 folds vs 1), and both suites green with the limit at 16 KB (997 engine, 165 sidecar).
- Next: base and new interleaved, 3 rounds, same four rows, range per cell, folds counted per row. Overlapping ranges are
  reported as not separated, for WAL and for pages/s alike.

## ORCHESTRATOR MESSAGE (mid-task; keep verbatim in substance)

STEER, finding 1: before committing the stop-and-reread fold, measure it against the simpler shape and KEEP THE SIMPLER
unless the numbers say otherwise: under the exclusive gate, hold NEW reads, let reads in flight finish, fold, release;
if the gate isn't had within a bound, skip this fold and try after the next commit. Deciders: longest any read waited,
longest the writer's commit waited (and whether fetching stalls), peak log, pages/s; interleaved base/simple/mine.
Whichever is kept: API doc says what a reader can now observe (a read that waits, never an error, never a wrong answer)
and the honest bound of the log at bench speed and at a real site's speed. TRUNCATE over RESTART is fine.

ADDITION A (after findings 1-7; additive): `GET /sitemaps` gains two counts per sitemap beside its URL counts:
listed URLs robots.txt refused (working name `robotsDisallowed`); listed URLs that redirect or are broken (working name
`redirectingOrBroken`), defined EXACTLY as the screen's quick filter: read `git show ui/sitemaps-robots:app/src/screens/sitemaps/sitemapLinks.ts`
and neighbours. Each count lands on itself: `POST /sitemaps/urls` with the matching filter returns exactly that many
(test on the bench crawl and on a test-site crawl with robots.txt refusing listed URLs; plant the case if missing, by
the repo's planted-case rule). An index's counts follow its other counts' rule. No slower: `/sitemaps` keeps its budget
on the 100k crawl (measure before/after). Document under "Engine changes E3c made"; TS type fields optional; REPORT THE
FINAL NAMES (a UI agent builds against the working names).

ADDITION B (docs only): a robots.txt origin can read `unanswered` and still have blocked URLs (a crawl continued after a
run that read a refusing file when the later run got no answer: only the last answer is kept; and files made by the
first engine, before `111c7f3`). Say so in the contract's robots.txt states. No code unless the engine can cheaply keep which.

## Plan after the steer

- `mine\` = copy of the Release sidecar+cli built from the stop-and-reread code; `mine-src\` = its three engine files.
- Implement the simple (drain) shape in the worktree, build, copy to `simple\`; wal.py labels base|mine|simple; add
  "longest gap in the live fetched count" (proxy for the writer's longest wait / fetch stall) to each row.
- Relay test readers must give up holding after ~50 ms, or a drain can never complete (a relay needs a NEW read to begin).

## State after the steer (worktree now holds the SIMPLE shape, uncommitted)

- Worktree: `CrawlReadGate.ExclusiveOnceIdle(path, patience)` (new reads wait, in-flight end by themselves; null if not in
  time), `CrawlDatabase.FoldLog` returns bool (FOLD_PATIENCE 2 s, then TRUNCATE with busy 250 ms), `CrawlWriter.FoldLargeLog`
  (after a fold that couldn't: retry 1 s later, doubling to 32 s; reset on success). Relay readers (test + `LinkReads`)
  give up holding after 100 ms. Read tests 25/25 pass with it. NOT yet seen failing without it in this shape; suites at
  16 KB not re-run in this shape.
- Stop-and-reread sources are in `mine-src\` (5 files) and its binaries in `mine\`; simple's binaries in `simple\`.
- Interleaved base vs mine, n=3 (logs\wal-interleaved.log; `python -I tabulate.py <log>`):
  0 links 22.2-26.1 vs 21.8-22.7 MB, 2477-2789 vs 2754-2860 pages/s; 1 links 84.4-123.3 vs 80.8-83.8 MB, 2438-2527 vs
  2406-2567, longest read 689-906 vs 1078-1147 ms; 6 links 87.8-159.1 vs 71.1-81.5 MB, 1725-2154 vs 2065-2257, longest
  1043-1975 vs 1006-1501 ms; 2 stats 22.7-39.5 vs 21.7-22.7 MB. No read errors anywhere. Rates overlap: not slower.
- Running: base/mine/simple interleaved x3 -> logs\wal-three.log (adds "stores stood still" = longest standstill of the
  live fetched count, the outside view of the writer's longest wait).
- Addition B: DONE in docs/v2-sidecar-api.md (robots.txt states: "The state is the last answer's; blocked is the whole
  crawl's"), uncommitted. Verified: RobotsReader.StateOf maps no-status + not start origin to Unanswered whichever engine
  made the file; 111c7f3 removed "unreachable server means full disallow" for other origins.
- Addition A definitions (from ui/sitemaps-robots:app/src/screens/sitemaps/sitemapLinks.ts): redirectingOrBroken =
  grid `outcome in [redirect, clientError, serverError, timeout, networkError]`; robots = grid `outcome in [robotsDisallowed]`.
  The UI's SitemapCount keys today: listed, crawled(fetched), notCrawled, indexable, nonIndexable.
- Finding 6 (from UrlColumns.cs): grid statusClass has no `blocked` (a block page's 403 is in grid 4xx) and its
  noResponse needs fetched (not-checked excluded). Per-figure filters still to be checked against a crawl.
- Finding 4 research: Stopwatch counts through sleep on Windows (QPC, MS "Acquiring high-resolution time stamps"), not on
  Linux (CLOCK_MONOTONIC) or macOS (CLOCK_UPTIME_RAW); dotnet/runtime#77945 open.

## DECISION on finding 1: the SIMPLE shape is kept (worktree), per the three-way measurement

logs\wal-three.log, 40k pages, base/mine/simple interleaved, n=3 each (min to max):
| Row | Build | WAL peak MB | pages/s | reads | longest read ms |
| 0 links | base / mine / simple | 21.5-24 / 21.7-22.9 / 21.8-22.7 | 2252-2541 / 2553-2772 / 2443-2675 | - | - |
| 1 links | base / mine / simple | 105.9-160 / 78.5-97.2 / 74.9-87 | 2223-2546 / 2313-2573 / 2310-2543 | 1441-1552 / 1297-1454 / 1576-1855 | 644-829 / 978-1199 / 745-931 |
| 6 links | base / mine / simple | 92.4-124.1 / 57.7-83.6 / 69.7-75.3 | 1941-2237 / 1924-2153 / 2010-2146 | 10378-11737 / 7878-10476 / 11856-12283 | 853-1103 / 1072-1953 / 924-1103 |
| 2 stats | base / mine / simple | 22.7-37.8 / 22.6-42.6 / 24.5-38.5 | 2123-2419 / 2159-2565 / 2025-2523 | ~5-7k each | 99-161 all |
No read error in any row. pages/s overlap in every row. "Stores stood still" (outside proxy for the writer's wait) is
~1.0 s in EVERY row incl. base (the measure's own floor: it can't resolve waits under ~1 s); mine reached 1,546 ms on 6 links.
=> simple: reads never longer than base's range, log tighter, nothing thrown away. The writer's wait is NOT yet measured
from inside: `CrawlWriter` now logs once at close "Folded the write-ahead log {Folds} times ... {TotalMs} ms in all,
{LongestMs} ms at longest" (Information); wal.py should grep the sidecar's log for it (TODO).

## Edits made since (all uncommitted; built Debug OK; 72 tests of the four touched classes + 7 LiveDataTests passed)

- F3: `CrawlWriter.RestoreRunTime`: `neverCrawled` (no stretch row and no response) => no untimed marker, TimeCrawledBefore = 0.
- F4: `CrawlRunReader` clamps a stretch's start to the previous end; `RunClock` leaves out gaps > ASLEEP (5 min) between readings.
- F5 tests: CrawlTimeTests (AFirstRunThatStoredNoStretchLeavesTheTimeKnown, AClockSetBackBetweenStretchesLeavesThemInOrder,
  TimeAsleepIsNotTimeSpentCrawling, AReadingOfAStretchHeardLateChangesNothing), LinkGridReaderTests
  (TheOthersAreReadThroughTheirTargetsOnlyUpToAnEighthOfTheLinks), UrlLinksAndSourceTests (not-checked other end),
  RunningCrawlReadsTests (TheTotalIsTheMomentsWhenTheReadThatTookItNeverAnswered; hammer holds answers to the finished
  file via TRUTHS), sidecar LiveDataTests.AUiThatOpensMidCrawlGetsItsHistorySinceTheStart (live item crawlingMs,
  crawledUrlCount with a planted not-checked URL, live history runs).
- F6 docs: figure-to-filter table (verified: logs\figures.log, 0 mismatches on a crawl with 5 not checked and 1 blocked;
  4xx 21 vs grid class 22, noResponse 14 vs 9), /history crawlingMs "at rest", stretches in order, time asleep.
- Addition B docs done.
- Mutation spec `m-all.py` (15 mutations: F1a-c, F3a-b, F4a-b, M09, M12, M15, M16, N08, M17, M18, M22): NOT YET RUN
  SUCCESSFULLY. First attempt: every run failed to BUILD with MSB4242 (see below), so NO fix has yet been seen failing
  in its final form except F1a earlier (in the stop-and-reread shape).

## BLOCKER (environment, not mine): the machine's .NET SDK is mid-update

From about the time of the mutation batch, every `dotnet build`/`dotnet test <project>` fails before compiling:
MSB4242 SDK Resolver Failure, workload manifest 'Microsoft.NET.Workload.Emscripten.net6' 10.0.112 lower than 10.0.114
required by mono.toolchain.net6 (C:\Program Files\dotnet\sdk-manifests\10.0.100). msiexec.exe and setup.exe are running
under Services; sdk\10.0.403 has appeared beside 10.0.401. Not touched by me. Until it settles: no builds, so no
mutations, no Release rebuild (Release binaries predate the F3/F4/fold-stats edits), no bench/readbench.
Workaround in use: `dotnet test <path to built .dll>` runs the Debug assemblies as last built (they match the sources).

## After the SDK recovered (builds work again; Release rebuilt from current sources)

- Suites on the Debug assemblies built from the current sources, run by dll path: 1003 engine, 165 sidecar, 17 CLI, all passed.
- The writer's OWN fold line (simple shape, 40k, logs\wal-foldstats.log): 1 links: 2 folds, waited 268 ms in all / 260 ms
  longest; and 2 folds, 586 / 340. 6 links: 1 fold, 7 / 7; and 4 folds, 1415 / 724. No try in vain. So the longest the
  writer's commit waited is 724 ms; WAL peaks 70.4-84.9 MB.
- Analysis (IssueWriter), first analysis of the 40k crawl, WAL peak: no reader 25.8 MB; 1 links client 33.9 (base) /
  34.0 (new) MB; 6 clients 35.0 MB. A reader lets its two commits add up (a third more), bounded by what the analysis
  writes; not the crawl's failure. DECISION: left alone, reported.
- Finding 7 MEASURED (logs\slots-new.log; sidecar with DOTNET_PROCESSOR_COUNT=2 => 2 read slots; two clients asking for
  link passes on a running 40k crawl): another crawl's kept /stats: median 30 ms, p95 118, longest 248 ms; with nothing
  else reading: median 2, p95 12, longest 13 ms. IT WAITS => fix: a kept answer takes no slot (`CrawlStatsReader.ReadKept`
  via `context.Noted`-style Has; sidecar StatsAsync peeks without the slot; test with GridReads = 1 and the slot held).
- Running: mutation batch m-all.py (15). TODO after: implement 7, 2 (measure `si readbench --only Stats` first), Addition A
  (SitemapUrlCounts + `robotsDisallowed`, `redirectingOrBroken`; Judged() needs LEFT JOIN responses in summary mode; use
  the grid's own `outcome` SQL; UnderCounts too; TS type app/src/api/grids.ts:413; check the test site has a listed URL
  robots.txt refuses, else plant), CI step, bench --read-links run (+ seen failing with the fold removed), final verify,
  commits (split files that mix findings by building intermediate contents: CrawlWriter.cs F1/F3, CrawlTimeTests.cs F3/F4/F5).

## MUTATIONS: all 15 SEEN FAILING (logs\mutations.txt; files restored byte for byte with fresh timestamps)

F1a (never folds: log 207 MB of a 16 MB file; 1 fold seen), F1b (fold without the gate: 56 MB), F1c (gate doesn't hold
new reads: 96 MB), F3a, F3b, F4a, F4b, M09, M12, M15, M16, N08, M17, M18, M22: each CAUGHT by the test named in m-all.py.
Changes made to get there: the relay readers hold each read 350-700 ms (so only the gate lets a fold happen; F1b had
survived with 100 ms holds, SQLite's own 250 ms wait being enough), SMALL_LOG_BYTES 2 MB; the N08 test's first read is
refused with NeedsAnalysisException (a `to.inlinks` filter) between taking the moment and counting.
M15 first failed to build (the SDK hiccup again: MacCatalyst props missing); rerun: caught.
Product code unchanged since the Release build; test code changed since the last full-suite run (RunningCrawlReadsTests).

NEXT: implement finding 7 (ReadKept, no slot), finding 2 (measure `si readbench --only Stats` on data\bench-base.sicrawl),
Addition A; then build, suites, mutations for 7/A, `si bench --read-links` (+ with the fold removed), final verify, docs
(API doc "Engine changes from the E3c review" at the end; README benchmark paragraph), commits per finding.

## Late state (everything below is uncommitted in the worktree unless said)

- All 6 further mutations CAUGHT (m-new.py): F7 (kept /stats takes no slot: KeptStatsTakeNoReadSlot), A1-A5 (sitemap counts).
- Finding 7 DONE: `CrawlStatsReader.ReadKept`, `GridReadContext.Keeps`, sidecar `GridData.StatsAsync` peeks without a slot.
- Finding 2 DONE (code): readbench class `stats-kept` gate 10 ms; `stats-pass` gate 450 (was 600); CliTests updated;
  API doc E3c stats paragraph rewritten as a known limitation. Stats timings on data\bench-base.sicrawl (3 runs of 20):
  stats 98-100 ms p50 / 113-128 p95; kept 0.6 / 0.7 ms; stats-pass 188-251 p50 / 209-287 p95.
- Addition A DONE: FINAL NAMES `robotsDisallowed`, `redirectingOrBroken` (as the working names) on each sitemap's `urls`.
  SitemapUrlCounts (+2 fields, REDIRECTING_OR_BROKEN, Plus), SitemapListReader.Judged (summary mode: response looked up
  by scalar subquery only where s.status_code isn't 2xx), test-site PLANT `/private/listed` (Plant.REFUSED; issues
  blocked-by-robots + sitemap-non-indexable; only with sitemaps), tests in RobotsAndSitemapsReaderTests (+1 new test),
  sidecar LinksSitemapsRobotsEndpointsTests updated (helper AddRefusedByRobots removed), TS type optional fields,
  API doc Sitemaps section. `/sitemaps` list on the 100k crawl: before 18-22 ms p50 (20-29 p95), after 29-30 (31-34):
  +10 ms, inside its 200 ms gate. verify_sitemaps.py: 0 mismatches on bench, garden, fresh-noanalyze, fresh-analysed.
- Addition B DONE (docs). Finding 6 DONE (docs + TS comments; verified by verify_figures.py).
- Bench 100k NEW with CI's thresholds (logs\bench-new-1.log): exit 0, every planted issue found, 45.2 s, 2,237 pages/s,
  peak 198 MB, growth -16 MB, 494 MB file, analysis 15.3 s / 276 MB peak, 32,410 issues (the plant adds 2), 110,264 URLs.
  Crawl kept at data\bench-new.sicrawl. Log peak during the crawl ~36 MB (no fold), 65 MB during the analysis.
- RUNNING: `si readbench data\bench-new.sicrawl --runs 20 --gate` -> logs\readbench-new.log.
- STILL TO DO: bench --read-links (40k with --max-log-mb 128; 100k for the bound) and the same with the fold removed;
  full suites on final code (last full run was before finding 7 / addition A / gates); app checks (npm ci, typecheck,
  lint, test) since app/src/api changed; "After the review" paragraph in the API doc + README benchmark numbers;
  COMMITS (nothing is committed yet); final report.

## Final verification so far (code as it will be committed; NOTHING COMMITTED YET)

- Release build: 0 warnings, 0 errors (after the last code change).
- readbench --gate --runs 20 on data\bench-new.sicrawl (fresh 100k crawl by this code): exit 0, 153 reads, every class in
  its gate: stats 107.3 (350), stats-kept 0.8 (10), stats-pass 206.6 (450), sitemaps 44.5 (200), link-grid 135.2 (200),
  link-targets 212.5 (550), link-pass 1,067.7 (2,500), crawl 98.8 (185), crawl-facets 118.9 (245), facets 67.9, filter
  26.5, text 67.0, scan 33.2, indexed 9.6, robots 31.5, links-kind 13.3, check 7.9, links 4.6, inspector 1.4.
- bench --read-links (logs\bench-reads-*.log): 40k: every planted issue found, 16.5 s, 2,450 pages/s, 4,786 reads,
  slowest 458 ms, log peak 76 MB (80 from outside), exit 0 at --max-log-mb 128. 100k: every planted issue found, 46.4 s,
  2,180 pages/s, 9,419 reads, slowest 1,521 ms, log peak 89 MB (93 from outside), exit 0.
  WITH THE FOLD REMOVED (40k): log 580 MB, "FAILED The write-ahead log reached 579.8 MB ... over the 128 MB limit", exit 1.
  Fold restored, Release rebuilt.
- Docs: API doc has "## Engine changes from the E3c review" (+ "After the review" paragraph); README benchmark paragraph.
- commit_all.py --dry matches the tree (10 commits planned). RUNNING: the three suites on the final Debug build.
- THEN: app checks (npm ci, typecheck, lint, test), `python -I commit_all.py`, final report.

## COMMITTED (ui/e3c, never pushed; tree clean; 10 commits from 4400814)

d13396c Engine: fold of the write-ahead log (finding 1; simple shape)
b3cd198 Cli: si bench --read-links / --max-log-mb, CI step (finding 1's gate)
9c9cf8c Engine: a crawl that hasn't crawled has spent no time (finding 3)
b9dc71c Engine: stretches in order; time asleep left out (finding 4)
5a23e90 Tests: the eight mutations (finding 5)
126eaeb Sidecar: a kept /stats takes no read slot (finding 7)
1996a6a Cli: readbench gates stats-kept 10, stats-pass 450 (finding 2)
dabf0d2 Engine: sitemap counts robotsDisallowed, redirectingOrBroken + the test-site plant (addition A)
288e816 App: types (addition A fields; finding 6 comments)
a01a416 Docs: API doc + README (findings 1-7, 6's table, additions A and B)

Before committing, on the same code: suites by dll path 1004 engine + 166 sidecar + 17 CLI passed; app npm ci, typecheck,
lint exit 0; vitest 873 passed / 1 skipped on the second run (first run: OverviewScreen.alerts test timed out at 5 s
under load; passed alone and on rerun; not touched by this work).
RUNNING on the committed tree: Release build + `dotnet test engine/ShoutingIguana.Engine.slnx`. Then the final report.
Not verified: no CI run; nothing at 1M URLs; no real site crawled; the stop-and-reread code is only in mine-src\ (dropped).
