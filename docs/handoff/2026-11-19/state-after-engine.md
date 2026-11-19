# Engine lane: state after the closing run (2026-11-19, afternoon)

Branch `ui/ux-engine`, worktree `C:/Projects/ShoutingIguana/.claude/worktrees/ux-engine`, base `4a7fab91`,
head **`a2f9db6f`** (was `c71fe5bd`). `git status` clean. Nothing pushed. Nothing of this lane is running; no port was
used. `AnalysisFreshness.VERSION` is still **21** (the two new commits change no analysis result).

## Commits added in this run

- `324e4f22` Engine: an export says a lost sitemap list in the screen's words. `Export/SitemapListWords.cs` now writes
  "Read, but its 240 URLs are lost" (singular "1 URL is lost"), as `sitemapWords.ts` does; `LostSitemapListTests` holds both.
- `a2f9db6f` Engine: a sitemap whose body breaks off on a recheck keeps what the crawl read. The open decision of step 4,
  decided as "turned away is not an answer": a connection that breaks, or a timeout, after the status came
  (`SitemapRecord.BrokeOff`, set in `Sitemaps/SitemapDiscoverer.cs`, read in `Storage/CrawlWriter.Recheck.cs`
  `ApplyRecheckSitemap`) is no answer on a recheck; a crawl still stores the part. Broken XML and a bad gzip are still
  stored (the file's own state); a file cut short with the connection closed in order can't be told from broken XML
  and is stored as that. `RecheckTurnedAwayTests.ASitemapWhoseBodyBreaksOffKeepsWhatTheCrawlRead` was seen failing
  first (the crawl's rows replaced), then passing; `ACrawlStoresThePartOfASitemapThatCameBeforeItBrokeOff` pins the
  crawl. `Support/TestWebHost.cs` gained `CutShort(path)` / `SendWhole(path)`. Words added to `docs/v2-fix-loop.md`.

## What ran at head `a2f9db6f`, exactly

`bash docs/handoff/2026-11-13/tools/verify-engine.sh <ux-engine> <logs>`, once, all four steps exit 0, while other
lanes' Vitest runs loaded the machine. No threshold failed, so nothing was rerun. Logs:
`_handoff/2026-11-19/lanes/engine-fix/logs/verify-a2f9db6f/` (`summary.txt`, `build.log`, `test.log`, `bench.log`,
`bench.json`, `readbench.log`, `readbench.json`; the 530 MB bench crawl was deleted).

| Step | Result |
| --- | --- |
| build (`-c Release`) | exit 0, 0 warnings, 0 errors |
| test | exit 0 (397 s). Engine 2,419 passed, 0 failed, 0 skipped; Sidecar 331 passed; CLI 45 passed |
| bench `--pages 100000 --concurrency 16` | exit 0. "Completed, every planted issue found"; 101,218 of 101,218 pages, 110,390 URLs, 5,159,430 links in 60.4 s; 1,676 pages/s; peak 208 MB working set (managed heap peak 83 MB); flatness 206 MB at 10-30% -> 183 MB at 70-100% (-24 MB), -356 bytes per URL; analysis 16.0 s, 32,144 issues, peak 320 MB |
| readbench `--runs 20 --gate` | exit 0 (1,308 s). 229 reads of 112,716 URLs, all 23 gates inside. Slowest: Links grid search for a common word (every link read) 1,502.1 ms p95 (gate 2,500). Nearest to its gate: stats-pass 313.4 ms (gate 450), crawl 120.1 ms (gate 185) |

App, in `C:/Projects/ShoutingIguana/.claude/worktrees/ux-engine/app` (no app file changed in this run):

- `npm run typecheck`: exit 0. `npm run lint`: exit 0. `npm run format:check`: exit 0, all files.
- `npx vitest run --maxWorkers=4 src/changes src/screens/sitemaps src/screens/rechecks src/screens/SitemapsScreen.test.tsx src/api`:
  44 files, 580 tests passed, 0 failed.

## What did NOT run

- The app's **whole** Vitest suite at head (last whole run was at `c315e050`: 510 files, 5,529 passed), `test:stories`,
  and every e2e spec (fix-loop, sitemaps). `npm run build:sidecar` was not run: the sidecar binary in this worktree
  predates `6b217e1f`.
- The live app (step 3 of the old list): the line under the Recheck button, Sitemaps after `c71fe5bd`, Simple's
  "Fixed it?" card, 1280 x 720. Nothing was looked at in the app in this run.
- The Linux Docker check. `smoke:shell`.
- The broke-off case with a timeout rather than a broken connection has no test (the code path sets the same flag).
- No test compares `response_details` and `response_html` row by row after a refused recheck (as before).

## Still not this lane's, not started

S-15's "and 3 more" under the list and the "Check again" form (simple and shell lanes' files); A-23's dialog width and
links (shell lane).

## For the merge

- **`IssueCatalog.cs` is untouched by this lane**, and no issue id, title or severity changed, so this lane alone gives
  no reason to recapture `app/src/test/simple/engine-checks.json`. Recapture it after the merge only if another lane
  changed the catalog or `IssuePlainTexts.cs`.
- `AnalysisFreshness.cs` (VERSION 21) and `AnalysisFreshnessTests.cs`: conflict if another lane also raised the version;
  take the higher and add one if both did.
- Engine files other lanes may also touch: `Analysis/PlainWords.cs`, `Analysis/SkippedCheckReasons.cs`,
  `tests/.../Analysis/PlainWords/plain-words.json`, `tests/.../Analysis/IssuePlainTextsTests.cs` (the count of skip
  reasons is pinned there: simple lane), `Analysis/CrawlAnalyzer.cs`, `Storage/AnalysisReader.cs`,
  `Storage/SitemapListReader.cs`, `Storage/RecheckReader.cs`, `Crawling/CrawlSession.cs`, `Fetching/HttpFetcher.cs`.
- App files in `Shared:` commits: `app/src/api/grids.ts` (the `listLost` state), `screens/sitemaps/sitemapWords.ts` and
  test, `screens/sitemaps/SitemapList.tsx`, `screens/SitemapsScreen.test.tsx` (data lane);
  `screens/rechecks/RecheckControl.test.tsx` (shell lane); `changes/recheckWords.ts` and test (this lane's own).
- Docs: `docs/v2-fix-loop.md`, `docs/v2-sidecar-api.md`.
- After the merge, rebuild the sidecar before any e2e or live run, and run the engine gates again on the merge: these
  results are for `a2f9db6f` alone.
- A si.exe seen running at the end (`ux-issues/app/.engine-build/.../si.exe testsite --pages 6000 --port 0`, PID
  120964) belongs to the issues lane, not to this one; it was left alone.
