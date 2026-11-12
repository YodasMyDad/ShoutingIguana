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
