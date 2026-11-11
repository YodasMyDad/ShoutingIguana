# Links slice: review findings (saved by the orchestrator from the reviewer's report, 2026-11-11)

Reviewed: `ui/links` at `2175872` (`git diff 17b340d..2175872`). 14 findings: 1 high, 5 medium, 8 low. Proof scripts
(`t*.mjs`, `walk-*.mjs`), logs, screenshots (`shots\`) and `reviewPattern.proof.test.ts` are beside this file. On a
fresh `npm ci`: typecheck, lint and format clean; unit 996 passed + 1 skipped; script tests 26; e2e (`links`,
`links-sweep`, `issues-subject`) 18 passed, 1 failed (finding 3). Not run: Storybook, the story checks, the rest of
the e2e suite.

## Findings, most severe first

**1. HIGH. Issues: a check's share is the analysis's count over the file's count as it is now. CONFIRMED**
- Where: `app/src/screens/IssuesScreen.tsx:184-196`, `app/src/crawl/checkSubject.ts:112-121`,
  `docs/v2-ui-architecture.md:557-564`.
- Scenario: stop a crawl, analyse it, continue it, stop again. A stopped crawl isn't analysed automatically, so the
  old counts stay on screen against the new totals.
- Seen (`t7-continue.mjs`, `t7-continue.log`): analysed at 1,661 pages, continued to 9,206. `title-duplicate`
  flagged 52 pages. Issues says "0.6% of the 9,206 pages crawled"; the truth is 3.1% of 1,661.
- Fix: use `analysis.pagesAnalyzed` for a `page` check; show no share for a `url` check while `outOfDateReasons`
  has `crawlChanged`.

**2. MEDIUM. At 200% zoom the Links grid has room for two rows, and none under its own presets. CONFIRMED**
- Where: `app/src/screens/LinksScreen.tsx:391-578`. Four toolbar rows sit over a grid with no least height, and
  nothing outside the grid scrolls.
- Seen (`t5-zoom.mjs`, `t5b-zoom-urls.mjs`, `shots\t5-640-*.png`), at 640x360 on the 100k crawl: Links with no
  filter: grid 94 px, 2 rows. Links "No anchor text" (two chips): 58 px, under 1 row. Links with three chips: 22 px,
  0 rows. URLs with one chip: 198 px, about 5 rows. With three chips the "no links match" state and its Clear can't
  be seen either.
- Fix: give the grid a least height and let the screen's column scroll, or fold the presets and facets into one row
  at narrow widths.

**3. MEDIUM. An e2e test passes by the luck of the crawl that makes its fixture. CONFIRMED (it failed here)**
- Where: `app/e2e/links.spec.ts:148-160`.
- Defect: it takes the site's origin from the first link's source URL. In the crawl made here the first stored link
  is the redirect from the test site's other host (`/ext-moved`).
- Seen (`e2e-links.log`, `e2e-failure\error-context.md`): expected `"/ext-moved"`, got `"http://127.../ext-moved"`.
  The crawl is cached per machine, so one machine always passes and another always fails.
- Fix: take the origin from the crawl's start URL.

**4. MEDIUM. Facets are read for every view, and every second of a crawl, with every menu closed. CONFIRMED
(requests and growth); PLAUSIBLE (cost at 5M links)**
- Where: `app/src/screens/LinksScreen.tsx:144-149`.
- Seen (`t6-live.log`, `t7-continue.log`): one `/links` and one `/links/facets` a second while crawling. A fresh
  facets count took 50 ms at 137k links and 143 ms at 467k; page 0 stayed at 5 to 46 ms. Extrapolated: a 5M-link
  crawl spends about 1.5 s of every 2 s counting facets nobody sees.
- Fix: read the facets when a facet menu opens; at the least not while crawling with the menus closed.

**5. MEDIUM. A filter the engine refuses gets a generic sentence and one way out, which throws the view away.
CONFIRMED**
- Where: `app/src/screens/LinksScreen.tsx:509-517`.
- Seen (`t3-keys.mjs` step g, `shots\t3-refused-pattern.png`): the pattern `(?=x)y` shows only "This view's query
  can't be run." The engine's plain sentence is behind Details. "Reset the view" navigates to `search: {}`, dropping
  the other filters and the chosen columns. The state has no story.
- Fix: show the problem's `detail`, offer "Change the filter" and "Remove the filter", keep the columns, add the
  story.

**6. MEDIUM. The filter editor refuses patterns the engine runs. CONFIRMED**
- Where: `app/src/grid/filterEditing.ts:204-207, 224-232`. `patternProblem` compiles a .NET pattern as a browser one.
- Seen (`t2-filters.mjs`; failing test `reviewPattern.proof.test.ts`, 4 of 5 fail): the editor refuses each with
  "This isn't a pattern that can be run", and the engine runs all four: `(?m)^Garden` (1,899 links), `(?s)a.b`
  (126), `a(?i)b` (1,131), `(?x) gar den` (51).
- Fix: drop the browser check, or strip every .NET inline option group before it.

**7. LOW. A hidden window still reads the grid and facets every second and redraws the count. CONFIRMED (hidden
state emulated)**
- Where: `app/src/live/liveBridge.ts:119-127`. Shared code the slice didn't change; the URL grid does the same, and
  Links adds the facets read.
- Seen: the HIDDEN lines of `t6-live.log`.
- Fix: skip the refresh while `document.hidden`, and refresh once when shown.

**8. LOW. After a reload, Back, Forward or a link, the inspector follows the other end of the rows. CONFIRMED**
- Where: `app/src/screens/LinksScreen.tsx:154, 551`. `inspectedEnd` is component state, `'to'` at mount.
- Seen (`t8-palette.mjs`): the inspector followed sources (12, then 148); after a reload, Down moved it to a target
  (1674).
- Fix: derive the end from the row and `search.inspect`, or keep it in the route.

**9. LOW. "Copy the target URLs" is the distinct URLs among the first 10,000 links, which the command doesn't say.
CONFIRMED**
- Where: `app/src/grid/copyAll.ts:26-45`, `app/src/screens/LinksScreen.tsx:260-271, 340-355`.
- Sound: at most ten reads of 1,000 rows, 10,000 rows held, Cancel after 300 ms, view order, duplicates out, and the
  toast says "From the first 10,000 of N rows".
- Seen (`t6-live.log`): a view of 583,554 links sorted by target status gave "Copied 1 target URL".
- Fix: say it in the command when the view has more than 10,000 links, or ask the engine for a view's distinct ends.

**10. LOW. A sort stays when its column is hidden, and nothing shows it. CONFIRMED**
- Where: `app/src/screens/LinksScreen.tsx:174-178`, `app/src/screens/links/presets.ts:80`.
- Seen (`t13-small.mjs`): the route still sorts, no header has `aria-sort`, and "All links" reads as chosen.
- Fix: drop a sort whose column is unticked.

**11. LOW. "Analyse it" on a crawl that was analysed, beside a banner saying "Analyse again". CONFIRMED**
- Where: `app/src/screens/LinksScreen.tsx:508` (the same words at `UrlsScreen.tsx:246`, older).
- Seen: `t12-out-of-date.mjs`, `shots\t12-needs-analysis.png`.
- Fix: word the button by `error.reason`, and show one button.

**12. LOW. A filter by an end's check shows the check's id. CONFIRMED (by a link only; nothing on screen builds one
yet)**
- Where: `app/src/screens/links/linkWords.ts:125-129, 138-146`.
- Seen (`t11-inspector.mjs`): "Target check is internal-client-error".
- Fix: word it by the catalog's title, as the URL grid's chips do.

**13. LOW. "All 1 in Links". CONFIRMED**
- Where: `app/src/inspector/LinksSection.tsx:211-221`.
- Fix: "Show it in Links" for one.

**14. LOW. The editor says capitals and small letters match each other; the contract says ASCII letters only.
PLAUSIBLE**
- Where: `app/src/grid/FilterEditor.tsx:153`. Neither crawl has an accented anchor text, so not shown on data.
- Fix: say "A to Z", or fold case fully in the engine.

## The denominator verdict (Job 1)

What each count is, from the engine:

- `pagesAnalyzed` (`CrawlAnalyzer.cs:82, 203, 309`; `AnalysisReader.cs:22, 152-173`): the pages the page rules ran
  on: internal, HTML, successful, body parsed, not under `/cdn-cgi/`, with details that decode. Stored by the same
  analysis as the counts.
- `pageCount` (`CrawlSummaryReader.cs:80-89`): the same definition without the `/cdn-cgi/` exclusion, counted in
  the file as it is now.
- URLs found: the URL grid's unfiltered `total`, as the file is now. Nothing stores the URL count at analysis time.

**A `page` check: `pagesAnalyzed` is right, worded "of the N pages analysed".** Every page check's flags are among
those pages on both crawls (`walk-checks.mjs`). It is of the same moment as the count, so the share can never pass
100%. `pageCount` is right only while the analysis is current. Where they differ: a Cloudflare `/cdn-cgi/` page
(101,107 against 101,106 on the 100k crawl; equal at 265 on garden); a crawl continued after its analysis (9,206
against 1,661: the case that decides it); a page whose details don't decode (not seen).

**A `url` check: "URLs found" is right, as built, and "crawled" would be wrong.** It is the only count every URL
check's flags are part of (`internal-client-error` flags 3,451 URLs, none a page; `sitemap-url-other-host` flags 2
external URLs). It is still of now (41,629 against 40,826 after the continue): show no share under `crawlChanged`,
or have the engine store the URL count with the analysis.

Counts, nouns and landings are sound: every check's count equals the URL grid's count for it (19 checks on garden,
75 on the 100k). Not re-checked: skipped or passed checks, and that no share shows while crawling (the build's e2e
covers those and passed).

## The URLs screen against the IA

What it has now through the shared pieces: its chips are the shared `FilterChips` (read and remove, as before),
plus the shared `NeedsAnalysis` and `useUrlWords`. Nothing a user can see changed (`t4-urls.mjs`). Still missing:

- Ctrl+F: no command; the key is reserved and does nothing.
- Header sort: headers are plain text. A sort in the route is honoured and shows `aria-sort`, so only the wiring is
  missing.
- Row menu: right-click, Shift+F10 and the menu key open nothing. `GridCore` reports them, but no screen has a menu,
  Links included.
- The filter bar: no Add filter, no editing a chip, no facets, no Clear filters.
- Column chooser and column presets: the columns are a fixed nine.
- Also: saved views, export (Ctrl+E), "Show full URLs", and a whole-view copy.

## Looks right

- Numbers: 256 facet figures on garden and 279 on the 100k, under 15 views, each land on their count and add up
  (`walk-facets.mjs`). Offset and cursor paging agree, including descending, the last page and past the end
  (`walk-paging.mjs`). "All N in Links" lands on N, and Back restores the inspector.
- Route: a value with quotes, a comma, `%`, `&`, `+`, `#` and non-ASCII survives the editor, reload, Back and
  Forward. No engine id shows among the 95 columns or the enum labels (`t9-words.mjs`).
- Running crawl: the grid's element, cursor and selection held through the crawl and the hand-over to the analysed
  state. Never busy, never a skeleton, the count changed at most every 2 s.
- Requests and performance: one page and one facets read per view change, no duplicates. Heap 29 to 32 MB after far
  scrolling through 5M rows; the last row drew in 58 to 117 ms (`t10-memory.mjs`).
- Keyboard: facet typeahead, Esc and focus return work. Focus is right after chip removal, Clear filters, a header
  sort and each palette command. Ctrl+F stays with Source's search.
- Outside the slice: `GridCore`'s additions are opt-in. The moved test helpers and the two changed expectations are
  not weakened; the mock defaults to the URL grid's behaviour.
- Job 2: copies are kept while the original's stamp holds and copied again when it changes. It writes only to the
  data and stamp folders. Two worktrees on the default temp still share one data folder, as before. One hardening:
  `copyCrawl` has no guard against being given the original's own folder.

Not defects, worth knowing: during the analysis that follows a crawl, Links says "hasn't been analysed yet" (it
could say it is being analysed); popovers open with focus on Close rather than the first field; a 21st filter is
dropped silently.
