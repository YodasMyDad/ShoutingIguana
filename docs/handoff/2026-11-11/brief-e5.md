# Brief: export, the workbook and the report (engine E5, round 3)

Shouting Iguana v2 is a desktop SEO crawler: a C#/.NET 10 engine (one SQLite file per crawl, a catalog of checks,
an analysis that writes issues), a sidecar HTTP API, a CLI (`si`), and a React app that reads the API. Nothing can
be exported from the app yet, and the owner wants "a report for everything at the end". You build the engine's
export: what any grid shows as CSV or Excel, the workbook of everything, and the report document. UI agents will
build the buttons and the Report screen on your endpoints afterwards.

## Where you work

- Worktree `C:\Projects\ShoutingIguana\.claude\worktrees\e5`, branch `ui/e5` (the step-3 branch with the reviewed
  engine build E3c merged in). Work ONLY there. Small commits (`Engine: ...`, `Sidecar: ...`, `Cli: ...`,
  `Docs: ...`, `Tests: ...`, saying what now holds), each ending with
  `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`. Never push, merge or touch another worktree (one
  exception below, when told). No bare `git stash`. **Never rename an existing public function.**
- Your scratch folder:
  `C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\f209576d-c3b5-4cef-9d6c-2666276ff82b\scratchpad\e5\`.
  Keep a `checkpoint.md` there, updated at each phase (done, decided and why, verified vs not, next).
- Other agents are building, testing and benchmarking on this machine, and memory is short. One heavy thing at a
  time; repeat a timing before you trust it. Stop only processes you started, by their own process id. Never run
  `dotnet build-server shutdown`. Never crawl a real site (`si testsite` serves the synthetic one). Never edit
  `%LOCALAPPDATA%\ShoutingIguana\dev-crawls`: copy a crawl before opening it. The dev crawls are a real site
  ("garden", 4,521 URLs) and a 100k synthetic crawl with 5 million links; analyse your copies first.

## Read first

- `CLAUDE.md` (repo root): the engine rules (only `Storage/` writes a crawl file; an export only reads it) and the
  conventions (models in `Models/` folders, one type per file, primary constructors, `ConfigureAwait(false)`).
  `engine/README.md`.
- `docs/v2-ui-simple.md`: "The report" in full (outline, sizes, the two cuts, the workbook, the Report screen that
  will show it, the report at the end), "The score", "Plain words" (the fields the report prints), "What the
  engine needs" (your rows: the report, the workbook).
- The report's design, on real data: `app/design/simple/report.html` (open it; it is the look and the words to
  reproduce), `app/design/simple/README.md`, and how it was generated: `app/design/simple/build/`.
- `docs/v2-ui-ia.md`: flow 5 "Hand over" (what export offers and where), the Issues screen's export.
- `docs/v2-sidecar-api.md`: "Conventions" (errors as problem types, paging, tokens, how long reads behave),
  "URLs", "Links", "Sitemaps", "robots.txt", "Issue groups" (the reads an export repeats), "Live events" (how
  progress is told), and the latest "Engine changes" sections, for how a contract change is written up.
- What exists: `engine/src/ShoutingIguana.Cli/Output/CsvWriter.cs` and `si issues --csv`.

## What to build

1. **Export of what a grid shows**, as CSV and as Excel (`.xlsx`): the URL grid, the links grid, one sitemap's
   URLs, an origin's blocked URLs, a check's rows (all of them, or one group's). "These filters, these columns":
   the request is the grid's own read (filters, search, sort) plus the columns to write and the format, so what is
   exported is exactly what the grid would show, in its order, with the grid's own column titles and value words
   (see how the app words enum values, and serve from the engine whatever titles the export needs so the two can't
   differ).
   - The app chooses the file with a native save dialog and gives you the path: the sidecar writes the file. It is
     a job: start, progress (rows written of rows expected), cancel, done or failed, in the way the contract's
     conventions and the live stream already tell long work. A cancelled or failed export leaves no half file
     (write beside the target, then move). A path that can't be written, a full disk and a file open in Excel each
     get their own plain problem.
   - **Memory stays flat**: 100,000 URLs and 5 million links are exported without holding the rows. Measure peak
     memory and time for both formats on the 100k crawl. Excel allows 1,048,576 rows a sheet: decide what a
     bigger export does (carry on to further sheets up to a stated cap, or stop and say so) and say it in the job's
     result, never a silently short file.
   - **Site-controlled text is hostile.** A cell that starts with `=`, `+`, `-`, `@`, a tab or a carriage return
     is written so no spreadsheet runs it as a formula (and stays a text cell in `.xlsx`), without corrupting
     honest values such as a negative number in a numeric column. CSV is RFC 4180, UTF-8 with a byte-order mark so
     Excel reads it, CRLF rows, quotes doubled. Test with titles and URLs holding quotes, commas, newlines,
     formulas, control characters and emoji.
   - Numbers are numbers and dates are dates in `.xlsx`; the header row is frozen and bold; columns have sensible
     widths without measuring every cell.
   - How you write `.xlsx` is your call, within these: streaming; opens in Excel, LibreOffice and Numbers with no
     repair prompt (prove it: a validator in the tests, and open the files in whatever spreadsheet program this
     machine has, saying which); a new package only if it earns its place, pinned in
     `engine/Directory.Packages.props` to a version published before 2026-10-21, with its licence and publish date
     in your report.
2. **The workbook** ("Spreadsheet of everything"): a summary sheet (the score, the counts, every check with its
   severity, plain and technical titles, count and share), then a sheet for each check that found something, with
   every URL, its title, its detail and the other URL involved, named and numbered as the design says. Kinds
   `workbook` (every check) and `check` (one check's sheet as its own file). The same job as any export.
3. **The report**: one self-contained HTML file, to the design: inline CSS, inline SVG, no script, no web font,
   light, printing well to A4 and Letter. `GET /api/crawls/{id}/report?cut=full|summary&examples=5&lookExamples=3`
   returns it, and the same export job saves it to a path. `si report <db> --out <file> [--cut summary]`.
   - Every string from the site or the crawl is HTML-escaped, in text and in attributes; URLs are never made into
     links with a scheme other than http or https; the document carries a Content-Security-Policy meta that allows
     no script and no network. Test with hostile titles and URLs.
   - Its size and length follow the checks that fired, not the site's size: measure it on the garden crawl
     (the design gives 11 pages and about 84 KB) and on the 100k crawl.
   - Every state the design names: a crawl stopped early, checks skipped, a perfect site, no score, never
     analysed (`not-analyzed`, no document).
   - **It needs the score and the plain texts, which another agent is building now** in the sibling worktree
     `C:\Projects\ShoutingIguana\.claude\worktrees\score` (branch `ui/score`): the score on the analysis (stored
     by the analysis), `siteWide`, the engine's "Fix first" order, and plain texts per check (present for ten
     checks now, null for the rest until a later round writes them). Build export and the workbook first. Build
     the report's document against a small interface of your own for "the score" and "a check's words", falling
     back to the technical texts where a plain text is null (the design's technical title sits beside the plain
     one anyway). **When the orchestrator messages you that `ui/score` is ready, merge that branch into yours**
     (`git merge ui/score`), put the real score and words behind your interface, and finish. You may read that
     worktree at any time to see where its names are heading; don't edit it.
4. **`si export`** on the command line for the same kinds, sharing the code (`si issues --csv` keeps working).
5. **Docs**: an "Export (E5)" section and a "Report" section in the API contract, a new "Engine changes" section
   for this work, the TypeScript types in `app/src/api/types.ts` (additive) with a typed client function for each
   new call in `app/src/api/` beside the existing ones (no screen code), and `engine/README.md` for the commands.

## Verify

All green at the end and seen green by you: `dotnet build engine/ShoutingIguana.Engine.slnx -c Release` (warnings
are errors), `dotnet test engine/ShoutingIguana.Engine.slnx`, `si bench --pages 100000` once at the end (every
planted issue found, nothing else flagged, memory flat), `si readbench --gate`; if you touched `app/src/api/`:
from `app/`, `npm ci`, `typecheck`, `lint`, `format:check`, `test`. Each export kind has a test that reads the file
back and compares it with the grid's own read, row for row, on a crawl of the test site, and a test that an export
running while a crawl runs answers for one moment (see how the links grid does that) and doesn't hold the crawl's
write-ahead log open for its whole length (another agent is fixing exactly that for grid reads on branch `ui/e3c`:
read its approach in `C:\Projects\ShoutingIguana\.claude\worktrees\e3c` when you get there, and say in your report
what an export of a running crawl does to the log). Print the report to PDF with a headless browser if one is on
the machine (the app's Playwright is in `app/node_modules` after `npm ci`) and look at the pages yourself.

## Report back

Short: the commits; the contract you settled (every endpoint, field and command, exactly: UI agents build on it);
the xlsx approach and why, with any package; measured memory, time and file size for each kind on the 100k crawl
and the garden crawl; the report's size and page count on both; what a hostile cell and a hostile title become;
what you saw when the files were opened; anything you found in the engine; what is not verified. No file dumps.
