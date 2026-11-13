# e5-review checkpoint
- worktree: C:\Projects\ShoutingIguana\.claude\worktrees\e5-review (detached 9b9409d), Release built 0/0.
- crawls copied to e5-review\crawls (garden, garden-hostile, synthetic-100k), from builders' analysed copies.
- Code read: Export/*, Xlsx/*, Tables/*, Reporting/*, Sidecar Exports/*, Api/Export*, CLI ExportCommand.
## candidate findings (to verify)
- "explained from page 2" false when page 1 overflows (100k: score table 3 pages)
- OneFix says "pages" for URL-subject checks
- LooksLikeFormula: first char only (leading spaces, \n)
- live crawl + issueRows: row.Id > MaxUrlId bound (ids are issue ids?)
- hard-coded score words (60 / 10 / 4 / thresholds) vs ScoreCalculator
- IfFixed.CheckIds vs first five
- CLI silently ignores fields of other kinds
## done
- proof_grid.py (sc.py, sidecar_run.py): garden + 100k, every kind csv+xlsx vs grid's offset pages: 0 fails (out/proof_grid_*.log).
  Item 1 sound on ordinary data.
- make_hostile.py -> crawls/mine-hostile.sicrawl; proof_hostile.py; excel-cells.ps1 / excel-rows.ps1 / excel-formulas.ps1.
  Report HTML sound (no tags/attrs from site, links https only). xlsx parts well-formed, opens in Excel, 0 formulas.
  Leading space/LF/NBSP before '=' in CSV: Excel 16 keeps as text (not a defect).
- **F1 CONFIRMED**: CSV field > ~32,759 chars: Excel splits it; remainder starts a new row in col A and "=1+1" there
  is evaluated (out/h-overflow2.csv -> A32 formula =1+1 value 2; 83 used rows for 64 data rows). Titles capped at
  1,000 by parser, but URLs aren't capped (to prove with C# test). CsvTableWriter.cs:117.
## next: proof_job.py (events, cancel, overwrite, paths, concurrency, running crawl, analysis), memory/time, report truth, workbook, CLI, tests/docs/TS
- proof_job.py: events/cancel/overwrite/paths/concurrency all as doc. Low: path with colon (ADS `d.txt:x.csv`) starts a job,
  fails "The parameter is incorrect", leaves empty `.d.txt` (ExportTarget.Open temp name).
- proof_live.py (testsite 30k): live export sound; LOW: rowsWritten 30770 > rowsExpected 30757 (cached count) on sort by url;
  pause under export does NOT end it (doc says it does); never-analysed -> not-analyzed; continue under export -> crawl-changed.
- proof_analyze_under.py: analysis under 100k links export: analysis 15.8s, export ends export-crawl-changed, no temp left. OK.
- measure.py: 100k urls csv 0.6s/70MB, xlsx 0.8s/87MB; links csv 31.0s/74MB (402MB), links xlsx 36.4s/91MB (159MB, 5 sheets, 5,053,909 rows, counts verified).
- proof_report.py: garden, 100k, stopped testsite: all figures match /analysis + SQL. Findings:
  - F2 CONFIRMED "explained from page 2" false: 100k What to fix starts p4 (A4+Letter); garden Letter p3. ReportDocument.cs:348,376
  - F3 CONFIRMED About "Not read: 2 addresses the site's rules file (robots.txt) keeps search engines out of": garden's own robots blocks 0; the 2 are external sites' robots (stats.Outcomes[RobotsDisallowed] counts all). ReportReader.cs:32, ReportDocument.cs:794 (+ NoPages sentence :261)
  - F4 CONFIRMED stopped->analysed->continued->stopped: "Pages that can appear in search: 4,064 of 3,113" (stats now vs analysis then); also queued-now in note.
- proof_states.py (url-limit, depth-limit, start-404, start-robots(noPages), whole): states as doc. LOW grammar "1 address, which weren't read".
- proof_workbook.py garden+100k: summary + every sheet == analysis + SQL; Excel opens all (garden, hostile, 100k 76 sheets, urls.xlsx/csv 112k) no repair, 0 formulas.
- CLI run (out/cli): exit 2 for refusals, 1 for parse; LOW `--filter title:eq:404` -> "eq needs text" (value typed by looks); CLI ignores fields of other kinds.
- ReviewProofTests.cs (copied to S3): link of 32,766+ chars kept whole by parser+normaliser, CSV one unguarded field. PASSES (=defect shown).
- Delegated test audit (agent aab135c7af828dff1) done: gaps list in final; doc says sheet "3 Large images that slow pag…" but real is "…pages…" (TWT:245).
- pause under export doesn't end it; docs (api:1777, README) + GridExport.cs:120 say it does; no test.
## next: shutdown mid-export + hard kill leftovers; print overflow check (long unbroken detail); final report; remove worktree
- overflow.mjs: h-report examples table 2,589px on a 680px page (unbroken 300-char detail): cut off in print. garden fine. Page counts garden 12 A4 / 13 Letter; state-url-limit: What to fix on p3 on A4.
- graceful shutdown mid-export: gone in 0.2s, no leftovers, old file intact. Hard kill: `.kill-links.xlsx.<guid>.tmp` (8.6MB) left; nothing sweeps.
- CSP hash matches; Unicode paths fine; report query refusals fine.
- DONE: sidecar stopped (stop.flag), big link exports deleted, worktree removed. Final report sent.
