# Checkpoint: merge ui/step-3 into ui/sitemaps-robots

Brief: ..\brief-sitemaps-merge.md. Worktree C:\Projects\ShoutingIguana\.claude\worktrees\sitemaps-robots (branch ui/sitemaps-robots, was fe51eb0).
ui/step-3 = a782baf. Scratch: this folder; tmp\ is TEMP/TMP for dev + e2e.
Ports: SI_DEV_SERVER_PORT=58874 SI_DEV_SIDECAR_PORT=58875 SI_PREVIEW_PORT=58876 SI_E2E_SIDECAR_PORT=58877 SI_STORYBOOK_PORT=6606.
Rules: never push; only this worktree; no bare git stash; never rename exported fn/component; stay out of engine/; stop only own pids/ports; one heavy thing at a time; never `dotnet build-server shutdown`; don't edit docs/v2-rebuild.md.
Previous fixer's scratch (read-only, reuse tools): C:\Users\LeeMessenger\AppData\Local\Temp\claude\e2e5beaddde4c070\f209576d-c3b5-4cef-9d6c-2666276ff82b\scratchpad\sitemaps-fixes (tools\e2e.sh, live\shots*.mjs, tmp\si-app-dev 900MB analysed copies - by an OLDER engine).

## State
- `git merge --no-commit --no-ff ui/step-3` started. Conflicts (8): app/e2e/overview.spec.ts, app/src/api/grids.ts, app/src/api/queries.ts, app/src/grid/GridCore.tsx, app/src/keyboard/registry.ts, app/src/screens/UrlsScreen.tsx, docs/v2-ui-architecture.md, docs/v2-ui-ia.md.

## Conflicts settled
(fill in)

## Next
resolve -> typecheck/unit -> commit merge -> 87 checks/404 words -> GridScreen+minRows+hidden window -> columns vs real sidecar (garden, 100k) -> docs -> full checks -> screenshots -> report.

## Progress (12 Nov)
- MERGE COMMITTED: d903ca6 (8 conflicts settled, see its message). Semantic contradiction: 2 of my assertions (GridCore.cells.test, GridCore.rowKey.test) expected onActivate(index,row) exactly; grid doc says onActivate's third arg is the column -> they now expect it. REPORT.
- After merge: typecheck, lint, format clean; unit 113 files 1287 passed 1 skipped (after the 2 assertion edits). build:sidecar exit 0 (merged engine).
- 87 checks / "404 Not Found": NOTHING of mine broke. Engine only changed analysis details (HttpStatusNames); the crawl file's sitemap/robots `error` is still "HTTP 404" (SitemapDiscoverer.cs:161, RobotsCache.cs:166, RobotsReader.cs:241), so Sitemaps/robots.txt still say "HTTP 404" while issue details say "404 Not Found": REPORT as inconsistency / engine ask.
- e2e/support.ts no longer copies colourTweensEnd: re-exports e2e/colourTweens.ts.
- Extra git-ignored fixtures only in my worktree (api/garden|synthetic/{robots*,sitemap*}.json, optionalFixture in the mock): run final stories/unit with them moved out, as ui-step-3 has none.
- DEV APP RUNNING (task bx3pzjov6): sidecar pid 42084 (58875), vite pid 42316 (58874). STOP BY PID. token shouting-iguana-development-token-not-a-secret. Data: tmp\si-app-dev\data.

## Plan for the frame
- SitemapDetail -> GridScreen + GridScreenBody + minRows 5 (like URLs).
- SitemapList / RobotsOrigins (lists sized to rows): GridScreen frame; grid minRows = min(5, rows).
- RobotsFileView: split; stacked under @max-split with --robots-pane-height. Blocked grid minRows.
- 200% zoom: count columns off-screen on the list -> look at it at 640x360 first.
- hidden window: my reads are all 'grid' keys (bridge skips hidden) + robotsFile refetchInterval (TanStack pauses when hidden): add a test.

## Frame work DONE in the working tree (uncommitted at this line; see git log for commits after)
- SegmentedControl `wrap` (+ .si-segmented--wrap, story row in Fields.stories).
- SitemapDetail: GridScreen/GridScreenBody/minRows 5; quick filters wrap; URL grid's analysis words (analyseWords by reason in NeedsAnalysis, UNAVAILABLE_WHILE_ANALYSING).
- SitemapTreeGrid + SitemapList, RobotsOrigins: GridScreen frame; list wrapper shrink-0 when <=5 rows else shrink + GridCore minRows 5; NARROW (measured width > 0 and < all columns): counts straight after the name (Sitemaps: url, 5 counts, state, foundVia; Origins: origin, blocked, state, read, size). Token --list-grid-min-height removed.
- RobotsFileView: GridScreen (+ @max-split:h-auto), panes = GridScreenBody as grid; blocked card min-h (not h) when stacked; BlockedUrls wrapper without min-h-0; minRows 5; refetchIntervalInBackground: false.
- e2e/gridView.ts (gridInView, scrollToFoot) shared with grid-zoom.spec.ts. Sweep: engine's two counts asserted present, made crawl has max robotsDisallowed 1, 'Redirecting or broken' walked, page's counts == list's; new test "sweep: at 200% zoom ...".
- Unit tests added (SitemapsScreen.test: 'Sitemaps in a short or narrow window' 5, 'one sitemap without the URL summary' 2; RobotsScreen.test: hidden 1, 'robots.txt in a short or narrow window' 5).
- SEEN FAILING without: proofs/frame (9 failed), proofs/analyse-words (2 failed), hidden (refetchIntervalInBackground true -> "expected 2 to be 0"), e2e zoom (proof-zoom.log: counts off-screen).
- Measured in dev app (look/before, look/after1): 640x360 sitemaps list counts 261-537 in window (was 571+), detail no sideways scroll (was 814 vs 584), origins Blocked 341-461; 1280x720 unchanged order, no scroll; 1280x420 file view blocked grid 202px (5 rows).
- Real sidecar /sitemaps: garden 1 sitemap failed 404 (all 0s -> dashes), 100k: 13 sitemaps, robotsDisallowed 0 everywhere, redirectingOrBroken 2 on index+extra.xml. Dev crawl ids: garden b247d86f91da7fc1, 100k 84bd596d43b97c2a (analysed by merged engine).
NEXT: full unit, commit; sitemaps.spec tighten (numbers not dashes); run sweeps with SI_E2E_CRAWL on garden + 100k copies (how: see old checkpoint / global-setup); docs; final cycle; screenshots; stop dev app (pids 42084, 42316).

## Committed (12 Nov): d903ca6 merge; 500160e segmented wrap; b4d2d11 frames; 542ac76 sweep + zoom; then sitemaps.spec tighten; then docs.
- Dev app STOPPED by pid (42316 vite; sidecar went with it). Ports free.
- Sweeps on real crawls (SI_E2E_CRAWL from tmp\si-app-dev\data copies): garden 3 passed (1 sitemap 5 dashes; 17 origins, 2 opened; zoom 3 of 4 views); 100k 3 passed (13 sitemaps, 107 landings, 22 asked, 10 dashes; zoom 4 of 4). Logs checks\sweep-garden.log, sweep-100k.log.
- sitemaps.spec 10 passed with Blocked 1 (/private/listed) on the seeded crawl.
NEXT = FINAL CYCLE on the final commit, one at a time: move the 10 extra git-ignored fixtures out (restore after), npm ci, typecheck, lint, format:check, test x3, build-storybook, test:stories (+--forced, --reduced-motion), node --test scripts, build:sidecar, e2e full, my specs x10 (sitemaps, robots, sweep, + grid-zoom since I touched it), screenshots (1280x720 both themes + 200% zoom) into app/test-results/sitemaps-robots AFTER last e2e, report.

## FINAL CYCLE on d2a6629 (started ~17:00 12 Nov); results in checks\final-summary.txt (one line per step: exit, secs, head, dirty)
- The 10 extra git-ignored fixtures MOVED to fixtures-kept\{garden,synthetic}\ (so the tree's fixtures == ui-step-3's). RESTORE at the end: mv back into app/design/data/fixtures/api/<which>/.
- Green so far: ci, typecheck (+ --force), lint, format, test x3 (113 files, 1300 passed, 1 skipped; third while Storybook built), scripts (38 pass), build-storybook.
- RUNNING: stories (task bm1bslgay; monitor bqxze6msv). THEN one at a time: step.sh stories-forced npm run test:stories -- --forced ; stories-reduced -- --reduced-motion ; build-sidecar ; e2e (npm run e2e) ; x10: npx playwright test e2e/sitemaps.spec.ts e2e/robots.spec.ts e2e/sitemaps-robots.sweep.spec.ts e2e/grid-zoom.spec.ts --repeat-each 10 ; screenshots (live\shots-serve.mjs in background -> READY line with pids; copy live\shots.mjs to app/.shots.mjs, run from app: node .shots.mjs <100k id> <garden id> <local id>; delete app/.shots.mjs; stop serve by pid).
- Extra sweep: local-site crawl copy (tmp\si-app-dev\data\127.0.0.1-20261111-085304.sicrawl, from the fixer's scratch): 3 passed, 1 sitemap 0 dashes (expects Listed 312 / Crawled 302 / Blocked 10: confirm numbers via API at screenshots).

## 17:05: FLAKE in the shared story check, fixed -> NEW FINAL COMMIT, cycle restarted
- On d2a6629: stories default 286 x 2 no violations (789 s); --reduced-motion 286 no violations (953 s); --forced DIED at 275 s with "frame.evaluate: Error: Axe is already running" (checks\final-stories-forced.log; summary kept as checks\final-summary-on-d2a6629.txt). Not a violation: the a11y addon's own axe run in the page (preview.tsx a11y test 'error') overlapped ours.
- Fix committed: scripts/lib/axe.mjs whenAxeIsFree (+ axe.test.mjs, 3 tests) used by scripts/check-stories.mjs. Proof with real libs: tools\axe-busy-proof.mjs -> proofs\axe-busy.log ("without: ...Axe is already running"; "with: a result after 7 tries").
- So EVERYTHING is rerun on the new HEAD (see git log -1): ci, typecheck, lint, format, test x3, scripts, build-storybook, stories x3 (13 min each), build-sidecar, e2e, x10, screenshots. Summary in checks\final-summary.txt (reset).

## 17:50 resumed after a usage stop. Coordinator thought the sweep edit was lost: it is committed (542ac76), HEAD 16a936d clean. Commits from now end with Co-Authored-By: Claude Opus 5.5.
On 16a936d green: ci, typecheck (force + npm), lint, format, scripts 41, test x3 (1300 passed, 1 skipped; third under load), build-storybook, stories 286x2 / --forced / --reduced-motion no violations.
NEXT: build-sidecar, e2e full, x10, screenshots, restore fixtures, report.

## FINISHED 19:45 12 Nov. HEAD 16a936d, tree clean, NOT pushed. All final checks green on 16a936d (checks\final-summary.txt): ci, typecheck, lint, format, scripts 41, test x3 1300/1 skipped, build-storybook, stories 286x2 / forced / reduced no violations, build-sidecar, e2e 111 passed, x10 (sitemaps, robots, sweep, grid-zoom) 290 passed.
Screenshots: 92 in app/test-results/sitemaps-robots (after the last e2e). Local-site crawl analysed by merged engine: 312 / 302 / 10 / 0; grid refused 10. Fixtures restored. All my processes stopped; ports free.
