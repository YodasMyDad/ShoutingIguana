# Brief: hand over, search and the analysis's limits in the UI (UI, round 3, the last slice)

Read `docs/handoff/2026-11-11/ui-slice-rules.md` in your worktree first: it holds the rules every UI slice follows
and the defects earlier reviews kept finding. Where it names a model in the commit line, use
`Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. This brief adds your slice.

The engine can now export every grid as CSV or Excel, make the workbook of everything and the report (E5), search a
crawl fast, serve a site's favicon and save the checks' limits (E6). None of it is in the app. You build it: flow 5
of the IA, "Hand over", the palette's search, favicons, and Settings > Analysis. The Report SCREEN is not yours
(round 4 builds it): you offer the report as a file.

## Where you work

- Worktree `C:\Projects\ShoutingIguana\.claude\worktrees\handover-ui`, branch `ui/handover` (cut from `ui/step-3`
  with E5, E6, the URL grid and round 3's shell, mode switch and Settings merged: the head is named in the message
  that launched you). Ports: `SI_DEV_SERVER_PORT=64874 SI_DEV_SIDECAR_PORT=64875 SI_PREVIEW_PORT=64876
  SI_E2E_SIDECAR_PORT=64877`, `SI_STORYBOOK_PORT=7206`.
- Your scratch folder (all your own files, `tmp`, `checkpoint.md`):
  `C:\Users\LeeMessenger\AppData\Local\Temp\claude\e2e5beaddde4c070\8e7045a4-9ede-460a-be29-971bc406a6c0\scratchpad\handover-ui\`.
- Other UI agents work in parallel on round 4 (the Simple screens: Home, checking a site, the Summary, What to fix,
  the Report screen). Stay out of Simple's screens. Shared files (`keyboard/registry.ts`, `api/queries.ts`, the
  mode table): add, don't reorder or reformat.

## Read as well

- `docs/v2-ui-ia.md`: "The five flows" 5 "Hand over", the Keyboard tables (Ctrl+E, Ctrl+K), each grid screen's
  Export, "Room for what comes later".
- `docs/v2-ui-simple.md`: "The report" (its outline, sizes, the two cuts, "The workbook", "The Report screen" for
  the default file names and the save toast), "What round 3 builds" items 6 and 7, "Settings in each mode".
- `docs/v2-sidecar-api.md`: "Export (E5)", "Report (E5)", "Engine changes E5 made", "Engine changes from the E5
  review" (the job, its event, its problems and their words, the journal of part files); "Search", favicon,
  "Analysis settings", "Engine changes E6 made" and its review's section.
- `docs/v2-ui-architecture.md`: the grid and `GridScreen`, the row menu and the slot the URL grid left for Export,
  "Feedback" (toasts and progress), the palette, how Settings sections are added (round 3's frame), secrets.
- `app/src/api/`: the client functions and types E5 and E6 added (`startExport`, `exportJob`, `cancelExport`,
  `report`, `parseExportJob`, `isExportOver`, the `export` event, search, link ends, favicon, analysis settings),
  and the mock transport's routes.

## What to build

1. **Export of every grid** (Ctrl+E and an Export button in the slot each grid's header has): URLs, Links, one
   sitemap's URLs, Sitemaps, robots.txt origins, an origin's blocked URLs, a check's pages on Issues. "CSV" or
   "Excel" of exactly what the grid shows: its filters, search, sort and columns. The native save dialog (the
   shell has `tauri-plugin-dialog`), a default file name from the site, the date and the view, then a progress
   toast fed by the `export` event (rows written of rows expected, a bar that never goes backwards, Cancel), and at
   the end "Open folder" and "Open file" (the shell has `tauri-plugin-opener`). Every problem the engine can answer
   (`export-busy`, `export-not-writable`, `export-file-in-use`, `export-disk-full`, `export-crawl-changed`,
   `export-too-large`, a network share refused, …) in a sentence of the app's, with the action that helps. An
   export of a running crawl says it is of the crawl as it was when it started. Cut cells (`truncatedCells`) and
   columns that couldn't be filled (`unavailableColumns`) are said in the finished toast.
2. **On Issues**, Export offers "This check" (the check's sheet as its own file) and "Every check" (the workbook).
3. **On Overview and Issues**, Export also offers **"The report…"** (full or summary page, HTML) and
   **"Spreadsheet of everything…"** (the workbook), as the Simple design's round 3 items 6 and 7 say, through the
   same dialog and toast. The palette lists them. A crawl never analysed offers "Work out the results" instead.
4. **Exports outlive the screen.** Leaving the screen, closing the window to the tray, or starting another export
   keeps every running export's toast reachable (a small list of exports in progress where the design's feedback
   rules put it). On start the app takes up `hello.exports` so an export running before a reload keeps its toast.
   A finished export while the window is hidden gives a system notification.
5. **The palette's search** uses `POST /search`: it sends the empty warm-up as the palette opens, then a search per
   keystroke (no debounce past what the 50 ms budget needs), shows the URL's title and address with the matched part
   marked, its status in words, and Enter opens the inspector on it. Results for one crawl only; the current crawl's.
6. **Favicons** where the design shows a site: Home's library rows, the top bar's crawl name, the crawl's tab or
   title if the design has one. `hasFavicon` false, a 204, or an image that fails to decode shows the letter
   fallback the design gives, with no broken image and no layout jump. Never more than one request per crawl per
   session.
7. **Settings > Analysis** (Advanced only), as one section file in round 3's Settings frame: every limit from
   `GET /api/analysis-settings` with its description, unit and range, inline validation from the API's own
   answer (errors keyed by limit id, `limits[n]` and the whole set), Reset one and Reset all with Undo, and what a
   change does: the crawls analysed with other limits read as out of date, and the app says so where it shows
   that state. A change applies at once and is remembered.
8. **Links' "Show these pages in URLs"** and the URL grid's link-ends filter (`linkEnds`), if the URL grid slice
   didn't build them: check its report in `docs/v2-ui-architecture.md` first, and say what you found.
9. **Every state** in Storybook (both themes, forced colours, 200% zoom, reduced motion): the export dialog's
   choices, each toast state (starting, counting, writing, done, cancelled, failed with each problem), exports in
   progress, the palette's search (warming up, results, none, an error), favicons (loaded, fallback), Settings >
   Analysis (loaded, a refused value, saving, reset). e2e for: a CSV and an Excel export of a filtered, sorted URL
   view read back and compared row for row with the grid; cancel; a refused path; the report and the workbook
   saved; search to the inspector; a limit changed, the crawl reading as out of date, reset.

## Measure

Ctrl+E to the dialog, and a palette keystroke to its results, inside 100 ms on the 100k crawl. Export of the 100k
URL grid as Excel, end to end, with the time the toast shows against the engine's own.

## Report back

As `ui-slice-rules.md` asks, plus: every grid's export with its default file name; each engine problem and the
sentence the app gives it; what the shell couldn't do (dialogs, opening a folder) and how you proved what it can;
what you left out and why.
