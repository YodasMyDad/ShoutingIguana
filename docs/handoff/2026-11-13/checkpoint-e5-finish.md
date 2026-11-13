# REVIEW FIXES (2026-11-12, after the "DONE" below): read this first

Orchestrator's message: fix all 16 findings in `...\8e7045a4-...\scratchpad\e5-review\findings.md` on ui/e5 (head was
9b9409d). Rules: every fix has a test SEEN FAILING without it (revert fix -> fail -> restore with a fresh timestamp);
F1 proved in Excel with the reviewer's excel-formulas.ps1; F13 = journal of part files in the sidecar's state folder,
cleaned at next start (never a sweep by pattern); F5 changes the CSP hash (docs must stay true); F16 = missing tests;
disagree with evidence rather than fix; docs for 2, 6, 7, 14. Then merge ui/step-3 if past a782baf (catalog fixes:
4 titles, 16th skip reason robots-txt-ignored, analysis version 8), then the full checks, then a short report:
commits; table of 16 (fixed/disagreed, test, seen failing); anything new; test counts; verified / not.

Progress table (update as each lands):
| # | State | Test | Seen failing |
| - | - | - | - |
| 1 | FIXED, committed. Excel's CSV limit is 32,758 chars NOT 32,767 (probe.py/probe2.py + excel-probe.ps1/excel-shape.ps1: 32,758 whole, 32,759 splits, any alphabet/column). CsvTableWriter.MAX_FIELD_CHARS; Cli CsvWriter (si issues --csv) too. Excel proof: out/long-before.csv 6,306 rows + formula A24; out/long-after.csv 6,287 rows 0 formulas; issues + check CSV after: clean. Docs still to update (API doc Export "The files", TS truncatedCells comment, README). | TableWriterTests.ACsvFieldLongerThanExcelReadsAsOneCellIsCutAndCounted, GridExportTests.AUrlLongerThanACellIsCutInBothFilesAndTheResultSaysSo, CliTests.TheIssuesCsvCutsAFieldLongerThanExcelReadsAsOneCell | yes, all three (written first) |
| 2 | FIXED, committed (2nd commit). Wording: "Each is explained under the same number in “What to fix”, with the first pages it's on." / notices: "explained under “What to fix”." Docs to update. | ReportDocumentTests.TheSummaryCutIsTheFullReportsFirstPage, APerfectSiteHasNothingToFixAndAScoreOfAHundred, ResultsFromBeforeTheScoreHaveNoScoreAndSaySo | yes (before fix) |
| 3 | FIXED, committed. BlockedByRobots = score.Covers.BlockedByRobots ?? count of blocked-by-robots check. | ReportDocumentTests.WhatTheSitesRobotsTxtKeptOutIsTheSitesOwnAddresses (+ the refused start URL in ACrawlThatReadNoPageHasNoScoreAndSaysWhy) | yes (before fix) |
| 4 | FIXED, committed. ReportModel.CrawlWentOn; OutOfDate always has CrawlChanged when true (CLI too); omits "Pages that can appear in search", "Not read", the unfinished note; Finished? says "Not known from these results: ..."; workbook Finished? too. Docs to update. | ReportDocumentTests.ACrawlThatWentOnAfterItsResultsLeavesOutWhatIsOfTheCrawlNow | yes (before fix) |
| 5 | FIXED, committed. CSS rule `.examples td, .examples--one .found, .problem .onefix { overflow-wrap: anywhere; }`. overflow.mjs: before 118 elements past the edge (2,632 px), after 0; garden still 12 A4 / 13 Letter, 100k 33/34. | ReportDocumentTests.ASitesUnbrokenTextWrapsWhereItIsPrinted | yes (before fix) |
| 11 | FIXED, committed. | WhatTheSitesRobotsTxtKeptOut... (score's sentence) and ACrawlThatReadNoPage... (no-score sentence) | yes (before fix) |
| 12 | FIXED, committed. "addresses" + "They all lead to the same address" for IssueSubject.Url. | ReportDocumentTests.OneFixCountsAddressesForACheckOfAddresses | yes (by mutation m01.py; its first failures were set-up errors) |
| 16f | examples by order: test added in TheReportSaysWhatTheAnalysisFound | same | yes (mutation m01.py: rows reversed) |
| 6 | FIXED, committed 9c3aab9 (clamp in ExportRun.Wrote). Doc to update (rowsExpected). | RunningCrawlExportTests.AnExportOfARunningCrawlNeverWritesMoreRowsThanItSaysItExpects (reproduced 560 of 540) | yes (before fix) |
| 7 | FIXED, committed 9c3aab9. CRAWL_CHANGED = "...it was analysed again, continued or it finished. Export it again." Docs to update (API doc, README). | ExportFileTests.ACrawlThatChangesUnderAnExportEndsIt (literal), CrawlChangedExportTests (literal); + ACrawlPausedAndResumedUnderAnExportDoesNotEndIt (behaviour, passes before and after) | yes (before fix, the literal ones) |
| 8 | FIXED, committed 9c3aab9. ExportTarget.Check refuses Path.GetInvalidFileNameChars. | ExportFileTests.AFileNameWithACharacterAFileCannotHaveIsRefusedBeforeAnythingIsWritten (Windows only) | yes (before fix: NotWritable) |
| 13 | ENGINE PART committed 9c3aab9 (IExportJournal, ExportSettings.Journal, ExportTarget tells it). SIDECAR PART TO DO: ExportJournal in state dir + recovery at start + test. | ExportFileTests.EveryPartFileIsNotedBeforeItIsThereAndOnceItHasGone | yes (mutation m02) |
| 15 | DONE, committed 9c3aab9 (internal ExportSettings.MaxXlsxRows). | GridExportTests.MoreRowsThanAWorkbookHoldsAreRefusedBeforeAnythingIsWritten | yes (mutation m02) |
| 16a | combined filter+search+sort (URL + links theories) | GridExportTests COMBINED cases | yes (mutation m02: search dropped) |
| 16b | literal titles and words | GridExportTests.EveryColumnsTitleAndEveryValuesWordIsWrittenOutHere | yes (mutation m02) |
| 16c | real continue / real finish | CrawlChangedExportTests.ACrawlThatIsContinuedUnderAnExportEndsIt, ACrawlThatFinishesUnderAnExportEndsIt | yes (mutation m02) |

| 9 | FIXED c6d2657: filter value typed by column type. | CliTests.AFiltersValueIsOfItsColumnsType | yes (before fix: "eq needs text") |
| 10 | FIXED c6d2657: ExportFields (engine) shared by sidecar + CLI. | CliTests.ExportMistakesGetOneLineMessages (new lines) | yes (before fix: exit 0) |
| 13 | DONE 5f01b34: sidecar ExportJournal (state/exports-in-progress.json), Recover() in SidecarHost.StartAsync before serving. | ExportEndpointsTests.AStartDeletesThePartFiles..., AnExportsPartFilesAreNoted... (+ engine ExportFileTests.EveryPartFileIsNoted...) | yes (mutation m03) |
| 16d | DONE 5f01b34 | ExportEndpointsTests.ACrawlAnalysedAgainUnderAnExport..., ADiskThatFillsUp..., MoreRowsThanAWorkbookHolds..., AnExportsProgressIsToldAtMostEveryQuarterSecond, TheLastSixtyFour... | yes (mutation m03) |
| 16e | DONE 5f01b34 | ExportEndpointsTests.ShutdownCancelsAnExportThatIsWritingItsFileAndLeavesNoPartOfIt | yes (mutation m03: part file not deleted) |
(Resumed after a usage-limit pause; commits from 5f01b34 on are Co-Authored-By Claude Opus 5.5.)

| 14 | FIXED a078c1c (docs only: API doc + v2-ui-simple.md said "pag…", engine writes "pages…"). Code was right. | existing TableWriterTests.ASheetsNameIsOneExcelTakes ("3 Large images that slow pages…") | n/a: doc error, no code change |
Docs for all findings committed a078c1c ("Engine changes from the E5 review" section added). ui/step-3 still a782baf: no merge needed.
FINAL CHECKS on a078c1c: Release build 0/0 done; Debug tests running (out/test-debug2.log); then Release tests, bench, readbench, app.

REVIEW FIXES DONE on a078c1c (not pushed). Checks: Release build 0/0; Debug and Release tests Engine 1278, Sidecar 228,
Cli 25 all passed; si bench 100k exit 0 (every planted issue, 192 MB peak, +3 MB flat, 2,948 pages/s); readbench --gate
exit 0 first run (closest link-pass 1,724/2,500 ms); app npm ci, typecheck, lint, format:check clean, 1066 passed 1 skipped.
Final report sent.

STILL TO DO after 9c3aab9: F9, F10 (CLI); F13 sidecar journal; F16d sidecar tests (export-crawl-changed, export-disk-full,
export-too-large, 250 ms spacing, last 64); F16e shutdown cancels an export that HAS opened its file; F14 + all docs
(API doc: CSV 32,758 + truncatedCells, F2 wording, F4 crawl-went-on, F6 rowsExpected, F7 pause, F8 invalid name, F13 journal,
F14 sheet name; README; TS comments truncatedCells); merge ui/step-3 if moved; full checks; report.
Mutation tool: `python mutate.py apply|restore mNN.py` (restores byte-for-byte with a fresh timestamp).

Notes: bash heredocs mangle backslashes here: write scripts with the Write tool (rNN.py use patch.py's patch()).
Plan per finding is test first (see it fail), then fix. For test-gap findings: mutate product, see fail, restore + touch.
F4 plan: model gets crawl-went-on from results.OutOfDate(any settings).Contains(CrawlChanged) even when OutOfDateBy is null.
F3 plan: BlockedByRobots = score.Covers.BlockedByRobots, or the blocked-by-robots check's count when there is no score.
F6 plan: clamp (Expected never below Rows) + doc. F13: IExportJournal in ExportSettings; sidecar ExportJournal in state dir.
F15: internal ExportSettings.MaxXlsxRows for tests.
# RESUMED 2026-11-12 by a second agent (brief: ...\8e7045a4-...\scratchpad\brief-e5-finish.md)

Read this section first; the HANDOFF below is what it started from.

**Phase A done (measure, pages, Excel):**
- `ui/step-3` merged at a782baf (commit daea5de, no conflicts, no engine change in it).
- Scratch copies re-analysed by the merged engine: 100k (score 0, Poor; 101,106 pages, 110,251 URLs, 85 of 87 checks),
  garden-hostile (85). garden was already.
- Report measured twice each: garden full 94.2 KB, 0.3 s, 42-45 MB peak, 12 pages A4 / 13 Letter; garden summary
  18.3 KB, 1 A4 / 2 Letter; 100k full 210.2 KB, 0.5 s, 97 MB peak (34 private), 33 A4 / 34 Letter; 100k summary 38 KB,
  3 / 3. Workbook: garden 61 KB, 20 sheets, 933 rows, 0.3-0.4 s, 46-55 MB; 100k 1.30 MB, 76 sheets, 32,402 rows,
  0.9 s, 98 MB (47 private). Design's report.html by the same tool: 82.1 KB, 11 A4 / 13 Letter.
- Every A4 page of the garden report looked at beside the design's (out/sbs-p1..12.png): same look on every page;
  differences are the catalog's words and order, "Checked once, for the whole site" lines, and About's rows.
  100k pages 1-3, 7-8, 23-24, 33 looked at too.
- Two faults found in print and fixed (commit 6f0d19f): the score table's foot repeated on every page of a long
  table (100k: three times); a score of 0 drew a dot on the ring. Tests in ReportDocumentTests.
- printpages.py (headless Edge) began to hang on this machine: use `node print.mjs <html>...` instead (the app's
  Playwright, the document as it is with its policy, A4 and Letter, closes its browser). Same page counts as Edge.
  `pages.py`, `sidebyside.py` make the images. No Edge process was left running (checked by PID listing).
- Excel 16.0 build 20527 by automation: garden.xlsx, 100k.xlsx, garden-check.xlsx, garden-urls.xlsx,
  garden-links.xlsx, hostile-workbook.xlsx, hostile.xlsx, hostile-pages.xlsx, hostile.csv, hostile-pages.csv all
  opened with no repair; 0 formulas; frozen bold filtered headers; summary sheet's dates are dates
  (yyyy-mm-dd hh:mm), numbers numbers, share a percentage; the 23 hostile titles are text, the formula-like ones
  with Excel's apostrophe prefix (xlsx) or a literal apostrophe (csv).

**Phase B done (app, docs), all committed on ui/e5, not pushed:**
- 6f0d19f Engine: score table prints its sum once; a score of 0 has no dot. (ReportDocument.cs + test)
- 64a0f66 App: `export` in `SidecarEvent` (types.ts), `parseExportJob` + `isExportOver` (exports.ts, exports.test.ts),
  live store parses it, hands it to onEvent listeners and keeps none (liveStore.ts + test), hello.exports read.
  app typecheck/lint/format:check clean, src/api + src/live tests pass (126).
- 9b9409d Docs: API doc "Exports (E5)" under Live events (exact shape, order, reconnect), "Report (E5)" (states,
  where score/order/words come from, ReportedOnce line, print, Showing it: CSP + sandbox as tried in Chromium 156,
  WebKit, Firefox with frame.mjs), "Engine changes E5 made" re-measured table + pages against the design; README.
- CSP findings (frame.mjs): app policy as it is -> styled; style-src 'self' -> unstyled; nonce + 'unsafe-inline' ->
  unstyled; report hash (alone or beside a nonce) -> styled; frame-src 'none' doesn't stop srcdoc; sandbox="" ->
  links do nothing, parent can't reach print (SecurityError); allow-same-origin allow-modals -> parent reaches print.
  NOT tried: the built Tauri shell (whether it adds a nonce to style-src), the print dialog itself.
- Grid exports measured once more on the merged engine: 100k URLs CSV 0.7 s/71 MB, xlsx 0.8 s/87 MB, links CSV
  18.7 s/75 MB/402 MB, links xlsx 20.4 s/93 MB/159 MB/5 sheets.

**Phase C (final checks on head 9b9409d), in order, one at a time:** Release build (done: 0/0), `dotnet test` Debug,
`dotnet test -c Release`, `si bench --pages 100000` (CI's thresholds, --db out/bench.sicrawl), `si readbench
out/bench.sicrawl --runs 20 --gate`, app `npm ci`, typecheck, lint, format:check, test. Then the final report.
Results are appended below as they come.
- Release build: 0 warnings, 0 errors.
- `dotnet test` Debug: Engine 1259, Sidecar 219, Cli 23, all passed (out/test-debug.log).
- `dotnet test -c Release`: Engine 1259, Sidecar 219, Cli 23, all passed (out/test-release.log).
- `si bench --pages 100000 --concurrency 16` with CI's four thresholds, once: exit 0. "Completed, every planted issue
  found"; 101,112 pages, 110,264 URLs, 5,054,060 links in 38.5 s; 2,629 pages/s; peak 194 MB; flat (194 -> 191 MB,
  67 bytes per URL); analysis 11.9 s, 32,410 issues (out/bench.log, out/bench.sicrawl kept for the read gate).
- `si readbench out/bench.sicrawl --runs 20 --gate`: exit 0 on its first run, every class inside its gate (7 min 27 s;
  closest: link-pass 1,055 of 2,500 ms, link-grid 129 of 200, text 92 of 190, stats-pass 193 of 450; out/readbench.log).
- app: `npm ci` ok; typecheck, lint, format:check clean; `npm test` 101 files, 1066 passed, 1 skipped
  (out/app-checks.log).

**DONE 2026-11-12.** Head 9b9409d on ui/e5, tree clean, not pushed. Final report sent to the orchestrator.
Not verified: LibreOffice/Numbers (not on this machine); the built Tauri shell's style-src and its print dialog;
the 159 MB links workbook in Excel itself (read part by part instead); a real full disk (a failing stream stands in).

# HANDOFF (2026-11-11: paused by the owner to save usage, mid phase two)

Resume from this section and the brief (`..\brief-e5.md`), then the orchestrator's phase-two message (quoted in
"Phase two asked for" below). Nothing else is needed from the session that wrote this.

**Where it stands:** worktree `C:\Projects\ShoutingIguana\.claude\worktrees\e5`, branch `ui/e5`, head `03877b6`,
tree clean, NOT pushed (never push). `ui/step-3` is merged in up to `d8efc41`.

**What was run on head 03877b6:** `dotnet build engine/ShoutingIguana.Engine.slnx -c Release`: 0 warnings, 0 errors. `dotnet test engine/ShoutingIguana.Engine.slnx -c Release`: Engine 1259, Sidecar 219, Cli 23, all passed. In `app/`: typecheck, lint, format:check clean; unit tests 880 passed, 1 skipped (as before). NOT run: `dotnet test` in Debug, the 100k bench, the read gate, Excel, the PDF pass (the owner's pause said not to start them).

## Phase two, item by item

| Item | State |
| --- | --- |
| Merge of `ui/step-3` | DONE. `c33cd68` (at 337f220, 4 conflicts, both sides kept: API doc and README in the order E3c review, score, E5; CliApp.cs; SidecarJsonContext.cs), then `03877b6` (at d8efc41: no change to the tree, it held the same two fixes already). |
| Report on the real score | DONE, tested. `ReportSources` reads `AnalysisResults.Score` / `.NoScoreReason`; `StandInScore` and my duplicate score types are deleted. A test changes the stored number and the report says it. |
| `fixFirst` | DONE, tested. Numbers in the report and sheets in the workbook follow `AnalysisResults.FixFirst`; no order of my own is left. |
| `ReportedOnce` | DONE, tested. "Checked once, for the whole site" in the problem's meta line, the "Fix these first" list and the workbook's "Of what" (with "Out of" and "Share" empty). In the score table: a site-wide check says "The whole site"; a check reported once that is NOT site-wide (`sitemap-error` is the one Error) says "Checked once, for the whole site (counts as a few)". That last wording is MINE, not the design's: say so in the final report. |
| `LooksFor` | DONE, tested. "What couldn't be checked" names a check by `LooksFor ?? Title` (`CheckWords.Named`); the workbook's rows for checks that passed or couldn't run use it too. |
| Limit tokens | DONE, tested. Every plain text goes through `PlainLimits.Fill(…, results.Analysis.Settings ?? new AnalysisSettings())`. Test `ALimitInACheckSWordsIsTheOneTheAnalysisRanWith`: no `{limit:` in the report, the workbook or a check's CSV; a crawl analysed with a title limit of 41 says 41. |
| Plain words, category titles, skip reasons | DONE, tested, with the technical texts still standing in for null. |
| Workbook's summary sheet | DONE, tested: the stored score, its word ("…, so far" when partial), "Score version"; no score says which reason. NOT opened in Excel since the merge. |
| States of the report | DONE, tested in `ReportDocumentTests`: stopped and failed (a copy set to that status and analysed again, so the score and the skipped checks are real), URL limit and depth limit (real crawls of the synthetic site), checks skipped (real reasons, plain words, and a later engine's reason falling back to its stored text), a perfect site and one with nothing but notices (`StoreTheScoreOfItsCounts` stores what the analysis would), no score `NoPages` (a real crawl whose start URL is 404), no score `NotScored` (the score columns nulled), never analysed. |
| Re-measurement | PART DONE. Garden copy (re-analysed with the merged engine: 85, Good, 265 pages, 4,521 URLs, 83 of 87 checks): full report 94.1 KB, 0.3 s, 42 MB peak, **12 pages on A4, 13 on Letter**; summary cut 18.2 KB, 1 page on A4, 2 on Letter. The design's `report.html` printed by the same tool: 82.1 KB, 11 pages on A4, 13 on Letter. Why 12 and not 11: the page breaks are the same through page 8; the catalog's words are about 5% longer than the mockup's (24,859 characters of text against 23,717), and "About this check" (`.facts`, kept whole) needs 41% of a page with 35% left on page 11. Why 94 KB and not 82: 10.4 KB is link markup (the design links nothing), 0.6 KB the larger sheet, the rest words. **100k NOT re-measured**, workbook NOT re-measured. |
| PDF look against the design | PART DONE. Page 1 on A4 looked at beside the design's page 1: the same, with the true numbers (83 of 87 checks, 64 found nothing wrong). The text of the whole report was diffed against the design's (`out/compare.txt`): same outline, the differences are the catalog's words, the catalog's category order, and three notices with one page each in catalog order. Pages 2 to 12 NOT looked at since the merge. |
| `si bench --pages 100000` | NOT RUN. |
| `si readbench --gate` | NOT RUN. |
| `dotnet test` in Debug (the default) | NOT RUN in phase two (Release was). |
| Excel | NOT reopened since the merge. |
| Docs | NOT updated for phase two. `docs/v2-sidecar-api.md`: "Report (E5)" (the bullets "Every state…" and "The score and the words": add the depth limit, the two no-score reasons, `ReportedOnce`'s line, `LooksFor`, limits filled from the analysis's settings, the URLs being the analysis's `crawledUrlCount`); "Engine changes E5 made" still has the report's pre-merge numbers on fallback words (garden 92 KB / 13 pages, 100k 203 KB / 34 pages): replace with the re-measured ones. `engine/README.md` "Export and the report": same. |
| Final report (the brief's "Report back") | NOT SENT: only a short stop message was. The contract itself is unchanged by phase two (no endpoint, field or command was added or changed). |

## Exact next step

1. `git -C <worktree> status --short` (expect clean) and `git log --oneline -3` (expect 03877b6 on top). If `ui/step-3` has moved past d8efc41, merge it again.
2. Re-analyse the two scratch copies still analysed by the pre-merge engine (they read as out of date and have no stored score):
   `si analyze crawls/synthetic-100k.sicrawl` and `si analyze crawls/garden-hostile.sicrawl` (`si` = the Release exe, path in `measure.py`).
3. Re-measure: `python measure.py "100k report" out/100k-report.html report crawls/synthetic-100k.sicrawl --out out/100k-report.html`, then
   `python printpages.py --png out/100k-report.html`; the workbook on both crawls (`export … --kind workbook --out …xlsx`); repeat a timing before trusting it.
4. Look at the garden pages (`out/garden-report.a4-p*.png`, made after the merge; delete the stale `garden-p*.png`, `garden-report-p14.png`, `garden-report.a4-p13.png` first) against `out/design-report.a4-p*.png`.
5. Open one workbook in Excel (`excel-open.ps1 -Files @(…)`), and the hostile one.
6. Update the docs (the row above), commit `Docs: …`.
7. `dotnet test engine/ShoutingIguana.Engine.slnx`; then `si bench --pages 100000` ONCE; then `si readbench --gate` (a class that fails: run it again alone before believing it, and say what was seen). One heavy thing at a time.
8. The final report, as the brief's "Report back" asks, with the contract (the section "Contract settled" below and the API doc).

## Traps

- Bash heredocs with apostrophes or backslashes break: write a script with the Write tool, run `python patch.py pNN.py` (p40 to p42 were phase two's). The Write tool can turn `\uXXXX` typed in content into the real character: run `python scan.py` after writing tests with escapes.
- The report reads the STORED score. A test that changes counts by SQL must store a matching score (`StoreTheScoreOfItsCounts` in `ReportDocumentTests`) or analyse again; `UPDATE analysis SET pages = 0` alone no longer makes "no score".
- `issues.issue` is the check's id (text), and `issue_types.severity` is text: build an `IN (…)` list from the catalog rather than comparing severities in SQL.
- `printpages.py` leaves headless Edge running (two `msedge.exe` with `--user-data-dir=…\scratchpad\e5\out\edge-profile`): stop them by process id when done. The session id in the path is shared with other agents' scratch folders: match on `scratchpad\e5\`, never on the session id.
- Only `crawls/garden.sicrawl` is analysed by the merged engine (analysis version 7). The other two copies are not yet.
- `ui/step-3` at 337f220 did not compile its sidecar tests and failed `format:check`; d8efc41 fixed both. Nothing to do, but don't be surprised by c33cd68's message.
- The design's report links nothing; the engine's makes every http/https address a link (decided in phase one, in the API doc). It is 10 KB of the garden report.

## Phase two asked for (the orchestrator's message, in short)

Merge `ui/step-3`; put the real score and plain words behind the interface; "Fix first" is the engine's order ("drop any
order of your own"); `ReportedOnce` prints "Checked once, for the whole site" in place of a share; `LooksFor` in "What
couldn't be checked"; fill `{limit:name}` with `PlainLimits.Fill` from the analysis's stored settings, with a test that
none survives into a report, a workbook or a CSV; then re-measure on the garden crawl (design: 11 A4 pages, about 84 KB)
and the 100k crawl; every state (stopped early, checks skipped, a perfect site, no score with each reason, never
analysed); the workbook's summary sheet with the score; the full checks; the 100k bench once; `si readbench --gate`;
print the garden report to PDF and look at the pages against `app/design/simple/report.html`; report back as the brief asks.

# E5 checkpoint (export, workbook, report)

Worktree `C:\Projects\ShoutingIguana\.claude\worktrees\e5`, branch `ui/e5`. Brief: `..\brief-e5.md`.
Edit helper: `python patch.py pNN.py` in this folder (functions patch(full, [(old,new)]), write(full, content);
ENGINE/ROOT/TESTS path constants). Bash heredocs containing apostrophes/backslashes break here: write scripts with
the Write tool. The Write tool can turn `\uXXXX` typed in content into the real character: run `python scan.py` after
writing tests with escapes. Measuring: `python measure.py <label> <out> <si args>`; Excel: `excel-open.ps1 -Files ..`;
PDF pages: `python printpages.py [--png] <report.html>` (Edge headless + PyMuPDF in `pylib/`); `xlsxscan.py`.
Crawl copies (analysed by this engine): `crawls/garden.sicrawl`, `crawls/synthetic-100k.sicrawl`,
`crawls/garden-hostile.sicrawl`. Outputs in `out/`.

## STATE: phase 2 under way. Merge of ui/step-3 (337f220) COMMITTED at c33cd68 (not pushed)

Done in the merge commit c33cd68 (built Release 0/0; tests Release --no-build: Engine 1259, Sidecar 219, Cli 23 pass;
app typecheck, lint, format:check, test 880 pass):
- Real score/words behind Reporting/ReportSources (StandInScore + my duplicate score types deleted).
- FixFirst = results.FixFirst; ReportedOnce -> "Checked once, for the whole site" (report meta, first list, workbook
  "Of what"; score table: site-wide "The whole site", reported-once-not-site-wide "Checked once, for the whole site
  (counts as a few)"); LooksFor in "What couldn't be checked" and workbook rows for passed/skipped; limits filled from
  results.Analysis.Settings; NotScored / NoPages texts; UrlLimit / DepthLimit / Stopped / Failed covers wording.
- Tests: ReportDocumentTests (states by real analysis: EndedEarlyAsync, CrawlAsync, StoreTheScoreOfItsCounts),
  WorkbookExportTests adapted; `{limit:` test (report, workbook, CSV).
- Findings on ui/step-3@337f220 itself: Sidecar.Tests didn't compile (ScoreTests.cs:109 AddRefusedByRobots removed by
  dabf0d2) -> helper restored in my merge; app format:check failed on src/api/grids.ts:420 (288e816) -> formatted.

STILL TO DO (in order):
1. Re-analyse scratch crawl copies (analysis VERSION 7): crawls/garden.sicrawl, synthetic-100k, garden-hostile.
2. Re-measure the report: garden (design 11 A4 pages, ~84 KB) and 100k: size, pages (printpages.py), time, memory.
   Print garden PDF and LOOK at pages against app/design/simple/report.html. Workbook on both; open in Excel again.
3. Docs: API doc "Report (E5)" (score/words source, ReportedOnce, states) + "Engine changes E5 made" numbers; README.
4. dotnet test engine/ShoutingIguana.Engine.slnx (Debug default) full; si bench --pages 100000 ONCE;
   si readbench --gate (rerun a failing class alone before believing it).
5. Final report per brief "Report back".

## Contract settled (see docs/v2-sidecar-api.md "Export (E5)", "Report (E5)")

- `POST /api/crawls/{id}/export` -> 201 export job. `GET /api/exports/{exportId}`. `POST /api/exports/{exportId}/cancel` -> 202.
- `GET /api/crawls/{id}/report?cut=full|summary&examples=5&lookExamples=3` -> text/html.
- SSE `export` event = the whole job; `hello.exports`.
- `/api/url-columns`, `/api/link-columns`: + `title`, `unit`, `valueTitles`.
- Problems: invalid-export 422, export-not-found 404, export-busy 409, export-not-writable 400, export-file-in-use 409,
  export-disk-full 507, export-crawl-changed 409, export-too-large 422, check-skipped 409.
- CLI: `si export <db> --kind K --out F [...]`, `si report <db> --out F [--cut summary] [--examples N] [--look-examples N]`.

## Measured (100k copy: 112,577 URLs, 5,053,909 links; garden: 6,286 URLs, 21,914 links)

See docs "Engine changes E5 made" table. Headlines: links CSV 28.6-32.8 s / 76 MB peak / 402 MB; links xlsx
30.3-37.1 s / 92 MB / 159 MB / 5 sheets; URLs 0.7-1.7 s; workbook 1.1 s (76 sheets); report 0.5 s, 203 KB.

## Findings in the engine (for the report)

- A grid read without a ReadCache recomputes target order per page: the CLI path needs its own cache (fixed for export).
- Text filter on links: one pass maps the file (243 MB working set, 48 MB private).
- Design's report prints "the whole site: the whole site" for a site-wide line; fixed in the engine's document.
- App `call()` drops 202 bodies -> export start is 201.
- A cursor is refused when the crawl's mode changes (expected); export checks the stamp before decoding.
