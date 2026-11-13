# E5 review (`ui/e5` @ 9b9409d): findings

Saved by the orchestrator from the reviewer's final message. Line numbers are at 9b9409d. The reviewer's proofs are
in this folder: `sc.py`, `sidecar_run.py`, `measure.py`, `proof_grid.py`, `make_hostile.py`, `proof_hostile.py`,
`proof_job.py`, `proof_live.py`, `proof_analyze_under.py`, `proof_report.py`, `proof_states.py`,
`proof_workbook.py`, `overflow.mjs`, `excel-cells.ps1`, `excel-rows.ps1`, `excel-formulas.ps1`,
`ReviewProofTests.cs` (copy into `engine\tests\ShoutingIguana.Engine.Tests\Export\` to run), outputs in `out\`,
crawl copies in `crawls\`.

## Findings, most severe first

1. **HIGH · `Export/Tables/CsvTableWriter.cs:117` (`WriteText`) · CONFIRMED.** A CSV field over about 32,760
   characters is written whole; Excel splits it, and the remainder starts a new row in column A, unguarded.
   - Scenario: 24 titles of `L`xN + `=1+1` around the limit, exported as CSV. Excel 16 showed 83 rows for 64, and
     `A32` as formula `=1+1`, value 2 (`out\h-overflow2.csv`, `excel-formulas.ps1`). This breaks the doc's "Text
     from the site can't run".
   - Reach: titles and descriptions are capped at 1,000 and 2,000, but URLs are not. `ReviewProofTests.cs` passes:
     a link of 32,766+ characters survives the parser and normaliser and is written as one field.
   - Limit: URL normalisation percent-encodes quotes, so only quote-less formulas survive in a URL; the row break
     happens regardless.
   - Fix: cut CSV text fields to 32,767 characters as the workbook does, counting them in `truncatedCells`.

2. **MEDIUM · `Reporting/ReportDocument.cs:348, :376` · CONFIRMED.** "explained from page 2" is false whenever
   page 1 overflows. The 100k report starts "What to fix" on page 4 (A4 and Letter). A URL-limit crawl with only 5
   problems starts it on page 3 on A4. Garden on Letter is page 3.
   - Fix: drop the page number ("explained under the same number in What to fix").

3. **MEDIUM · `Reporting/ReportReader.cs:32`, used at `ReportDocument.cs:260-261, :792-795` · CONFIRMED.**
   `BlockedByRobots` counts every robots-disallowed URL, other sites' included, but the text blames "the site's
   rules file". Garden's About says "2 addresses the site's rules file (robots.txt) keeps search engines out of".
   Its own robots.txt blocks 0 (`/robots`, and the score's `covers.blockedByRobots` is 0); the 2 are on two
   external sites.
   - Fix: use the internal-only count (`score.Covers.BlockedByRobots`).

4. **MEDIUM · `Reporting/ReportReader.cs:77-78` · CONFIRMED.** `IndexablePages` and `Queued` are the crawl as it is
   now; `Pages` is the analysis's. Stopped, analysed, continued, stopped again: the report says "Pages that can
   appear in search: 4,064 of 3,113", under the out-of-date note.
   - Fix: when `OutOfDate` has `CrawlChanged`, leave out the figures read from the crawl as it stands.

5. **MEDIUM · `ReportDocument.cs:108-115` (CSS `.examples td`, also `.found`, `.onefix`) · CONFIRMED with planted
   data.** Site text has no `overflow-wrap`, so an unbroken run in "What was found" widens the table past the page.
   A 300-character unbroken detail made the examples table 2,589 px on a 680 px page on A4 and Letter
   (`overflow.mjs`), so print cuts it off.
   - Fix: `overflow-wrap: anywhere` on those cells. (The CSP hash of the sheet changes with it.)

6. **LOW · `Export/GridExport.cs:153-156` · CONFIRMED.** `rowsExpected` comes from a cached count, so on a running
   crawl it can be lower than `rowsWritten` (30,770 of 30,757, sorted by URL). The doc says the opposite. Fix:
   expect the count of ids up to the bound, or clamp and correct the doc.

7. **LOW · `Export/GridExport.cs:120-121`, API doc :1776-1778, `engine/README.md` · CONFIRMED.** A pause does not
   end an export (a links export finished `done` under a pause), though the message and both docs say "paused,
   resumed". Fix: drop those words; nothing is wrong with the file.

8. **LOW · `Export/ExportTarget.cs:63-99, :117` · CONFIRMED.** A file name with a colon (`d.txt:x.csv`) passes the
   extension check. The job starts, fails with "This file couldn't be saved: The parameter is incorrect.", and
   leaves an empty `.d.txt`. Fix: refuse `:` and other invalid file-name characters in `Check`.

9. **LOW · `Cli/Commands/ExportCommand.cs:268-272` · CONFIRMED.** A filter value is typed by what it looks like, so
   `--filter title:eq:404` fails with "eq needs text". Fix: type it by the column's type.

10. **LOW · `Cli/Commands/ExportCommand.cs:74-85` · CONFIRMED.** Options of another kind are ignored
    (`--kind urls --check x --cut summary` exits 0); the API refuses them. Fix: share the sidecar's "belongs to
    another kind" rule.

11. **LOW · `ReportDocument.cs:324` (and :261) · CONFIRMED.** "robots.txt keeps search engines out of 1 address,
    which weren't read." Fix: "wasn't" for one.

12. **LOW · `ReportDocument.cs:547-551` · PLAUSIBLE (read only).** The "One fix" line always says "pages" and "They
    all name the same address", but 12 grouped checks are URL checks (`redirect-chain`, `redirect-downgrade`, ...).
    Fix: take the noun from the check's subject.

13. **LOW · `Export/ExportTarget.cs:117` · CONFIRMED.** After a hard kill, `.kill-links.xlsx.<guid>.tmp` (8.6 MB)
    stays in the user's folder and nothing ever removes it. The doc admits the part file; the shell's job-object
    kill makes this the normal crash path.
    - ORCHESTRATOR'S DECISION (under the owner's delegation, the owner can overturn it): never sweep a user's
      folder by pattern. The sidecar keeps a small journal in its own data folder of the part files of exports in
      progress (written before the part file is created, removed when the export ends any way), and at its next
      start deletes each journalled part file that still exists, then clears the journal. A part file it can't
      delete is left and logged. The CLI (`si export`) needs none: it deletes its part file on Ctrl+C already, or
      say what it does.

14. **LOW · docs :1729, `WorkbookExport.cs:18` · CONFIRMED.** The example sheet name "3 Large images that slow
    pag..." is wrong; the code writes "3 Large images that slow pages...".

15. **LOW · tests · CONFIRMED by grep.** The `export-too-large` pre-check (`GridExport.cs:145, :208`) and its "no
    file" promise have no test; only the writer's cap is tested, lowered to one sheet (`TableWriterTests.cs:240`).

16. **LOW · tests · from a delegated read, one line spot-checked.**
    - `GridExportTests.cs:63-68`: no case combines filter, sort and search, and filtered cases assert only more
      than 0 rows.
    - `GridExportTests.cs:428, :439` and `RunningCrawlExportTests.cs:257` take expected headers and words from the
      product's own `GridExport.Title`/`Cell`.
    - Crawl-changed is a SQL timestamp bump (`WorkbookExportTests.cs:132`, `ExportFileTests.cs:177`); "finished"
      has no test.
    - No sidecar test for `export-crawl-changed`, `export-disk-full`, `export-too-large`, the 250 ms spacing or
      "last 64".
    - The shutdown test cancels an export that hasn't opened its file (`ExportEndpointsTests.cs:354-365`).
    - Report examples are tested by count, not order.

## Looks right (the reviewer's note, short)

Export equals the grid (0 mismatches on garden and 100k, CSV and Excel, with filters, sorts, search, empty
results). Hostile content: no repair, 0 formulas, nothing from site text in a tag or attribute. The job's events,
cancel, overwrite, read-only, held-open, UNC, busy and `hello.exports` as documented. A running crawl's export is
bounded to its start. Every report and workbook figure matched `/analysis`, `/stats` and SQL. Engine rules kept.
Links CSV of 5,053,909 rows: 31 s, 74 MB peak.
