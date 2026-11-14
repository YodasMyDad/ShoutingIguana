# Brief: finishing step 3 (UI, round 5), four agents at once

Read `C:\Projects\ShoutingIguana\.claude\worktrees\_handoff\2026-11-11\ui-slice-rules.md` first: the rules every UI
slice follows and the defects earlier reviews kept finding. Commit lines end with
`Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. This brief adds your part. **Four agents build round 5 at
once, each one part (A, B, C or D, named in the message that launched you).** Read the whole brief so you know where
the others' edges are; build only your part.

Step 3 is built and was audited on 2026-11-14. The owner's word on the findings: **"Fix everything."** The findings
are `docs/handoff/2026-11-14/closing-audit-findings.md` (gaps numbered 1-32, the gallery's defects G1-G6 and its
"Minor" list at the end): read all of it, then the gaps your part owns below. "Fix" means built to the design docs'
promise, with tests and stories, not deferred. Where a gap says "defer", the owner has overruled that: build it.

## Where you work

Each part has its own worktree and branch, cut from `ui/step-3` at the head named in the message that launched you.
The worktrees exist and have the fixtures copied.

| Part | Worktree | Branch | Ports (dev, sidecar, preview, e2e sidecar; Storybook) | Scratch folder (under `S5`) |
| --- | --- | --- | --- | --- |
| A | `.claude/worktrees/r5-simple` | `ui/r5-simple` | 61874-61877; 7606 | `r5-simple\` |
| B | `.claude/worktrees/r5-home` | `ui/r5-home` | 62874-62877; 7706 | `r5-home\` |
| C | `.claude/worktrees/r5-grid` | `ui/r5-grid` | 63874-63877; 7806 | `r5-grid\` |
| D | `.claude/worktrees/r5-shell` | `ui/r5-shell` | 64874-64877; 7906 (and 64878 for WebView2's DevTools) | `r5-shell\` |

`S5` = `C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\a539aa23-e856-44c1-bd21-ca484e8740e2\scratchpad`.
Make your scratch folder with a `tmp` inside for `TEMP` and `TMP`; keep `checkpoint.md` there at each phase boundary.
Ports 50000-50059, 50870-51825 and 56376-56475 are reserved by Windows here: never use them.

Shared files (`keyboard/registry.ts`, `api/queries.ts`, `api/types.ts`, the mode table, the router, the glossary,
`tokens.css`): add, don't reorder or reformat, so the four branches merge cleanly. A shared component another part
also touches is owned by the part named below; the others use it as it is on `ui/step-3`, and report what they'd
need changed instead of changing it.

**The engine (`engine/`) and the shell (`app/src-tauri/`)**: the slice rules say stay out of `engine/`. For this
round, a part may change the engine or the sidecar API ONLY for the items below marked "engine allowed", following
`CLAUDE.md`'s engine rules (only `Storage/` writes; parse once into `PageFacts`; never rename an issue id; a new check
needs a planted case and its count) and keeping `dotnet test engine/ShoutingIguana.Engine.slnx` and
`si bench --pages 100000` green. Document any API change in `docs/v2-sidecar-api.md`. Only part D changes
`app/src-tauri/`.

## Part A: Simple's truth and words, and the stopped check

Gaps 1, 2, 6, 7, 11 (= G5), 12, 14, 25, 29, 30, G3, and the Minor "Home's last checked 14:51 against 14:13".

1. **Gap 1.** A robots.txt that didn't answer or was unreachable is never "tells checkers to stay out": each robots
   state gets its own true sentence (slice rules, item 2). And an address typed without a scheme must not end in "no
   pages could be read" when the other scheme answers: find where the scheme is chosen and make the check reach the
   site that answers (engine allowed, if the choice is the engine's). Find why that check took about 2 minutes and fix
   it if it's a wait the user shouldn't sit through.
2. **Gap 2, in both modes.** A stopped check gets its results: the score "so far" (the dashed ring), as
   `v2-ui-simple.md` "The Summary" (stopped early) and `summary-stopped.html` show, and Advanced's Overview the same
   from that analysis. Decide with evidence whether a stopped crawl is analysed when it stops or on first open (engine
   allowed, if the engine doesn't analyse a stopped crawl), and never offer "Open the report" as the primary action
   when there are no results.
3. **Words (gaps 6, 7, 29).** No Advanced or engine word in Simple: the palette's and `?` sheet's headings, export
   messages (every line of `exportWords.ts` the audit lists), About, Updates, the export toast, "Checking now" while it
   only works out results. Add a test that renders every Simple screen, palette, sheet, toast and dialog and fails on
   the glossary's Advanced-only words (crawl, URL, issue, engine, analysis, analyse, notice, warning, error...). The
   shell's own strings (tray, notices, quit question) are part D's.
4. **Gap 11 / G5: owns `feedback/AppToaster.tsx`.** A toast with two actions puts its actions under its text when the
   text would go narrower than a readable measure; both modes, both themes, 200%.
5. **Gaps 12, 14, 25, 30, G3, the "last checked" time.** "Where it is" says what it shows in true words (causes vs
   addresses); the Summary's report card gets Save as PDF and the subtitle its duration (as `summary.html`); no slow
   alert before there are enough answers to judge; Simple Home tells sites apart by host AND port; a problem opens at
   its top whatever the list's scroll was; Home, the breadcrumb and the sidebar say the same time for the same thing
   (say which: started or finished, consistently).
6. Update `docs/v2-ui-simple.md` where the built names differ (gap 32: `si export report`; `noPages`/`notScored`).

## Part B: Advanced Home, one count everywhere, Overview and Issues

Gaps 3, 4, 15, 16, 17, 28, 31 ("Advanced Home has no visible Open button"), the Minor "1 crawling for a paused
crawl", and gap 32's `v2-comparison.md` lines.

1. **Gap 3: one number everywhere** (`v2-ui-ia.md:95`). Settle the definitions (URLs found vs URLs in the grid vs
   pages read; probes and not-checked URLs per slice rules item 1) in `v2-ui-ia.md`, then make Home, the sidebar,
   Overview, URLs, Issues, Simple's Summary, What to fix and the report agree, with a test on the garden, 100k and
   test-site crawls that reads every screen's count and compares them (engine allowed, for the library's figures).
2. **Gap 4: Advanced Home as designed** (`v2-ui-ia.md:89-94`, `v2-ui-design.md`, and the Home section of
   `v2-ui-brief.md` "Screens"): crawls grouped by site, the running crawl's card, and per crawl Open, Continue, Crawl
   again, Analyse again, Show in folder, Delete (with the confirmation the docs give), a visible Open… for a crawl
   file; every state (empty, one, many, running, paused, stopped, failed, out of date, never analysed); keyboard and
   palette. Remove the "Foundation version" comment.
3. **Gap 17: drag and drop** a `.sicrawl` file onto the window opens it (both modes; a wrong file says why). If the
   shell must forward the drop, say exactly what you need from part D and build the page side.
4. **Gaps 15, 16, 28 and the sidebar.** "% of total" beside issue counts in the checks list and Fix first, against
   the right total (pages for page checks; say which); Overview opens with a totals row (Crawled, Internal,
   External, as `v2-comparison.md` "What to take from Screaming Frog" says); a site-wide fault reads as the whole site
   in Advanced too; the sidebar says paused for a paused crawl.
5. Update `docs/v2-comparison.md` (gap 32's lines; mark what is now done) and put its "later" items (SERP snippet,
   duplicates diff, Insight) in `v2-ui-ia.md` "Room for what comes later".

## Part C: the grid, URLs, Links, the inspector and exports

Gaps 8, 9, 10, 13, 19, 20, 21, 22, 23, 24, 27, G4, the Minors "URLs search placeholder cut" and "a click on Filter by
type didn't open its menu once", and the parity item "the workbook doesn't take filters".

1. **The grid (`app/src/grid/`, owned by C)**: column resize (drag and keyboard), reorder and pin, remembered per
   view and saved with saved views (gap 10); the row menu's "Filter by this value" and "Exclude this value" for the
   cell under the cursor (gap 9); the menu opens at the pointer or, from the keyboard, at the row (G4); key hints in
   the footer as `v2-ui-design.md` gives them, using `--grid-foot-height` (gap 21); "Clear the filters" keeps the sort
   (24); no raw column id in any message (27); the search's placeholder fits and says Enter applies it, or it
   applies as you type within the budget (23 and the Minor: pick one with measurements); the Filter by type button
   opens on the first click (reproduce it first).
2. **Gap 13**: at 200% the URLs screen shows a usable number of rows, with and without the inspector (the header and
   filter bar fold, as the 200% sections of the design doc allow).
3. **Gap 8**: Ctrl+F on the inspector's Source tab, from the tab, the panel or the viewer, searches the source.
4. **Exports (gaps 19, 20, 22, and the workbook)**, engine allowed: checks hidden for this crawl only as well as for
   all crawls (discovery decision 2), and exports leave hidden checks out; an "All columns" preset; the inspector's
   link grids export; the workbook takes the view's filters as the CSV does.
5. Update `docs/v2-ui-architecture.md` (the grid; remove "not built" for what you built).

## Part D: the shell, the frame and the platform

G1, G2, G6, gaps 5, 18, 26, 31 (licences, Open logs folder), and a Simple walk in `smoke:shell`. You alone change
`app/src-tauri/`; you own `layout/` (the shell's frame, the top bar) and the Report screen's frame styling.

1. **G1**: the top bar never cuts off the Simple | Advanced switch: at 1280 px with a crawl running in Advanced,
   search and Home give way first, as `v2-ui-ia.md`'s order of giving way says; a test at 1280, 1024 and 200%.
2. **G2**: no page scrolls the whole window (the `sr-only` spans' positioned ancestor; check every route at 1280x720
   and 200% for `document.scrollingElement.scrollHeight > innerHeight`, as a test).
3. **G6**: the Report screen's toolbar keeps its subtitle to one or two lines at 1280 px; the report's frame uses the
   app's scroll bars (the report is the engine's HTML: if the scroll bar can only be styled in the engine's report
   CSS, engine allowed for that alone).
4. **Gap 5**: the shell speaks the current mode: the hide-to-tray notice, the tray's tooltip and menu, the quit
   question, in Simple's words in Simple (the page tells the shell the mode; the shell keeps Advanced's words in
   Advanced). Test both.
5. **Gap 18, the platform**: a signed in-app updater (`tauri-plugin-updater`, checking the GitHub releases of this
   repo's `origin`; generate the signing key pair with the Tauri CLI, keep the private key OUT of the repo at
   `%USERPROFILE%\.shouting-iguana\updater.key`, put the public key in the config, and write in
   `docs/v2-ui-architecture.md` exactly what the release workflow needs (the secret's name; how a release publishes
   `latest.json`); Settings › Updates checks, says what's new, and installs on the user's word); a proxy test in
   Settings (engine allowed: a sidecar endpoint that fetches through the proxy and says what happened, in plain
   words); credentials (proxy, and any site login the settings hold) kept in the OS credential store, never in
   settings files; a data folder the user can change (moves the crawls with a progress and a way back, or opens the
   new one empty: settle it in the architecture doc with the reason).
6. **Gaps 26, 31**: each screen gives the window its own title ("Issues · garden… · Shouting Iguana"); About lists
   the third-party licences (generated at build time from the npm, Cargo and NuGet packages actually shipped);
   Settings › Diagnostics has Open logs folder.
7. **`smoke:shell` walks Simple too** (after the Advanced walk, or `--mode simple`): first start, a check of the test
   site, close to the tray with the Simple notice, back, the Summary, the Report, quit. Run both walks on the release
   build at the end.
8. If part B asks for a drop to be forwarded from the shell (gap 17), build the shell side.

## All four

- Every state in Storybook (both modes where the screen is in both, both themes, forced colours, 200% zoom, reduced
  motion), e2e for your flows on the engine's test site, a test seen failing before each fix, and the slice rules'
  full check list green on your branch at the end.
- Before your final checks, `git merge ui/step-3` into your branch (it may have moved) and run every check on the
  merged result. Don't merge other parts' branches.
- Measure what you change against the budgets (first page of a grid 300 ms; a click or key to a visible response
  100 ms; Overview of the 100k crawl 1 s).

## Report back

As the slice rules ask, plus: each gap you own, by number, with what you built and the test that proves it;
anything you couldn't build and exactly why (an owner decision needed, a thing this machine can't run); engine or API
changes; anything you needed from another part.
