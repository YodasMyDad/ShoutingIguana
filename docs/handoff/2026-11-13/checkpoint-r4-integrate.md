# HANDOFF (paused by the orchestrator, 2026-11-13)

**Head `37a58c3` on `ui/r4-integrate`; the tree is clean** (no WIP commit needed: every change is committed; only
final checks are unfinished). Not pushed, not merged anywhere. Every process I started is stopped (by PID; ports
51874-51877 and 5956 free).

## Brief items
1. Merge A, B, C in order: **done** (ce1b472, 646fb43, 25dc40c), plus ui/step-3 e350053 (E7) as 8e35411 (clean).
2. Mode table `overview` row: **done**. A's `simple/SimpleOverview` kept; its finished branch renders B's
   `SummaryScreen` (static import); B's running/analysing fallback to Advanced's Overview removed. Hand-over focus to
   the Summary's title held by a unit test and e2e (simple-home).
3. Every `TODO(round4-merge)`: **done**, `git grep -n "TODO(round4-merge)"` is empty. B's ScoreRingStandIn + CSS and
   C's Overview ring -> A's `ui/ScoreRing`; C's ScoreBreakdownStandIn -> B's `ScoreBreakdownFromRoute` (gained an
   optional `onCloseAutoFocus` prop); B's handOver.ts sheet export and C's saveReportFile/reportFile -> hand-over's
   `openExportDialog` (Report: `overviewOffer(item, crawling, simple)` with `{choiceId, cut}`; problem:
   `checkSheetOffer(item, check)` with choiceId `check`). Shell: ONE `reveal_export_file` (hand-over's) + its
   `open_export_file`, one grant; C's ExportItem/BadExportId/own command dropped; capabilities test lists both.
   Also removed the parts' `SIMPLE_SCREENS.report ?` gates (the row is set now); the frame's own gates stay.
4. Links walked end to end: **done** in e2e (new `e2e/simple-links.spec.ts` + additions to simple-home): Home row menu
   -> Report; Summary's Open the report -> Report; Summary count -> What to fix part; Ctrl+1/2/3 in Simple; Ctrl+8 in
   Advanced; Overview score -> breakdown (Close and deep link + Esc, focus back to score); checking -> Summary with
   focus on its title; finished toast Open the report -> Report in both modes; Report Save/Spreadsheet -> export
   dialog on their choice and cut.
5. Favicons: **done** (SiteRow uses `CrawlFavicon`).
6. Filled-project tests: **done as far as possible**: the Simple screens' through-the-app tests can't run in `filled`
   (it stubs every row by design); the conditional report-row assertions now assert the report in both projects.
7. Docs: **done** (architecture "Simple mode (round 4)" one account; v2-ui-simple "As built").
Orchestrator extra: pagesAnalyzed beside the score (Home rows, Summary head; `scoredPages` in simple/home/sites.ts):
**done** (37a58c3), unit tests seen failing without the change, e2e home row updated (not yet run since).

## Conflicts settled
- live/api.rs: hand-over's export read + A's problems, both on one private `get`; C's export read dropped.
- notify.rs imports both; A's `Notice.title: String` broke the hand-over's `export_notice` (the r4-home merge commit
  doesn't compile in Rust) -> fixed with `Notice::new` (commit "Shell: the export notice…").
- build.rs, capabilities/default.json, main.rs, tests/capabilities.rs, commands.rs, shell/ipc.ts, shell/os.ts: HEAD
  (hand-over). simpleScreens.ts overview: A's row. OverviewScreen header: score + ExportButton. smoke-shell.mjs: export
  step, then C's report print step. Architecture doc: all sections, later rewritten as one.

## Run on 37a58c3, all green
npm ci; build:sidecar; typecheck; lint; format:check; node --test scripts (43); npm test x3 (320 files, 3394 passed,
54 skipped each; run 3 under build-storybook load); build-storybook; cargo fmt --check, clippy -D warnings, cargo test
(118 + 3).

## NOT run on 37a58c3 (next steps, in order, one at a time, via `bash <scratch>/checks.sh <step>`)
1. `checks.sh stories stories-forced stories-reduced` (~23 min each; on 4c34de7 all three were 481 stories x 2
   themes, no violations; storybook-static is already built from 37a58c3).
2. `checks.sh e2e-filled`, then `checks.sh e2e-parts` (the 4 specs, --repeat-each 5), then `checks.sh e2e` (full, last).
   On fa4b511/4c34de7 the 4 specs passed once (links 4, home 5, results+report earlier 18 incl.); never with
   --repeat-each 5, and e2e:filled/full e2e never run on this branch.
3. Release build: from app/, `npm run tauri build -- --no-bundle` (sidecar binary already built for 37a58c3), then
   `npm run smoke:shell` (needs the desktop session): export step + the Report's print step. Not run at all here.
4. Write the final report (brief "Report back").

## Traps
- Ports are 51874-51877, Storybook 5956; TEMP/TMP must be the scratch `tmp` (checks.sh sets both).
- Bash tool moves a command to background after ~180 s: run long steps with run_in_background and wait on their
  output file; checks.sh prints "<step> <exit>" and ALLDONE.
- Stopping a background task leaves node/chromium alive: kill by PID via the listener on your port and its child tree
  (checks.sh's bash also keeps going to its next step unless killed).
- `e2e/simple-links.spec.ts` uses the seeded crawl and default (Advanced) storage for its Advanced test.
- The Report's Save in a browser now goes through the export dialog's "Save as" field (C's localStorage seam is gone).
- Inspector skeleton flake seen on 4c34de7 is fixed by E7 (deterministic test); not seen on 37a58c3.

# r4-integrate checkpoint

Worktree C:\Projects\ShoutingIguana\.claude\worktrees\r4-integrate, branch ui/r4-integrate from db6b69e (ui/step-3 + handover).
Ports 51874 dev, 51875 sidecar, 51876 preview, 51877 e2e sidecar, Storybook 5956. TEMP/TMP = scratch r4-integrate\tmp.
Brief: scratchpad\brief-round4-integrate.md. Commits end with Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>.
npm ci done, fixtures copied, build:sidecar done (binary in src-tauri/binaries).

## Commits so far
- ce1b472 merge r4-home (conflicts: live/api.rs both fns on one get; notify.rs imports; arch doc both). NOTE: this
  merge didn't compile in Rust (A made Notice.title String; handover's export_notice used &str) -> fixed in shell commit.
- 646fb43 merge r4-summary (overview row: kept A's SimpleOverview; doc both)
- 25dc40c merge r4-report (shell: kept handover's reveal/open_export_file, grant, ExportJob; dropped C's ExportItem,
  BadExportId, own reveal; Overview header = OverviewScore + ExportButton; smoke: export step then report print step)
- shell: Notice::new in export_notice
- App: SimpleOverview finished -> SummaryScreen (static import); Summary's OverviewScreen stand-in dropped; rings ->
  ui/ScoreRing (Overview md, dialog md, Summary lg); Overview -> ScoreBreakdownFromRoute (+ optional onCloseAutoFocus
  prop added to it and ScoreBreakdownDialog); plainWords.bandWord delegates to ui/score scoreWord.
- App: Report Save/Spreadsheet -> openExportDialog(overviewOffer(item,crawling,simple),{choiceId,cut}); deleted
  saveReportFile(.test), reportFile(.test). Problem Save as a spreadsheet -> handOver.checkSheetOffer + saveCheckSheet
  (item, check) opens dialog on 'check' (title 'Save as a spreadsheet', plain title file name).
- App: SiteRow CrawlFavicon; removed SIMPLE_SCREENS.report gates in parts' screens/tests (frame's gates kept);
  e2e/simple-links.spec.ts new; simple-home spec walks Summary focus + toast Open the report (both modes).

## Verified
- tsc, eslint, prettier clean; vitest touched files pass (app project); cargo fmt/clippy/test (118+3) green.

- fa4b511 tests (report.spec save via dialog path field; links spec fixes); 4c34de7 docs (arch one account
  "Simple mode (round 4)", v2-ui-simple "As built"). git grep TODO(round4-merge) empty. HEAD = 4c34de7.

## Final checks on 4c34de7
- typecheck, lint, format:check, node --test scripts (43) green.
- unit: run1 green (3386 passed/52 skipped), run2 FAILED 1 (Inspector skeleton timing, pre-existing, unchanged since
  db6b69e; 3/3 alone), run2b green, run3 green (maybe under stray sb build), run4 under real build-storybook load:
  same Inspector flake in filled project. Not round 4's; reported, not fixed.
- build-storybook green.
- e2e (before docs commit, code same): parts' 4 specs green after fixes (links 4, home 5).

- On 4c34de7: stories plain/forced/reduced 481x2 no violations.

## Orchestrator round 2 (done)
- 8e35411 merged ui/step-3 e350053 (E7) cleanly. 37a58c3: scoredPages(item)=pagesAnalyzed??pageCount on Home rows
  and Summary head; tests fail without (verified via path-stash), pass with; e2e home row uses pagesAnalyzed.
- ALL final checks to redo on 37a58c3 (engine changed: build:sidecar again).
- On 37a58c3: npm ci, build:sidecar, typecheck, lint, format, scripts 43, unit x3 (3394 passed/54 skipped each; run 3
  under build-storybook load), build-storybook, cargo fmt/clippy/test (118+3) all green.
- Running (bg beq2ka2zf via scratch checks.sh): stories x3, e2e:filled, parts --repeat-each 5, full e2e. Logs f-*.log.
- Then: release build (tauri build) + smoke:shell.

## Next (old list)
- test:stories (+forced, +reduced-motion), e2e:filled, parts' specs --repeat-each 5, full e2e last, build:sidecar,
  cargo fmt/clippy/test, release build + smoke:shell.
