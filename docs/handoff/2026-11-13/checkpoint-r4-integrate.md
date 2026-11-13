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
