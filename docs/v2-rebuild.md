# v2 rebuild: plan, decisions and status

The single place to pick up the rebuild. Technical detail lives in [engine/README.md](../engine/README.md) and the
rules for working in the code in [CLAUDE.md](../CLAUDE.md). Update the status and open items here at the end of every
step. For step 3, **Where step 3 stands** below is the only status. `v2`'s copy of this doc only points here. Update
this section before you stop, then push `ui/step-3`.

## Why

The WPF app in `src/` was hard to build on, its UI was clunky, and large sites caused memory problems. An audit
traced the problems to the design, not to C#:

- every page went through Chromium;
- the HTML was stored and reloaded about nine times per page, and each page was parsed ten or more times;
- plugins held static state;
- the default 1.5 s delay per host meant 40+ hours for 100k URLs.

v2 is a fresh solution that ports the useful logic.

## Plan

| Step | What | Status |
| --- | --- | --- |
| 1 | Headless engine, storage and CLI (`si`), proven on a generated 100k-page site, with the benchmark in CI | Done: `3e556be` |
| 2 | SEO rules and issue catalog (84 checks), `si analyze` / `si issues`, exact-match proof on the synthetic site | Done: `1489adb`, then a real-site crawl and two full reviews ([Status](#status)) |
| 3 | Desktop UI: React + TypeScript in a Tauri 2 shell, with the engine as a sidecar behind a token-protected localhost API. Enterprise-grade UX: the bar is in [the UI brief](v2-ui-brief.md). Since 2026-11-10 it opens in Simple, with Advanced one switch away, and ends in a report: [Simple first](#simple-first-advanced-on-a-switch-2026-11-10) | In progress on `ui/step-3`. Pick up at [Where step 3 stands](#where-step-3-stands-2026-11-11) |
| 3b | The check audit: every check proven against real sites and worded for a non-expert, then the old app's checks worth keeping and those a comparison with Screaming Frog shows are missing | Planned on 2026-11-10: [Simple first](#simple-first-advanced-on-a-switch-2026-11-10) |
| 4 | Missing basics: JavaScript rendering, a browser fallback for blocked sites, include/exclude rules, auth and cookies, list mode, custom extraction | Later |
| 5 | Integrations: Search Console, then GA4, Bing Webmaster, Google Ads and Microsoft Advertising | Later |

**Step 3 starts with a design conversation with the owner, not with code.** The owner wants a "super slick,
enterprise level" UI and UX. The old app was "clunky and a bit all over the place". **[The UI brief](v2-ui-brief.md)
sets the bar and the process**: discovery, sign-off on mockups and a clickable prototype, vertical slices, and a
definition of done for every screen. It also covers the performance budgets and the engine work the UI needs.
Discovery and the visual direction were settled on 2026-11-01 (direction B, "clarity"). Do not restart them. Simple
(below) is added to them, in the same look.

## Simple first, Advanced on a switch (2026-11-10)

On 2026-11-10 the owner changed what the app is for. This section is the plan for that change. None of it is
designed or built yet: the owner asked only for the plan.

**What the owner asked for:**

- An SEO tool that is "super easy for anyone to understand".
- The app opens in a "simplified user friendly UI" that shows the overview. A switch to an "advanced mode" lets a
  technical SEO "look at everything in detail".
- All the same checks, each "properly audited and analysed to make sure they work".
- "A report for everything at the end."
- A quick comparison with Screaming Frog: "what we're missing compared to them", and whether the UI is as good or
  better.

**What it changes:**

- **Who the app is for.** The 2026-11-01 answer was an agency with many client sites. Now anyone comes first, and
  the agency and the technical SEO are one switch away.
- **What a crawl opens on.** Simple shows the answer, what to fix first and how to fix it, in plain words, and the
  report. Advanced adds URLs, Links, Sitemaps, robots.txt, the inspector, the filters and columns, and the crawl
  settings beyond the start URL.
- **The report moves into step 3.** The UI brief and the IA had client reports as "later". The report is now what a
  Simple crawl ends in.
- **The checks' words.** They were written for an SEO. Each check gets words a non-expert can act on. Whether that
  is a new line for each check or the existing texts rewritten is settled in the Simple design.
- **A score.** The Simple summary carries one number, with how it is worked out one click away. The owner said yes
  to it on 2026-11-10. What goes into it is settled in the Simple design.

**What it doesn't change:**

- The engine, the crawl file, the sidecar API and the 86 checks.
- Everything step 3 has built or has in flight. Those screens are Advanced as they stand, and none is redone.
- Steps 4 and 5, unless the comparison reorders them.

**Rules for the Simple design:**

- Simple is a view over the same crawl, the same checks and the same numbers. It is not a second app, and it shows
  no figure that Advanced can't list the URLs for.
- One switch, in the same place on every screen, and remembered. A first start is Simple.
- Simple starts from what exists: Overview (done), with its answer and "Fix first", and each check's "what's wrong,
  why it matters and how to fix it".
- "Simple" and "Advanced" go in [the glossary](v2-ui-discovery.md#glossary) before the app uses them.

**Where it fits: inside step 3, before round 3.** It doesn't interrupt what is in flight, and it doesn't wait until
everything else is finished.

- Round 2 isn't held up. Links, Sitemaps, robots.txt and E3c are Advanced, and already built.
- Round 3 waits for the Simple design. It builds Settings, New crawl and export. The switch lives in the first,
  Simple's "paste a URL and go" is the second, and the report rides on the third. Built before the design, all
  three would be changed afterwards, and step 3's closing audit would be run twice.
- Simple itself is a fourth round, after round 3. Step 3 isn't finished without it, as it is the first thing a
  user sees.

The order, item by item, is "Left, in this order" under [Where step 3 stands](#where-step-3-stands-2026-11-11).

**The comparison.** Research only, no code. It writes `docs/v2-comparison.md`.

- Screaming Frog for features, checks and how much a screen holds. Sitebulb beside it for the Simple side: the
  owner picked it on 2026-11-01 for how it explains issues.
- Three lists: features, checks and UI. Each gap gets a verdict: step 3, the check audit, step 4, step 5, later, or
  not doing.
- The plan already holds the known gaps. Step 4: JavaScript rendering, include and exclude rules, logins, list mode
  and custom extraction. Step 5: Search Console and analytics. Later: crawl comparison, scheduled crawls and
  projects.
- It reads the vendors' own sites, documentation and screenshots. Neither tool is run: the owner has no Screaming
  Frog licence and asked for no more than that (2026-11-10).

**Step 3b, the check audit.** After step 3 and before step 4.

- **Why.** The synthetic site proves each check flags its planted case and nothing else. It doesn't prove a check
  is right on real HTML. One real site has been audited: 20 checks fired there, and 3 of them were wrong (since
  fixed or documented). Most of the 86 have never met a real site. A wrong flag costs more in Simple, where the
  reader can't tell.
- **How.**
  - Three real sites, named by the owner on 2026-11-10 and crawled with their HTML kept:
    `www.gardenfurnituresets.co.uk`, `www.aptitude.co.uk` and `jitsunami.com`.
  - For each check: its flags sampled against the stored HTML, for wrong flags; a sample of the pages it didn't
    flag, for misses; and its words read as a non-expert would read them.
  - Three sites won't make every check fire. A check that fires on none is listed as not seen on a real site. It
    stays proven on the synthetic site only, until the owner names a site that has the fault.
  - A verdict for each check in `docs/v2-check-audit.md`: right, fixed, reworded, severity changed or dropped. Each
    fix gets a planted case and a test seen failing without it, as before.
  - Then the checks in [the old app's backlog](v2-ui-discovery.md#backlog-checks-from-the-old-app), and those the
    comparison shows are missing.
- **When.** It is engine work and touches no screen, so it can run beside step 3's rounds if the owner wants it
  sooner. It costs usage, and that is theirs to call.

The owner answered this plan's four questions on 2026-11-10, and nothing here waits on them: a score, yes; the app
flags and explains, and doesn't draft or make fixes; the comparison reads the vendors' sites and screenshots; and
the three audit sites above.

## Where step 3 stands (2026-11-11)

Work on branch `ui/step-3`, worktree `.claude/worktrees/ui-step-3`, pushed to `origin/ui/step-3`. `v2` and `master`
were not moved. This section is the place to resume.

**Paused on 2026-11-11 by the owner, to save usage.** Step 3 is about two-thirds done. The owner asked for everyone
to finish where they had got to and for everything to be saved, so that it is easy to continue after their usage
resets. Nothing was launched after that, and nothing is running. Merging to `master` or releasing still needs an
explicit yes.

A new session needs this section and one folder:
`C:\Projects\ShoutingIguana\.claude\worktrees\_handoff\2026-11-11\` (outside git, on this machine). It holds
`checkpoint.md` (the session's log, with a PAUSE section at its end that has each branch's head), every brief
(`brief-*.md`), `ui-slice-rules.md` (the rules every UI slice follows), `tools\verify-app.sh` and
`tools\verify-engine.sh`, the reviews' findings (`reviews\`), and each agent's own checkpoint
(`checkpoint-<name>.md`). Subagents can't be resumed across sessions: relaunch each from its brief and its
checkpoint.

### Merged on this branch on 2026-11-11

- **[The comparison](v2-comparison.md)** with Screaming Frog and Sitebulb (item 3).
- **The Simple design** (item 4): [`v2-ui-simple.md`](v2-ui-simple.md), small edits to the IA, the design doc and the
  glossary, and 18 mockups on the garden crawl in `app/design/simple/`. It ends in three lists the build rounds are
  briefed from: what round 3 builds, what round 4 builds, what the engine needs. The owner's gallery of the
  mockups is published, private: https://claude.ai/artifact/R53PTE7wZb7rPZHX6Z3GfN (built by
  `gallery-simple\build.cjs` in the handoff folder, published from
  `C:\Projects\ShoutingIguana\.claude\worktrees\_gallery\simple\`: republish from that path to keep the URL).
- **Engine E3c, with its review's 7 fixes** (`ui/e3c`), each with a test seen failing without it:
  - A running crawl's log is folded past 64 MB while a links grid reads it: 75 to 87 MB at its peak on a
    40,000-page crawl with one reader, against 106 to 160 MB before, and the crawl is no slower. A read may wait
    for a fold; none fails. `si bench --read-links --max-log-mb` gates it, and CI runs it.
  - The time spent crawling survives a first run that stored nothing, a clock set back and a machine that slept.
  - `GET /sitemaps` gives each sitemap's `urls.robotsDisallowed` and `urls.redirectingOrBroken`.
  - A kept `/stats` takes no read slot. `stats-pass` is gated at 450 ms, and its miss of the 300 ms budget is a
    known limitation in the API doc.
- **The engine's score work** (`ui/score`; contract: "The score" and "Engine changes the score made" in the API
  doc):
  - The score, version 1, worked out by the analysis from its own counts and stored with it:
    `analysis.score`, `fixFirst`, `crawledUrlCount`; the library item's `score`; `si score`. The garden crawl
    scores 85, as the design works it out.
  - The catalog has **87 checks**: the new one is `site-hidden-from-search`. Checks carry `siteWide` and
    `reportedOnce`. `AnalysisFreshness.VERSION` is 7. A status in a detail reads "404 Not Found".
  - **The plain words for all 87 checks**, 17 categories and 15 skip reasons, written to the design's template,
    reviewed against each rule by a second agent and corrected. They are generated from one data file,
    `engine/tests/ShoutingIguana.Engine.Tests/Analysis/PlainWords/plain-words.json`. Ten texts name a limit as a
    token the engine fills from the settings the analysis used. `GET /api/checks` serves them.
- **Verified on the last merge by the orchestrator:** the engine build and 1,353 tests (1,156 engine, 176 sidecar, 21 CLI), the 100k bench (every planted issue, flat memory), and the app's typecheck, lint, format, unit tests, script tests and stories. The app's e2e was still running at the time of writing: its result is in the handoff folder's `checkpoint.md`, PAUSE section.
  - The first run of these checks found two faults that every agent's own run had missed: the merged tree didn't
    compile its sidecar tests (the score's test used a helper the E3c fixes had removed), and one file came
    unformatted from the E3c fixes. Both are fixed. `verify-engine.sh` now stops when the build fails, where it had
    gone on to test the previous build.
  - `readbench --gate` failed on this machine while four other agents were running, each time in a different
    class and by a different margin (1,139 ms and 2,078 ms against gates of 200 and 550 in two links classes, then
    245.7 ms against 245 in a facets class), and each class passed when run again alone. Owed: one whole run of
    the gate with nothing else running.

### Built on local branches, not merged

None of these is pushed.

| Work | Branch and worktree | State at the pause |
| --- | --- | --- |
| Links, with the Issues wording and the dev script | `ui/links`, `links` | All 14 findings and four smaller notes are fixed, each with a test seen failing without it (`7d896d5`). Its fixer was merging `ui/step-3` into the branch when the pause came: read the branch's log and `checkpoint-links-fixes.md` |
| Sitemaps and robots.txt | `ui/sitemaps-robots`, `sitemaps-robots` | All 17 findings are fixed the same way (`fe51eb0`). `ui/step-3` is not merged into it yet |
| Engine: export, the workbook and the report (E5) | `ui/e5`, `e5` | Phase one is done (`6edf697`): CSV and Excel export of every grid, the workbook, and the report on the technical texts, with `si export` and `si report`. Phase two (merging `ui/step-3`, then the report on the real score, order and plain words) was under way at the pause: its `HANDOFF` is at the top of `checkpoint-e5.md` |
| Engine: fast search, the favicon, the limits' API, and what round 2 found the engine lacks (E6) | `ui/e6`, `e6` | Under way at the pause, told to stop at a clean point: its `HANDOFF`, item by item, is at the top of `checkpoint-e6.md` |

### To resume, in this order

1. **Links.** Finish merging `ui/step-3` into `ui/links` if the branch's log shows it isn't done (87 checks and
   "404 Not Found" will break tests that count 86 or match "HTTP 404"; a URL check's share takes the analysis's
   `crawledUrlCount`). Then merge `ui/links` into `ui/step-3`, run `verify-app.sh`, push.
2. **Sitemaps and robots.txt.** Its fixer merges `ui/step-3` into `ui/sitemaps-robots` (expect conflicts in
   `GridCore.tsx`, `api/queries.ts`, `keyboard/registry.ts`, `e2e/overview.spec.ts`, `UrlsScreen.tsx` and both UI
   docs). Its two screens take the Links slice's `GridScreen` frame and `minRows`. The list's Crawled and Blocked
   columns have only been seen against a mock: check them against the real sidecar, which now sends the two
   counts. Then merge, `verify-app.sh`, push.
3. **Export (E5).** Finish phase two from its `HANDOFF`, then a fresh-eyes review of the whole branch (it has had
   none), fixes, the merge, both verify scripts, push.
4. **E6.** Finish its list from its `HANDOFF`, then review, fixes, merge `ui/step-3` in, the merge, both verify
   scripts, push.
5. **The catalog's fixes**, an engine job that isn't briefed yet:
   - `site-hidden-from-search` must also require the home page (or the start URL's page) to be noindex. As built
     it counts noindex pages only, and a shop with many noindexed filter pages could still be told its site is
     hidden, which costs 60 points.
   - Twelve faults in the technical texts, confirmed by the plain words' review (the list is in
     `reviews\plain-words-review.json` and `plain-words\notes.md` in the handoff folder).
   - Three behaviours: `charset-missing` ignores a byte-order mark; `blocked-by-robots` reads as passed, where it
     should be skipped, when the crawl was told to ignore robots.txt; `canonical-to-redirect` gives no final URL.
6. **Three UI slices, briefed and not started**, to run at once after items 1 and 2 (they touch the shell those
   two change): `brief-urls-grid.md` (the URL grid to the IA: filter bar, columns and presets, header sort,
   Ctrl+F, a row menu on every grid, saved views), `brief-round3-ui.md` (the mode and its switch, the top bar's
   order of giving way, Settings, New crawl in both modes, the crawl's settings, and the frame round 4 plugs
   Simple's screens into), `brief-e3c-takeup.md` (the UI takes up E3c's fields and the engine's `fixFirst`, and
   three of the real shell's findings).
7. **Not briefed yet:**
   - After E5 and E6 merge: export on every grid (Ctrl+E, the save dialog, the progress toast), "The report…" and
     "Spreadsheet of everything…", the palette's search, favicons, Settings > Analysis, the inspector's exact links
     to a sitemap and a robots.txt line, "Show these pages in URLs" from Links. Two notes from E5: the `export`
     event isn't in the app's `SidecarEvent` union yet, and a `srcdoc` frame for the report inherits the app's
     CSP, so that policy must allow the report's inline style sheet.
   - Round 4, on the frame `brief-round3-ui.md` builds, three agents at once: the Simple shell, Home and "checking
     a site"; the Summary, the score's breakdown, What to fix and one problem; the Report screen in both modes,
     the finished toast, and the score on Advanced's Overview. The design's "What round 4 builds" is the list.
8. Re-shoot the screenshot gallery, then the closing audit in both modes (items 7 and 8 of the list below).

The machine sets the pace: with four heavy agents and a verify run it was at 50 of 57 GB of committed memory and
90% of its processors, and timings went wrong. Run at most four heavy agents, and one verify run at a time.

### How each piece goes

1. A build agent.
2. A fresh-eyes review. A subagent can't write a file named `findings.md`: save its report yourself as
   `<name>-review\findings.md` for the fixer.
3. A fixer, which gives every fix a test seen failing without it, and then merges `ui/step-3` into its own branch,
   so the agent that knows the code resolves the conflicts.
4. The merge into `ui/step-3`.
5. The full checks, run again on the merge and not taken from the agent: `tools\verify-app.sh` and
   `tools\verify-engine.sh`. Read `summary.txt` line by line: a step that failed can hide behind later ones.
6. The push.

### Decided on 2026-11-11 under the owner's delegation

Each is the owner's to overturn.

- **The score:** notices cost nothing; a fault of the whole site costs 60 points; it can't pass 89 while a serious
  problem stands. It carries a version.
- **The switch** between Simple and Advanced is at the right of the top bar.
- **Simple's severity words:** Serious, Worth fixing and Worth a look, one to one with Error, Warning and Notice.
- **"Fix first":** a fault of the whole site leads its severity, in Advanced as well. The app still uses its own
  order until `brief-e3c-takeup.md` is built.
- **A check's share:** a page check is over the pages the analysis ran on; a URL check is over the URL count as it
  was at the analysis.
- **"Crawled" on Sitemaps** means the crawl requested the URL. URLs robots.txt refused have their own column.
- **Plain words** are new texts beside the technical ones. A limit in one is filled in from the analysis's
  settings. A check reported once for the site says so, where it would have said "1 of 265 pages".
- **The report** is one HTML file the engine makes, with an Excel workbook beside it.
- **An export path on a network share is refused**, as a crawl file's is.

### Known and open

- On a crawl of several hundred thousand pages a link pass takes over 2 s, the fold of the log gives up, and the
  log grows as it did before E3c's fix. Nothing at that size was measured.
- The inspector's link grids show one row at 200% zoom. Sitemaps' count columns start off-screen there.
- Nothing has run in the real Tauri window since the shell pass: the grids' least height (`contain: size`) and the
  hidden-window refresh are untested there.
- A real screen reader, anything at 1M URLs, and CI on this branch: not done.

Done on this branch, on top of `v2`:

- The grid readers, the inspector, live crawl data, and the app shell and front-end review fixes are merged.
- The client uses `POST /urls/inspect`, `/urls/inlinks`, `/urls/outlinks` and `/urls/source`.
- Near-duplicates use four SimHash lanes. `AnalysisFreshness.VERSION` is 6.
- The catalog has 86 checks. FAQPage no longer requires `mainEntity`. `es-419` is an invalid hreflang. JSON-LD is
  strict. A remote JobPosting needs `jobLocation` or `jobLocationType`. New check `html-past-crawl-limit`.
  `blocked-by-robots`, `description-missing` and `h1-missing` are Notices. `duplicate-site-version` is a Warning.
  Each check has `why` and `learnMore`, and `GET /api/checks` returns them.
- Inter and JetBrains Mono are bundled (roman 400, 500 and 600) in `app/src/styles/fonts/`.
- The forced-colours Tab test is green (merged `ui/forced-tab`, 2026-11-04). The app had no bug: the cached e2e crawl
  had an analysis by an older engine, so the URLs screen showed its "Analyse again" offer, a Tab stop between the
  search and the grid. The e2e setup (`startOnCurrentCrawl` in `app/scripts/lib/engine.mjs`) now remakes a stale
  cached crawl, or re-analyses the copy of a stale `SI_E2E_CRAWL`, and the test checks its precondition. 309 unit
  tests, 14 script tests, and the e2e suite (10 tests) passed twice in a row.
- The design system (merged `ui/design-system`, 2026-11-04): Lucide icons, Radix primitives, and the components in
  `app/src/ui/` in the clarity look, listed in the architecture doc's "Components". Each is shown in every state under
  "Design system/" in Storybook, and `npm run test:stories` runs axe over every story (`-- --forced` for forced
  colours). The shell, Home, URLs, Settings, feedback and the palette use them. On the merge: 354 unit tests, 59
  stories with no violations in either theme, e2e 13 of 13.
- The design system's fresh-eyes review found 25 defects, all fixed (merged `ui/ds-fixes`), each with a test seen to
  fail with its fix reverted. The worst: under forced colours, focus was invisible on anything selected. Also one Esc
  for a tooltip and the app, fields' clear reaching `onChange`, chip focus, ConfirmDialog errors, 200% zoom, reduced
  motion, cached registry lookups, and app messages in their own tone (not the severity marks). On the merge: 389
  unit tests, 14 script tests, stories clean in both themes and forced colours, e2e 27 of 27.
- The Issues screen (merged `ui/issues`, 2026-11-04):
  - **Checks list:** the checks by category, skipped checks always listed with their reason, passed behind a switch,
    hidden checks remembered with Undo.
  - **Selected check:** what's wrong, why it matters and how to fix it, with Learn more.
  - **Affected pages:** a paged, virtualised treegrid in `GridCore` (`tree`, `spanRow`, `sortable`), with the
    likely-template callout and "Show all N pages in URLs". A `none` check lists its URLs flat.
  - **Copy URLs**, the palette and keys (J/K, Ctrl+F, Ctrl+Shift+C/H), and every state in Storybook.
  - **Sidebar:** counts by severity, and it starts as icons below 1024 px.
  - **Inspector history:** one model for both screens, `routing/inspector.ts`.
  - **Review:** a fresh-eyes review found 23 defects, and 22 are fixed, each with a test seen to fail with its fix
    reverted. The one left is "pages affected" shown for checks that can flag non-pages: it needs an engine field,
    in E3b.
  - **Verification:** on the merge, 488 unit tests, 84 stories clean in both themes, forced colours and reduced motion,
    14 script tests, and e2e 36 of 36. Timings are inside the budgets on the garden and 100k crawls: switching checks
    takes 77–96 ms, opening a group 37–47 ms.
  - **Export** waits for E5.
- The Overview screen (merged `ui/overview`, 2026-11-10):
  - **Live:** counts, rate, time left when a URL limit is set, the found-and-crawled chart, the magnified status
    bar, alerts and waits. One clock: the time this run has spent crawling.
  - **Done:** the answer sentence, severity, Fix first, categories, skipped checks, status codes, indexability,
    depth and response times. Every number is a link. 220 of them were checked against where they land, on three
    crawls.
  - **Controls:** Pause, Resume and Stop stay in the top bar only, and the banners carry each state's next action.
    The owner was told and can overturn it.
  - **Review:** 18 defects, all fixed, each with a test seen failing without its fix.
- The URL inspector (merged `ui/inspector`, 2026-11-10):
  - **Sections:** Summary (the four indexability tests, values drawn on the engine's limits), Issues, Inlinks and
    Outlinks (each URL walks the inspector to it), Redirects, Headers, Structured data, Hreflang and Source
    (CodeMirror 6 with the app's own search).
  - **Palette:** "Go to URL…".
  - **Hidden checks:** a check the user has hidden stays listed for the URL, marked Hidden, out of the counts.
  - **Review:** 30 defects, all fixed the same way.
- Engine E3b (merged `ui/e3b`, 2026-11-10):
  - `GET /api/link-columns`, `POST /links` and `/links/facets`, `GET /sitemaps`, `POST /sitemaps/urls`,
    `POST /robots`, `/robots/file` and `/robots/blocked`.
  - `subject` (`page` or `url`) on every check, and `group` on each of the inspector's issues.
  - The analysis builds a link summary (4.5 MB on the 100k crawl). `AnalysisFreshness.VERSION` is still 6.
  - robots.txt has a new state, `unanswered`: no answer, and the crawl went ahead.
  - **Review:** 8 defects, 7 fixed in the engine and 1 in the merge. The worst: `/links/facets` answered 500 while a
    crawl ran. A running crawl's links reads now answer for one moment of the crawl, and a test reads every kind of
    query about 11,500 times while a crawl grows.
- After the merges (merged `ui/palette-handoff`, 2026-11-10):
  - A command chosen in the palette runs once the palette has given focus back, so it keeps the focus it takes.
  - The session's first Ctrl+K takes about 20 ms, not 320.
  - A grid's result count is no live region. It is said once, after the user changes the view.
- The real shell (merged `ui/shell-pass`, 2026-11-10):
  - **How:** the release build (`tauri build --no-bundle`, WebView2 157, 150% scale) with its own data folder,
    driven over WebView2's debugging port, with a Win32 helper for the window, the tray, native dialogs and keys.
    `npm run smoke:shell` repeats the walk: 14 steps in about a minute. It needs a Windows desktop session, so it
    isn't in CI. The architecture doc says how to run it.
  - **Works:** start, and the engine killed or slow to start; a crawl from Home with F7 and F8; URLs, the inspector
    and Issues; the palette, "Go to URL…" and the shortcut sheet; window size and place; the tray, with a crawl
    finishing while the window is hidden; "Stop crawling and quit?"; a second start; the OS theme changing;
    copying; "Open in the browser".
  - **Keys:** WebView2's own (F5, Ctrl+R, F3, Ctrl+G, Ctrl+J, Ctrl+P, F12, Shift+Esc) do nothing and still reach
    the page.
  - **Timings** on the 100k crawl are beside Chromium's in the architecture doc, all inside their budgets: the
    answer painted in 57 to 61 ms, a status class to its first rows in 51 to 66 ms, the inspector's summary in 18
    to 20 ms, the first Ctrl+K painted in 46 to 61 ms, and no frame over 33 ms through a live crawl.
  - **Fixed:** back from the tray after a crawl ended there, Overview failed with too many re-renders
    (`useAnalysisAnswer`).
  - **Found, still open:**
    - Home at 200% zoom loses the library's Site column and scrolls sideways.
    - The top bar's search field shrinks to one letter beside Pause and Stop at 1280 px.
    - Scroll bars are Windows' classic ones with arrows, which headless Chromium never shows.
    - Overview's breadcrumb crushes the date at 200% zoom.
    - Hidden in the tray, the shell and WebView2 hold 66 MB of working set where the doc says 23 MB. The two
      figures may not measure the same thing.
    - Not built: Ctrl+F on URLs (the IA's "Filter this grid"), a header sort and a row menu on the URL grid.
  - **Not checked:** real hardware keys and the physical screen (it ran in a disconnected remote session),
    notifications as shown, the tray icon's look, Pause and Resume from the tray menu, scales other than 150%,
    the `.sicrawl` file association and drag and drop.
- **Verification on the merges**, run again by the orchestrator:
  - App, on the shell merge `c709158`: 876 unit tests, 25 script tests, 167 stories clean in both themes, forced
    colours and reduced motion, and e2e 62 of 62.
  - Engine: 1,156 tests, the 100k bench (every planted issue, flat at 193 MB) and `readbench --gate`.
  - The gate failed once, under load, on four URL-grid sorts: 201 to 264 ms against 200. On a quieter machine the
    same reads took 26 to 35 ms with E3b and 25 to 33 ms without it, and the whole gate then passed.
- **Not verified by anyone yet:** a real screen reader, anything at 1M URLs, and the shell under a person's hands
  (the list above). CI has not run this branch.
- The clickable prototype is the app itself: `npm run dev` runs it against copies of the real crawls. A static mock
  build isn't published, as it can't be reviewed file by file.

Left, in this order. Where each stands is above.

1. Round 2: E3c is merged. Links, and Sitemaps and robots.txt, are fixed and not merged.
2. The UI takes up E3c's fields, and the shell pass's open findings are fixed: briefed, not started.
3. Done: the comparison.
4. Done: the Simple design, with its mockups published for the owner.
5. Round 3: the engine's score is merged; its export and E6 are part-built on their branches; the UI (the mode
   and its switch, Settings, New crawl, the URL grid) is briefed and not started; export and search in the UI are
   not briefed.
6. Round 4, Simple: the plain words are done and in the engine. No screen is built.
7. The screenshot gallery of the built app (36 views from the shell pass, private:
   https://claude.ai/artifact/NWJJ4cCWtaDor2FfkzdHsR) is re-shot and republished when the remaining screens land.
   Its builder is `gallery\build.cjs` in the 2026-11-10 scratchpad, and it is published from
   `C:\Projects\ShoutingIguana\.claude\worktrees\_gallery\site\`.
8. Before step 3 is called finished: a read-only audit of what is built against the IA, the Simple design and the
   UI brief (screens, the Keyboard table, the definition of done), in both modes.

After step 3 comes step 3b, the check audit, and then step 4.

Known and open: a links filter on the target with a link's position takes 565 ms half way down a big crawl, and
nothing is measured at 1M URLs.

Never edit the files in `%LOCALAPPDATA%\ShoutingIguana\dev-crawls`.

The catalog branch was verified before its merge: 1,104 tests, and the 100k bench found every planted issue. The
merge itself was clean. CI on `v2` does not run this branch.

## Status

As of 2026-10-31:

- **Real site.** `www.gardenfurnituresets.co.uk` (behind Cloudflare) was crawled at 2 requests/s on 2 connections:
  4,521 URLs in 37 minutes, never blocked. An audit checked every issue it reported against the stored HTML of all 265
  pages. 17 of the 20 issues it reported were right. The rest are fixed or documented below:
  - duplicate H1s within a paginated series;
  - images sized by CSS;
  - one-off slow responses.
- **Reviews.** Three rounds of parallel reviews covered storage, rules, fetching, and the CLI and tests. They found
  about 60 defects, then about 35, then about 20. Every one is fixed, except those listed under
  [Known limitations](#known-limitations). Each fix has a test that was seen to fail with the fix reverted.
- **Legacy app.** The old app's link-queue fix shipped as 1.0.4 from `master`.

## Decisions (and why)

- **C# / .NET 10 for the engine.** Official .NET SDKs exist for every integration on the roadmap (Search
  Console, GA4, Google Ads, Microsoft Advertising). The old app's problems were design, not language.
- **Cross-platform.** The engine targets `net10.0` and CI tests on Windows, macOS and Linux. The UI shell is Tauri
  for the same reason.
- **The NuGet plugin marketplace is gone.** It never worked (plugins never loaded) and the owner agreed. Checks are
  built-in rules in `engine/src/ShoutingIguana.Engine/Analysis/Rules`.
- **HTTP-first fetching.** Each page is parsed once into `PageFacts`. A browser is used only where needed, in step 4.
- **Blocked sites are reported, not hidden.** The crawler uses:
  - realistic headers;
  - a polite rate with automatic backoff;
  - explicit `Blocked` detection (Cloudflare, Akamai, DataDome...);
  - custom headers, so a site's firewall can allowlist it.

  There will be no proxy rotation or fingerprint spoofing.
- **`www` and the bare domain are one site.** Both are internal, and they share one rate limit and connection limit.
  Sites serve one and redirect the other, and separate limits would double the load on the server. The owner confirmed
  this on 2026-11-01.
  - Crawling them as one site stays. A version that answers instead of redirecting to the main one is
    `duplicate-site-version`, a Warning: Google treats the versions as duplicate URLs and picks one, preferring
    HTTPS. Probes aren't parsed, so a canonical on the other version isn't seen.
  - Nothing links to the other versions as a rule, so the crawler probes each one at the start of a crawl: a status
    check of the start URL's path there, never parsed or followed, so the site is never crawled twice.
  - A dead version isn't flagged, and neither are its unreachable URLs or robots.txt: a dead `www` twin is normal.
  - A probe never holds up a crawl: one try, none at the end, and none at all where its origin's robots.txt timed
    out. A version behind a firewall that drops packets then costs one robots.txt cycle, not three minutes.
- **A site that stops answering stops the crawl, which can be resumed.**
  - The crawler pauses a minute between tries, and after 10 minutes without an answer it stops: status Stopped,
    and `si crawl` exits 3.
  - `--resume` continues from there.
  - Whether something answers is judged per origin, with its robots.txt as the probe. So one part of the site
    going quiet (a dead `www` twin, a closed port 80, a section that never answers) doesn't stop a healthy crawl.
  - An origin that has answered before is waited out, never written off. Only one that has never answered can be
    written off, and only while the site's last answering origin still answers. A start URL that only redirects
    (`http://` to `https://`) therefore can't make the real site look dead.
  - Time spent paused doesn't count toward the 10 minutes.
  - A URL the crawler gave up on without asking is "Not checked", never broken.
- **A check that can't be accurate is skipped, not reported clean.**
  - The orphan and sitemap coverage checks need a complete crawl and readable sitemaps.
  - The external-link, image and sitemap checks need what the crawl options may switch off.
  - When a crawl doesn't support a check, it is left out of the results with nothing stored for it, and a note says
    why.
  - Not checked is not the same as 0, and the UI must show the difference.
- **A robots.txt that gets no answer at all blocks only the start URL's origin.**
  - RFC 9309 says to assume a full disallow when robots.txt can't be fetched, and the crawler does that for a 5xx or
    a 429 anywhere.
  - On any other origin with no answer at all (another site, a dead `www` twin, a closed port 80), the URL is fetched
    anyway. An origin that can't answer robots.txt almost never answers anything else, and fetching shows the real
    network error. Without it, a dead link would be reported as "blocked by robots.txt".
- **Crawl files hold no secrets**, since people share them. Header values and proxy credentials aren't stored, and
  resuming a crawl asks for them again.
- **One SQLite file per crawl (`.sicrawl`).**
  - Only `Storage/` writes to it: `CrawlWriter` during a crawl, `IssueWriter` for analysis.
  - Memory stays flat because the frontier, results and links live on disk.
- **Analysis runs after the crawl, from stored data.** Any crawl can be re-analysed with new rules or thresholds
  without recrawling. Issue ids are stored in crawl files, so they never change.
- **Analysis judges what search engines will see.**
  - An instant meta refresh is a redirect.
  - A canonical whose target leads back to the page, or nowhere, leaves the page indexable. A broken canonical is
    ignored, and a redirect is the stronger signal.
  - A sitemap may list URLs on its own host, the start URL's host and the home page's host. A `www` sitemap listing
    `www` pages is fine on a crawl that starts at the bare domain.
  - A link is named the way a screen reader names it: an image's alt text, a labelled icon, `aria-labelledby`,
    `aria-label` or `title`.
  - The pages of a paginated series (`rel=next/prev`) may share an H1, but each still needs its own title and
    description.
  - The crawl's own record stays as fetched. The analysis stores where it differs in `effective_indexability`,
    which the stats and the UI read.
- **The proof is exact.**
  - The synthetic site (`engine/src/ShoutingIguana.TestSite`) is clean by construction: problems appear only where
    they are planted.
  - The e2e test and the 100k benchmark require the exact URLs for every check, so extra flags fail as well as
    misses.
  - Every new check needs a planted case.
  - The one allowance is for response times, which depend on the machine. Up to 5% of pages (at least 25) may be
    flagged slow besides the planted one. The e2e test also checks the stored times directly, so the crawler's own
    timing can't drift unnoticed.
- **Thresholds follow common SEO tool defaults.** For example: titles 30–60 characters, descriptions 70–155,
  200 words, images over 200 KB. All are configurable in `AnalysisSettings`. Googlebot's 2 MB HTML limit is not:
  it is Google's, named in the check, and not a setting.
- **Check severities (November 2026).** Severity is part of the checks fingerprint, so analyses from before these
  changes are out of date. That is intended.
  - `blocked-by-robots` is a Notice, not a Warning. Blocking is usually deliberate (baskets, internal search,
    filters). Search Console lists it as excluded, not an error, the same as `noindex-page`.
  - `description-missing` is a Notice. Google writes most snippets from the page, and a missing description has no
    documented ranking effect.
  - `h1-missing` is a Notice. Google says it rarely relies on headings' HTML semantics; the gap is mainly
    accessibility.
  - `duplicate-site-version` is a Warning, not an Error. Nothing is broken: Google treats the versions as duplicate
    URLs and picks one, preferring HTTPS. Probes aren't parsed, so the check can't see a canonical on the other
    version and can't tell a harmless copy from a harmful one.
  - `meta-refresh` stays a Warning. One severity per id. Google treats an instant refresh as a permanent redirect,
    but a server redirect is what Google recommends, and every client follows it.
- **Simple first, Advanced on a switch (2026-11-10).** The owner wants an SEO tool "super easy for anyone to
  understand", with the detail one switch away for a technical SEO. That replaces the 2026-11-01 aim, an agency's
  tool, as the first face of the app. The screens built for that aim stay, as Advanced. The plan is
  [Simple first](#simple-first-advanced-on-a-switch-2026-11-10).

## Handoff to step 3 (the UI)

The engine is a library, and the CLI is a thin layer over it. The UI needs the same thin layer as a localhost API.
What the UI must be like is in [the UI brief](v2-ui-brief.md). This section covers what the engine gives it.

Work on a branch off `v2` (for example `ui/step-3`) and merge `v2` in often. Engine changes keep landing on `v2`
meanwhile, starting with the check for duplicate site versions (bare domain or `www`, http or https).

### What exists

Everything below is public in `ShoutingIguana.Engine`.

| Need | API |
| --- | --- |
| Start, pause, resume or stop a crawl | `new CrawlEngine(loggerFactory).Start(new CrawlRequest(options, path, Resume), observer)` returns a `CrawlJob`: `Pause()`, `Resume()`, `StopAsync()`, `Progress`, and `Completion` (a `CrawlSummary`) |
| Live progress and warnings | `ICrawlObserver.OnProgress(CrawlProgress)` every 500 ms. `OnNotice(CrawlNotice)` carries a stable `Code` to key on, such as `blocked`, `site-down` or `robots-unreachable`; the message text may change |
| Crawl settings | `CrawlOptions` and its `Validate()`. `CrawlDatabase.ReadOptions(path)` returns a stored crawl's settings, secrets removed (`HasRedactedProxyCredentials`; header names are kept, values aren't) |
| Analyse | `new CrawlAnalyzer(loggerFactory).AnalyzeAsync(path, settings, cancellationToken)` returns an `AnalysisSummary`: counts per check, `IsPartial` and `Notes` |
| Issues | `IssueReader`: `ReadAnalysis`, `ReadCounts`, `ReadNotes`, `ReadIssues(path, id, limit, offset)` (paged) and `ReadAll` (streamed, for export). A check missing from `ReadCounts` wasn't run |
| What each check means | `IssueCatalog.All` and `Find(id)`: id, category, severity, title, description (what), why, fix and an optional learn-more link |
| Headline numbers | `CrawlStatsReader.Read(path)` |
| Anything else | `CrawlDatabase.OpenReader(path)` is a read-only connection. `BlobCodec` decodes `response_details` (headers and page facts) and `response_html`. The tables are defined in `Storage/CrawlSchema.cs` |

### Rules the UI must keep

- **One writer at a time.**
  - A crawl or an analysis holds `<crawl file>-lock`. On Linux and macOS the file stays after the lock is released.
  - An analysis refuses a crawl that is running or was interrupted.
  - Readers are safe at any time, including during a crawl.
- **Show indexability as the analysis judged it**:
  `COALESCE(effective_indexability.indexability_reason, responses.indexability_reason)`.
- **Show skipped checks and the analysis notes.** A skipped check is not a 0.
- **Ask again for secrets on resume.** Header values and proxy credentials are not in the file.
- **Never write to a crawl file outside `Storage/`.**

### What's missing

1. **The sidecar.** An ASP.NET Core minimal API on localhost, started by the Tauri shell, with a random port and a
   per-launch token. It needs:
   - crawl control;
   - analysis;
   - the readers;
   - progress and notices, pushed over server-sent events.
2. **Readers for the grids.** These must be paged, filtered and sorted, and live in `Storage/`:
   - URLs: status, indexability, depth, type, size and time;
   - links in and out of a URL: anchor, position and flags;
   - one URL in detail: headers, facts, redirect chain, inlinks, issues and HTML;
   - sitemaps and robots.txt.

   Only issues are paged today. Anything else means SQL against `OpenReader`.
3. **Export.** Today only the CLI's issues CSV exists. Still to do: URL lists and Excel.
4. **Grouping.** A sitewide template fault is one row per page. On the real site, one logo link without text was 265
   rows of `empty-anchor-text`, which buried the 4 that differed. The grid should group a check's rows by detail and
   related URL.
5. **A list of crawls.** Today a crawl is just a file path.

## Open items and owner decisions

- **Proposed, not yet confirmed:**
  - crawl modes HTTP / Rendered / Auto (Auto samples raw against rendered HTML and switches per site);
  - raw-vs-rendered diffs as a feature;
  - a per-host browser fallback when a site blocks plain HTTP.
- **Decided this round under delegation, easy to revisit:**
  - stopping after 10 minutes without an answer;
  - the checks skipped on crawls that can't support them.

## Known limitations

- Near-duplicate detection estimates similarity from a 256-bit fingerprint. Pages 70% to 96% alike (a heavy shared
  template) can be flagged on a large enough site, and a bucket of pages sharing much of their text is searched within
  a bound, so a near copy in one could be missed (`engine/README.md`, Near duplicates). Crawls made by engines before
  the 256-bit fingerprint skip the check until crawled again.
- Links are capped at 10,000 per page, so pages linked only from a huge HTML sitemap can look orphaned.
- Some crawls skip the orphan check: stopped, depth-limited, URL-limited, folder-scoped, or with sitemaps off or none
  read. A declared sitemap that failed (other than with 404/410) skips the not-in-sitemap check. The analysis notes
  say why, and `si analyze` and `si issues` print them.
- A redirect chain is reported once, where it's entered. But if it ends at a broken URL, every hop is flagged
  `redirect-broken`.
- An hreflang alternate that redirects to a 404 is reported as non-indexable (it redirects), not as broken.
- Block pages served with status 200 (Imperva, the Sucuri JavaScript challenge) aren't recognised; their HTML is
  analysed as the page.
- robots.txt: a `Crawl-delay` line ends a run of `User-agent` lines, as a rule does. So `User-agent` lines after it
  start a new group.
- An external host that stops answering is written off for the rest of the crawl. Its remaining URLs are "Not
  checked", and links to them aren't reported as broken.
- On a depth-limited crawl, a sitemap URL that redirects has its target fetched, but not if that URL is also linked
  from beyond the limit.
- Two robots.txt checks, `robots-txt-unreachable` and `robots-txt-missing`, are proven by loopback crawl tests, not
  by plants on the synthetic site, whose own robots.txt has to work. `blocked-by-robots` and
  `robots-txt-unreachable-other-origin` are planted.
- `slow-response` reports a single measurement, so one stall during the crawl flags a page. The detail shows how much
  of the time was spent waiting for the first byte.
- `image-missing-dimensions` reads the HTML attributes. An image whose box CSS sizes is still flagged, as the
  crawler doesn't read the CSS.
- A sitemap is stored under the URL it was found at, not the URL it redirects to.
- Word counts for Chinese, Japanese and Thai count characters, which is close enough for thin-content checks.
- Title and description widths count full-width characters and emoji as two, an estimate of the room they take,
  not a pixel measurement.
- A version of the site that answers 200 is flagged `duplicate-site-version` even if its page has a canonical or an
  instant meta refresh pointing to the main site, because probes aren't parsed.
- `charset-missing` doesn't treat a byte-order mark as a charset declaration. W3C says a BOM wins over every other
  declaration, but the crawl stores decoded text and `PageFacts` doesn't record that a BOM was there. The check
  doesn't re-parse the HTML to look for one.
- Hreflang script subtags aren't checked against ISO 15924: any four letters pass. Rare enough that it wasn't added.
- A sitemap URL on a different protocol from the sitemap (an http URL in an https sitemap) isn't reported.
  sitemaps.org drops it; the check only compares hosts. Left on the backlog.

## How to verify

```bash
dotnet test engine/ShoutingIguana.Engine.slnx
dotnet run --project engine/src/ShoutingIguana.Cli -c Release -- bench --pages 100000 --concurrency 16 \
  --max-peak-mb 400 --max-growth-mb 100 --min-pages-per-sec 250
```

CI (`.github/workflows/engine.yml`) runs both on every push to `v2`.

Latest results:

| Run | Commit | Tests | Crawl | Peak memory | Analysis |
| --- | --- | --- | --- | --- | --- |
| Local, 32 cores, quiet machine | `68d55a2` | 417 engine + 16 CLI | 35 s (2,916 pages/s) | 168 MB | 7 s |
| GitHub runner, 4 cores | `530733b` | 464 + 16 on Windows, macOS and Linux | 91 s (1,114 pages/s) | 204 MB | 12 s |

Both runs found every planted issue and flagged nothing else, with no check skipped or failing. Runner speed varies:
on `68d55a2` the runner crawled in 76 s and analysed in 7 s, and the analysis does almost the same work on both
commits.

## Working with the owner

- Keep reports short and to the point: the answer first, no essays.
- The owner delegates freely ("get it done", "do whatever is needed next"). Pushing to `master` or releasing still
  needs an explicit yes.
- `src/` is legacy: critical fixes only. New work goes in `engine/` (and `app/` for the UI).
- The owner's bar for the UI is high: show them mockups and a prototype, and get a sign-off before building each
  screen ([the UI brief](v2-ui-brief.md)).
