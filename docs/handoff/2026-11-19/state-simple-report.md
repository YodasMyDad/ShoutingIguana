# State of the simple lane's report sub-lane, 2026-11-19 (stopped on the coordinator's word)

Named `state-simple-report.md`, not `state-fix.md`: the other lanes' files here are `state-<lane>.md`, and the simple
lane will write its own.

- **Branch** `ui/ux-simple-report`, worktree `C:/Projects/ShoutingIguana/.claude/worktrees/ux-simple-report`.
- **Base** `4a7fab91`. **Head** `84b2641a`. Eight commits, `git status` clean, no stash. Not pushed, not merged.
- `AnalysisFreshness.VERSION` is **20** on this branch (untouched: nothing here changes what an analysis stores).
- Scratch (flows, shots, before and after reports, scripts, checkpoint):
  `C:/Users/LeeMessenger/AppData/Local/Temp/claude/e2e5beaddde4c070/da833503-3c78-46eb-9e8a-d618294b5af4/scratchpad/simple-fix/report/`

## What the owner asked for, and what this sub-lane was told to build

The owner, on the installed app: "The UI looks nice in most cases but the UX really falls short." On Simple: "it shows
the broken link which people will click, but the people are going to be probably more interested as to where the
broken link is."

The brief (from the simple lane): fix the report and the spreadsheet's "where found" columns: audit findings **S-11**
(the report), **A-12** (the report, from the shell audit) and the spreadsheet half of **S-05**. Six items: (1) the
report's links work in the app, and a problem's heading leads into the app; (2) "Fix these first" rows are anchors and
the long document gets a contents list; (3) words: "2 on more" in English, Simple's report in Simple's words, the
technical title out of the client's way, a Simple and an Advanced form; (4) each problem says where to fix it, page
first, and shows up to about 25 rows; (5) the Report screen's buttons named for what they do, and the preview uses the
pane; (6) the check's sheet splits "Linked from" into "Page that links to it", "Where on the page", "Link text", one row
per link.

## Each piece

| # | Piece | State | Commit |
| --- | --- | --- | --- |
| 1 | Links work in the app; heading leads into the app | **done** | `30ca91f4` (app), `6b6176d1` (engine marks) |
| 2 | "Fix these first" anchors; contents list | **done** | `6b6176d1` |
| 3a | "2 problems fixed, 2 got worse." | **done** | `6b6176d1` |
| 3b | Plain and technical wordings (`words=plain|technical`) | **done** | `6b6176d1`, `99b350b3`, `8435dc45` |
| 3c | Plain rows say what Simple's screens say | **partly done** | `6b6176d1` |
| 3d | Technical title moved to "the words in this report" | **done** (plain wording; a table under About this check) | `6b6176d1` |
| 4 | Where to fix it, page first; 25 rows | **done** | `6b6176d1`, `fd246971` |
| 5 | Buttons' names; preview fills the pane | **done** | `30ca91f4` |
| 6 | Sheet: the page that links to it in its own cells | **partly done** | `b9f5d757` |
| – | e2e specs' button names and saved-file check | written, **not run** | `8a43f1da` |
| – | Docs | **done** for the two sections allowed | `84b2641a` |
| – | Stories fixture `app/design/report/garden-*.html` | **not started** | |

**3c, partly done.** `engine/src/ShoutingIguana.Engine/Reporting/ReportPlainWords.cs` mirrors the app's pieces (how an
address answered, link position, evidence columns, image file and size, failure words, "and N other … on this page").
`ReportPlainDetails.cs` mirrors the detail sentences of **101 checks** (its `DETAIL_WORDS`), which is every check with
a detail on the garden and jitsunami crawls. **37 checks that write a detail on the synthetic crawl are not
mirrored**; their rows show the engine's own detail (the same fallback the app uses for a form it doesn't know):
canonical-invalid, canonical-invalid-attribute, canonical-loop, canonical-multiple, canonical-outside-head,
canonical-relative, charset-invalid, head-tags-in-body, hreflang-duplicate-code, hreflang-header-mismatch,
hreflang-inconsistent-code, hreflang-missing-self, hreflang-outside-head, html-structure-errors, image-inline-large,
image-missing-src, media-preview-blocked, meta-refresh, pagination-not-linked, pagination-sequence-error,
redirect-loop, sitemap-error, sitemap-lastmod-invalid, social-image-broken, social-image-relative,
social-image-too-large, social-image-too-small, social-tags-multiple, soft-404, structured-data-invalid,
structured-data-missing-recommended, structured-data-missing-required, structured-data-schema-errors,
unavailable-after-passed, uncrawlable-links, viewport-invalid, x-card-incomplete. To add one: port its entry from
`app/src/simple/plainDetails.ts` into `DETAIL_WORDS`, add cases to
`engine/tests/ShoutingIguana.Engine.Tests/Reporting/PlainRows/plain-rows.json` with `"said": null`, run
`SI_WRITE_PLAIN_ROWS=1 npx vitest run src/screens/report/plainRows.test.ts --project app` from `app/`, then the
engine's `PlainRowsTests`.

**6, partly done.** A row is still one address. The first page that links to it has its own cells ("Page that links
to it", "Where on the page", "Link text"), which lead the row on `internal-client-error`'s sheet; the other pages are
one cell, "Other pages that link to it". **One row per link is not built**: it changes every count the export gives
(rows written against rows expected, the workbook's sheets against its summary, "Wrote 8 rows") and what Advanced's
export of a check holds, and the reader names only the first ten pages of each address. Code: `WhereFoundColumns.Sources`,
`GridExport.WriteGrid` (its `extra` loop writes one line a row).

## Decisions, and why

- **The report had one form.** `ReportOptions.Wording` (Plain, the default, or Technical). Simple asks `plain`,
  Advanced `technical`, for the document shown and the file saved. Same outline, numbers and titles: only how a row
  says what was found differs, and where the technical name sits.
- **The app's link into a problem** is put where the engine leaves a comment (`<!--si:open {check}-->`), in the
  frame's copy only, so the saved file and paper have no dead control. Sections are `id="problem-{check}"`.
- **Only `internal-client-error` leads with the page to edit** (`WhereFoundColumns.FixedAtTheLink`): a missing page is
  mended at the link; a server error or a redirect chain is fixed at the address, which still leads.
- **Caps** 25 (problems to fix) and 10 (things worth a look), were 5 and 3; a page names five things (two for a thing
  worth a look), were three. The garden report is 237 KB (156 KB before).
- **Plain rows drop** "It's a page", how long checking took, link counts where the linking pages are listed, "It
  forwards to another page", and evidence that only repeats the detail (`ReportRows.SaidByTheDetail`).
- **Parity by one cases file** read by a test on each side, written by the app's test, so a sentence changed in
  `app/src/simple` fails `app/src/screens/report/plainRows.test.ts` first.
- **Zoom**, not wider paper: `body { zoom }` under `@media screen`, 1 to 1.5 in steps of 0.05, so print is unchanged.
- **The link words** ("The link that says “Buy Now”, in the main content", "It leads to …") are the report's own, from
  the audit's section 4, not mirrored from the app: the simple lane was building that card at the same time.

## Files changed that another lane owns

- engine lane: `engine/src/ShoutingIguana.Engine/Export/ExportFields.cs`, `Export/WhereFoundColumns.cs`,
  `Export/GridExport.cs`, `Export/Models/ExportExtraColumns.cs`; `engine/src/ShoutingIguana.Sidecar/Api/ExportRequests.cs`,
  `Api/Models/ExportRequestDto.cs`; `engine/src/ShoutingIguana.Cli/Commands/ExportCommand.cs`; tests
  `Sidecar.Tests/ExportEndpointsTests.cs`, `Sidecar.Tests/FixLoopChangesTests.cs`, `Cli.Tests/CliTests.cs`,
  `Engine.Tests/Export/GridExportTests.cs`, `Engine.Tests/Export/WorkbookExportTests.cs` (`99b350b3`, `b9f5d757`).
- no lane's list: `app/src/api/exports.ts`, `app/src/api/client.ts` and its test, `app/src/export/offers.ts` and two
  tests (`8435dc45`); `app/src/screens/ReportScreen.test.tsx` (`30ca91f4`); `app/e2e/report.spec.ts`,
  `app/e2e/simple-links.spec.ts` (`8a43f1da`).
- Nothing under `app/src/simple/**` was edited. `plainRows.test.ts` imports from it.

## What ran, and what did not

Ran, with these results:

- `dotnet build engine/ShoutingIguana.Engine.slnx -c Release`: 0 warnings, 0 errors, at `fd246971`'s tree.
- `dotnet test engine/ShoutingIguana.Engine.slnx -c Release --no-build`, whole suite, at `b9f5d757`'s tree: Engine.Tests
  2,428 passed, Sidecar.Tests 333 passed, Cli.Tests 45 passed, 0 failed.
- After `fd246971` (two things a page for a notice): only the Reporting tests, 60 passed. **The whole suite was not
  run again after it.**
- App: `npm run typecheck` clean at head. ESLint clean on `src/screens/report`, `src/screens/ReportScreen.test.tsx`,
  `src/api`, `src/export` and the two e2e specs. Prettier check clean on `src/screens/report` and the screen's test.
  Vitest, both projects: `src/screens/ReportScreen.test.tsx` and `src/screens/report` 81 passed, 15 skipped; `src/api`,
  `src/export`, `src/app` with them in the `app` project 257 passed.

Did **not** run: whole Vitest (`npx vitest run --maxWorkers=8`); whole `npm run lint` and `npm run format:check`; the
stories; any e2e spec (the two edited ones included); the 100k bench (`verify-engine.sh`); the Linux Docker check;
the sidecar build after `b9f5d757` (the live app ran a sidecar built at `30ca91f4`'s tree).

Tests seen failing first: the comparison words, the app's seven tests (client, screen, parts). The engine's report
tests and the sheet's test were written with their code; their red state was a build that didn't compile, not a
failing assertion. `fd246971`'s test was not seen failing at all.

## Seen in the live app

Yes, at 2560 × 1380 and 1280 × 720 in Simple and 2560 × 1380 in Advanced, on a copy of the garden crawl, with a sidecar
built from `30ca91f4`'s tree (so before the sheet change and before two-things-a-page). Driven by
`…/simple-fix/report/flows/report.mjs`; shots in `…/simple-fix/report/shots/`:

- `after-simple-2560-01-top.png` … `-07-summary.png`, `after-simple-1280-*`, `after-advanced-2560-*`. I looked at
  `after-simple-2560-02-section-404.png`, `after-simple-1280-01-top.png` and `after-advanced-2560-03-worth-a-look.png`;
  the other eighteen were taken and not looked at.
- Seen: at 2560 the sheet is 1,140 px wide at zoom 1.5 (frame 1,290 px); at 1280, zoom 1.1. The 404 section reads
  "Page to edit | It leads to | What's wrong" with the page as a link and "The link that says “Buy Now”, in the main
  content" under it, all 8 rows, one page's five links together, "Open in What to fix" at the heading's right.
  Advanced shows "Open in Issues", the technical name and the engine's words.
- Logged by the flow: a "Fix these first" row scrolls the frame (0 → 1,928); the page-to-edit link opens
  `https://www.gardenfurnituresets.co.uk/rattan-furniture/rattan-fire-pit-sets/` (recorded `window.open`, not
  loaded); the contents link and Enter on a focused link scroll; the heading link lands on
  `/crawls/…/issues/internal-client-error` and Back returns; under print media the app's link is `display: none`,
  links are plain and the zoom is 1.
- Before shots are the auditors': `…/scratchpad/audit/shots/simple-w-50-report.png`, `simple-w-51-report-404-rows.png`,
  `simple-w-52-report-summary-page.png`, `shell-16-report-summary-c.png`.
- Not seen: the summary page's "Since your last check" line (my scratch sidecar had no comparison, so the note was
  absent; its words are held by tests only); the built shell (Tauri), where `openInBrowser` uses the opener plugin;
  a real print or PDF.

## Known breakages and loose ends

- **The parity test will fail after the merge wherever the simple lane changed a sentence** in `app/src/simple`
  (`plainRows.test.ts`). That is its job: write the cases again (command above), then make `ReportPlainWords` or
  `ReportPlainDetails` say the same until `PlainRowsTests` passes.
- Commit `6b6176d1`'s message and `docs/v2-sidecar-api.md` say "113 of the 150 checks": that counts checks with no
  detail. The mirrored details are 101 checks'; 37 with details on the synthetic crawl are not.
- `docs/v2-ui-ia.md` lines 361 to 370 still describe the report's "Linked from 3 pages: …" and the sheet's "Linked
  from" cell (not this sub-lane's to edit). `docs/v2-ui-simple.md`'s 237 KB and "about twice as long" were measured
  on screen, not printed.
- `app/design/report/garden-full.html` and `garden-summary.html` (the Report stories' document) are an engine report
  of 13 November: the stories show the old rows and no "Open in What to fix".
- The image line says "The image file is 213.6 KB" (the app's `formatBytes`) beside a detail that says "214 KB" (the
  engine's): the app's cards do the same.
- Between `6b6176d1` and `99b350b3` three sidecar and CLI tests fail (they pinned `<h2>What to fix</h2>`); from
  `99b350b3` they pass.

## Next five steps

1. After merging: run the whole engine suite, the 100k bench, whole Vitest, lint, format check, the Report stories and
   `e2e/report.spec.ts` and `e2e/simple-links.spec.ts`. None of the app-wide or e2e runs has been done.
2. Bring the plain words back in step with the simple lane's: run `plainRows.test.ts`, write the cases, fix the
   engine's mirror until `PlainRowsTests` passes.
3. Decide one row per link in the sheet (item 6): it is an engine-lane change to `GridExport.WriteGrid` and to what
   every export count means; until then the other pages share a cell.
4. Mirror the 37 checks' details listed above, so a synthetic-scale site's plain report has no engine text.
5. Capture `app/design/report/garden-*.html` again from this engine, and check the report in the built shell: a link
   opening in the system browser, and print to PDF at its own size.
