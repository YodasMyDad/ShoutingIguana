# Round 5's review fixes: the fixers' reports, condensed

## F1, the app: `ui/r5-fix-app` (15 commits on `008b4b2`)

Every app finding fixed, each with a test seen failing then passing; none judged not a defect.

- R1-1: Advanced's `CrawlMenu` and Simple's `RowMenu` keep the crawl the dialog was opened for (a test each deletes the
  right crawl after the swap).
- R1-2: engine grid operator `isNot` ("the other value or empty"; `FilterOperator.IsNot`), URL and links grids and the
  `indexable` special case; Exclude uses it; the editor offers "is not yes/no". Engine reference-evaluator tests and
  `valueFilter` unit tests.
- R1-3 heading from `runningTitle`, row badge from the live status. R1-4 an analysing-only card (Crawled, Pages; no
  queue, rate or bar). R1-5, R1-6 header cursor and stuck drag. R1-7 "Not crawled" = `outcome is empty` (a URL with a
  status code always has an outcome). R1-8 Crawled's line reads "URLs the crawl asked for" (Overview's words; the
  progress event can't count the probe exactly). R1-9 `reportedOnce` reads "Whole site". R1-10 the status line is one
  line (tooltip for the rest), `useElementWidth` measures before first paint; a per-frame height e2e at 640x360 and
  1280x720 failed before, passes at repeat 10. R1-11 no Continue for a missing file. R1-12 "Show this check in every
  crawl / in this crawl", with Undo.
- Also noted: the grid query and its exports take `hiddenChecks` (engine, built by F1's subagent and reviewed): the
  four issue-count columns subtract hidden checks in values, sorts (paging cursors too), filters and facets; SQL
  unchanged with nothing hidden; links grid ends too; unknown ids accepted. The refusal names the column from the
  sorted list sent.
- R2-1 app side: `quickStartUrl` sends the defaults with secrets restored. R2-2: delete forgets `crawl:<id>`; Remove
  moves secrets out of the store at once and holds them in memory for Undo.
- Leftovers: Advanced Home names sites by host and port (e2e `siteRow()`, `SI_E2E_SITE_ROW`); `GoToUrl`'s slowness
  was the first render paying the lazy screens' load (0.25 s of 0.6 s, >5 s under load): a `beforeAll` pays it once;
  back on the 5 s default.
- Engine files outside the grid readers (may conflict with F2): `Sidecar/Api/ExportRequests.cs`,
  `Sidecar/Crawls/GridData.cs`, `Engine/Export/ExportFields.cs`, `Cli/Commands/ExportCommand.cs` (grid exports now
  honour `si export --hidden-checks`).
- Checks: typecheck, lint, format; unit 3,730 x 3 (98 skipped; one run under load); storybook; scripts 49; stories
  528 x 3 modes clean; sidecar; e2e 226 (2 skipped); engine 1,478 + 282 + 28. Not verified: the 100k bench and
  readbench; sort/filter speed by an issue count with hidden checks on a big crawl; R1-10's first-frame jump never
  captured failing; the links-grid refusal fix has no test of its own; no screenshots.

## F2, the engine and shell: `ui/r5-fix-engine` (`7afc667`…`ce0d000` on `008b4b2`)

R2-1 (engine), R2-3 to R2-11 fixed, each test seen failing then passing except as noted.

- R2-1: `StartAddress` doesn't count a 407, a refused tunnel, or a 5xx over plain http through an http proxy as an
  answer (trade-off: a real http-only site answering 5xx through a proxy starts on https). R2-7: `start-url` checks its
  options and answers `invalid-options` (422) without probing; validation also refused `ftp://` proxies (they used to
  500 a crawl start too).
- R2-6: only a crawl the user stopped is analysed; a give-up on a dead site isn't (code comment and API doc agree).
- R2-8: inspector queries use `CrawlSchema.NOT_CLOUDFLARE`. R2-5: the report's score table gets a row for left-out
  checks ("2 hidden checks · Left out of this report; the score still counts them") and adds up; "Held at 50" says so
  when the holding problem is hidden. R2-9: a single check's export takes `hiddenChecks` (ExportFields, API, CLI,
  `app/src/api/exports.ts` type) and is numbered as in the workbook with those hidden.
- R2-3 and R2-10: the update install re-checks under the quit lock ("Something started while the update downloaded.
  Install it once that has finished."); the shell tracks running exports from the event stream and refuses the
  folder switch and the install while one runs, in plain words.
- R2-4: an analysed stop is told as a stop: Advanced "Crawl stopped" / "example.com: 1,234 URLs, 18 checks found
  issues. Continue it from the library."; Simple "The check of example.com was stopped" / "Score 71 so far: 2
  problems to fix. Continue it from Your sites."; a cancelled analysis gives the stop alone.
- R2-11: the shell logs each tooltip state and menu; a new state's tooltip is drawn at once; the Simple walk checks
  checking -> working out the results -> idle with no Advanced words (`trayWords`, `simpleTrayProblems`).
- Checks: engine 1,456 + 282 + 28; 100k bench (193 MB flat); readbench gate; cargo fmt, clippy, test 139 + 3;
  typecheck, lint, format, scripts 51; unit green x2 (one load run failed `CrawlSettingsScreen.test.tsx` "continue
  without the headers", `findBy` 1 s default; passed on a second load run); stories 528 x 3 clean (first attempts died
  with Node errors under contention, passed on rerun); e2e 224 (2 skipped); release build, `smoke:shell` Advanced 17 +
  Simple 8.
- Needed from F1: `app/src/export/offers.ts`, the `kind: 'check'` request of "This check", should send `...hidden()`
  (or `hiddenChecks: currentHiddenChecks(item.id)`).
- Not verified: R2-3 only through the extracted `begin_stopping` (no signed download); R2-8's "audited" half; R2-5's
  "Held at" sentence not seen failing; the new walk not run against the old shell. The tray shows "Shouting Iguana"
  for a few ms between a crawl's end and its analysis (the live view drops the crawl then; the menu did so before).
