# Brief: fresh-eyes review of engine E5 (export, the workbook, the report), read-only

Review engine E5 of Shouting Iguana v2 (a desktop SEO crawler: a .NET 10 engine in `engine/` with a sidecar API, and
a React app in `app/`). It is branch `ui/e5` at the head named in the message that launched you. Review
`git diff $(git merge-base ui/step-3 ui/e5)..ui/e5` and the code it touches. You did not write it and nobody has
reviewed it.

- S3 = `C:\Users\LeeMessenger\AppData\Local\Temp\claude\e2e5beaddde4c070\8e7045a4-9ede-460a-be29-971bc406a6c0\scratchpad`
  (yours: `S3\e5-review\`, with a `tmp` folder for `TEMP` and `TMP`, and a `checkpoint.md` you keep as you go).
- The builders' scratch folder, read-only (their measurements, crawl copies, scripts):
  `C:\Users\LeeMessenger\AppData\Local\Temp\claude\e2e5beaddde4c070\f209576d-c3b5-4cef-9d6c-2666276ff82b\scratchpad\e5\`.

Work in a worktree of your own: from `C:\Projects\ShoutingIguana`,
`git worktree add --detach C:\Projects\ShoutingIguana\.claude\worktrees\e5-review ui/e5`. **Edit, commit or push
nothing in any other worktree.** In your own you may build, test and run anything. Copy crawls before you open
them; never edit `%LOCALAPPDATA%\ShoutingIguana\dev-crawls` (a real site of 4,521 URLs, "garden", and a 100k
synthetic crawl with 5 million links). The engine's synthetic test site (`si testsite`) is the only site you may
crawl. Other agents are at work on this machine: one heavy thing at a time; a timing that fails is run again alone
before you believe it. **Stop only processes you started, by their own process id**, never by a command-line
match. Never run `dotnet build-server shutdown`.

## Read first

- `CLAUDE.md` (repo root): the engine rules (one SQLite file per crawl; only `Storage/` writes it; an export only
  reads) and the conventions.
- `docs/handoff/2026-11-11/brief-e5.md` (what was asked), and the builders' `checkpoint.md` in their scratch folder
  (what they say they did, measured and left out).
- `docs/v2-sidecar-api.md`: "Export (E5)", "Report (E5)", "Engine changes E5 made", "Live events", Conventions.
- `docs/v2-ui-simple.md`: "The report", "The score", "Plain words". The design: `app/design/simple/report.html`.

## What to look for

Real defects, each with a concrete failure scenario, in order of weight:

1. **An export is exactly what the grid shows.** For every kind (URLs, links, a sitemap's URLs, sitemaps, robots,
   a check's pages, issue groups, whatever the doc lists): the same rows in the same order under the same filter,
   sort and search as the grid's own read, with the grid's column titles and value words; the first and last row;
   more rows than one sheet holds; an empty result; a filter with quotes, commas, percent signs and non-ASCII.
   Prove it on real crawls with a script of your own, not the branch's tests.
2. **Hostile content.** CSV and Excel formula injection (`=`, `+`, `-`, `@`, tab, carriage return at a cell's
   start, also after leading spaces), cells over Excel's 32,767 characters, control characters and unpaired
   surrogates in XML, newlines and quotes in CSV, a BOM and encoding Excel opens correctly, a sheet name from site
   content, URLs that aren't valid hyperlinks. In the report: every value from the site escaped in HTML and in
   attributes, an address with `javascript:` or `data:` never made a link, nothing that loads from the network,
   no script. A title or description made of markup.
3. **The job.** Start, progress, cancel, failure: no half file left, a file that exists (overwrite rules), a path
   that can't be written, a network share (refused, as the doc says), a disk that fills, two exports at once, an
   export while the crawl is running and while it is being analysed (never a write path; what it does to the log
   and its fold; the read slots), the sidecar stopping mid-export, the event's shape against the doc.
4. **Memory and time.** 100,000 URLs and 5 million links exported without holding the rows: measure peak memory
   and time yourself, CSV and Excel. What a bigger export than Excel's limits does and says.
5. **The report's truth.** Every figure in the report against the API's own answers and SQL of your own, on the
   garden, synthetic and 100k crawls: the score and its word, the counts, "Fix these first" in the engine's
   `fixFirst` order, shares and their denominators (a page check over the pages analysed, a URL check over the URLs
   crawled, a check reported once), examples (which, how many, stable between runs), limits filled from the
   analysis's stored settings, every state (running, stopped, failed, limited by URL count or depth, checks
   skipped, no score for each reason, never analysed, analysed by an older engine). The two cuts. Printing to A4
   and Letter: page breaks, nothing cut off, the page count the doc claims.
6. **The workbook**: each sheet against the same sources; sheet order and names; opens in Excel without a repair
   prompt (the builders have `excel-open.ps1`); column types (numbers as numbers, dates as dates).
7. **The CLI**: `si export`, `si report`, exit codes, errors' words, `si issues --csv` unchanged.
8. **Code and tests**: the engine rules kept (no write path outside `Storage/`; HTML never re-parsed);
   `.ConfigureAwait(false)` on every await in the engine; models one per file in `Models/`; tests that actually
   test (each claim in the docs has a test that would fail); anything weakened elsewhere to get green; the
   TypeScript types in `app/src/api/` matching what the sidecar really sends.
9. **The docs** say what the code does.

Verify each finding before reporting it: read the code path and, where you can, prove it (a failing test or a
script). Mark each CONFIRMED (shown) or PLAUSIBLE (reasoned). No style preferences, and no speculation reported as
a defect.

## Report back

A ranked list, most severe first, at most 30: severity (high / medium / low), file:line, one-sentence defect, the
failure scenario, CONFIRMED or PLAUSIBLE, and a one-line fix. Then a short "looks right" note on what you checked
and found sound, and your own measurements. Write your proof tests and scripts to `S3\e5-review\` and name them.
**Give the full list in your final message** (the orchestrator saves it for the fixer; you can't write a file
named `findings.md`). No file dumps. When done, stop your processes and remove your worktree
(`git worktree remove --force` on your own folder only).
