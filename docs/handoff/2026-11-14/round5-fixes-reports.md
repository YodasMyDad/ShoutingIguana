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

## F2, the engine and shell: `ui/r5-fix-engine`: running
