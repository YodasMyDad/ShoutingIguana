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
| 3 | Desktop UI: React + TypeScript in a Tauri 2 shell, with the engine as a sidecar behind a token-protected localhost API. Enterprise-grade UX: the bar is in [the UI brief](v2-ui-brief.md). Since 2026-11-10 it opens in Simple, with Advanced one switch away, and ends in a report: [Simple first](#simple-first-advanced-on-a-switch-2026-11-10) | In progress on `ui/step-3`. Pick up at [Where step 3 stands](#where-step-3-stands-2026-11-10) |
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

The order, item by item, is "Left, in this order" under [Where step 3 stands](#where-step-3-stands-2026-11-10).

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

## Where step 3 stands (2026-11-10)

Work on branch `ui/step-3`, worktree `.claude/worktrees/ui-step-3`, pushed to `origin/ui/step-3`.
`v2` and `master` were not moved. The temp checkpoint under
`%LOCALAPPDATA%\Temp\claude\...\c67f90ea-...\scratchpad\checkpoint.md` is history. This section is the place to resume.

**Resumed on 2026-11-11.** The owner said: "Ok finish step 3". The work goes through "Left, in this order" below to
its end, without reports in between. Pausing is theirs to call. Merging to `master` or releasing still needs an
explicit yes. The checkpoint a new session resumes from, and every brief, are in the 2026-11-11 scratchpad folder
that the owner's memory file names (`checkpoint.md`, `brief-*.md`).

Done that day so far, and on this branch:

- [The comparison](v2-comparison.md) (item 3).
- **The Simple design (item 4)**: [`v2-ui-simple.md`](v2-ui-simple.md), with small edits to the IA, the design doc
  and the glossary, and 18 mockups on the garden crawl in `app/design/simple/` (merged `ui/simple-design`). It ends
  in three lists the build rounds are briefed from: what round 3 builds, what round 4 builds, what the engine
  needs. The owner's gallery of the mockups is published, private: https://claude.ai/artifact/R53PTE7wZb7rPZHX6Z3GfN
  (built by `gallery-simple\build.cjs` in that scratchpad, published from
  `C:\Projects\ShoutingIguana\.claude\worktrees\_gallery\simple\`: republish from that path to keep the URL).
  Decided there, and the owner's to overturn: notices cost the score nothing; a fault of the whole site costs 60
  points; the switch is at the right of the top bar. The design adds one check, `site-hidden-from-search`, so the
  catalog becomes 87.
- **The two reviews of round 2**: Links has 14 findings (1 high) and Sitemaps and robots.txt has 17 (2 high). Each
  list is saved as `<name>-review\findings.md` in the scratchpad.

- **Engine E3c is merged** (`ui/e3c`, with its review's 7 fixes, each with a test seen failing without it):
  - A running crawl's log is folded past 64 MB while a links grid reads it: 75 to 87 MB at its peak on a
    40,000-page crawl with one reader, against 106 to 160 MB before, and the crawl is no slower. A read may wait
    for a fold; none fails. `si bench --read-links --max-log-mb` gates it, and CI runs it. **Known limit:** on a
    crawl of several hundred thousand pages a link pass takes over 2 s, the fold gives up, and the log grows as
    before. Nothing at that size was measured.
  - The time spent crawling survives a first run that stored nothing, a clock set back and a machine that slept.
  - `GET /sitemaps` gives each sitemap's `urls.robotsDisallowed` and `urls.redirectingOrBroken`.
  - A kept `/stats` takes no read slot. `stats-pass` is gated at 450 ms and its miss of the 300 ms budget is a
    known limitation in the API doc.
  - Verified on the merge by the orchestrator: 1,004 engine, 166 sidecar and 17 CLI tests; the 100k bench (every
    planted issue, 32,410 issues on 110,264 URLs, flat at 199 MB); the app's typecheck, lint and 876 unit tests.
    `readbench --gate` failed twice on a machine running four other agents, each time in a different links class
    (1,139 ms and 2,078 ms against gates of 200 and 550), and both classes passed when run again (151 and 364 ms).
    The app's stories and e2e were not run on this merge alone: they run on the Links merge, which holds it.

Running, each on its own local branch, unmerged (if this is the newest news here, read the checkpoint, then each
branch's log, and relaunch what hadn't reported):

| Work | Branch and worktree | Brief |
| --- | --- | --- |
| Links fixer: 14 findings. A page check's share is over `pagesAnalyzed` | `ui/links`, `links` | `brief-links-fixes.md` |
| Sitemaps and robots.txt fixer: 17 findings. "Crawled" means requested, and URLs robots.txt refused get their own column | `ui/sitemaps-robots`, `sitemaps-robots` | `brief-sitemaps-fixes.md` |
| Round 3, engine: the score, `siteWide`, the new check, the plain-text fields, a code's name in details | `ui/score`, `score` | `brief-score.md` |
| Round 3, engine: export (E5), the workbook and the report | `ui/e5`, `e5` | `brief-e5.md` |

The order they merge in: E3c; Links (its fixer merges `ui/step-3` in first); Sitemaps (the same); the score; export
(it merges the score in before it builds the report's last part). Each merge gets the full checks, run again.

Queued behind the Links and Sitemaps merges, because they touch the shell both of those change:

- **The URL grid to the IA**: Ctrl+F, header sort, a row menu for every grid, the filter bar with Add, edit and
  Clear, facets, the column chooser and presets, "Show full URLs" and a whole-view copy. The Links review listed
  what URLs lacks, and the comparison names this as where Screaming Frog is plainly better for an expert.
- **The UI takes up E3c's fields**, with the shell pass's open findings (item 2).
- **Round 3's UI**: the mode setting and the switch, the top bar's order of giving way, Settings, New crawl in both
  modes, and export on every grid.
- **Round 3's second engine piece (E6 and the gaps round 2 found)**: full-text search, favicons, `POST /robots`
  search, `/urls/inspect` naming its sitemaps and robots.txt line, the URL grid filtered to the ends of links, a
  view's distinct ends, the `inlinks` gap, the parallel link pass.

It had been paused in round 2 on the evening of 2026-11-10, to preserve usage.

Overview, the URL inspector, engine E3b and the real shell pass are merged, verified and pushed (below). Three
pieces from `17b340d` are built, each on its own local branch. None is pushed, merged or through its review and
fixes, and their checks are the build agents' own, not run again:

| Work | Branch and worktree | State | Saved in the scratchpad |
| --- | --- | --- | --- |
| Engine E3c: what the slices found the engine lacks | `ui/e3c`, `.claude/worktrees/e3c` | Built to `4400814` (25 commits) and reviewed, with the E3b fix round: every number was right against the reviewer's own SQL, and it found one medium defect and six low. Not fixed yet | `e3c-review\findings.md` |
| The Links screen, the Issues "pages affected" wording, and `npm run dev` keeping analysed copies | `ui/links`, `.claude/worktrees/links` | Built to `2175872` (24 commits). No review yet | `links\report.md` |
| The Sitemaps and robots.txt screens | `ui/sitemaps-robots`, `.claude/worktrees/sitemaps-robots` | Built to `8d77343` (20 commits). No review yet | `sitemaps-robots\report.md` |

To resume:

1. E3c: a fixer from the review's findings, then the merge, both verify scripts and the push. The medium defect
   matters to Links: while a links grid reads a running crawl, the write-ahead log is never reset (160 to 220 MB on
   a 40k-page crawl, against 22 MB with no reader), so fix it before Links merges or with it.
2. Links: a review (model: `brief-overview-review.md`, plus the open points in `links\report.md`), a fixer, the
   merge, `verify-app.sh` and the push. Merge it before Sitemaps: it is the one that reshaped the grid's shared
   pieces.
3. Sitemaps and robots.txt: the same, merged onto Links. Both changed `app/src/grid/*` (`GridCore`), the registry,
   the mock transport, `errors.ts`, the test setup and the docs, so expect real conflicts.
4. Then "Left, in this order" below, from item 2.

What the two screens' builds reported, unchecked by anyone else:

- **Links:** 996 unit tests, 202 stories and e2e 81 of 81. On the 100k crawl a new search takes 0.94 to 1.87 s, a
  text filter 0.5 to 0.8 s and a filter on the source's depth 1.46 s. "Show the sources or targets in URLs" isn't
  built, as the URL grid has no filter for it. The Issues wording was written by a subagent and not read line by
  line: its review must settle which denominator a check's share uses.
- **Sitemaps and robots.txt:** 1,033 unit tests, 229 stories and e2e 84 of 84. Searching the origins for those that
  blocked something isn't built, as `POST /robots` can't filter. "Crawled" counts URLs robots.txt refused.
- **For round 3's engine brief:**
  - `POST /robots` has no search or filter.
  - `/urls/inspect` names no sitemap and no robots.txt line.
  - A sitemap has no count of redirecting or broken URLs.
  - The URL grid can't be filtered to the ends of a set of links.
  - `/issues/groups` calls its count `pages` for checks on URLs too.
  - The analysis's `inlinks` is 1 or 2 away from the stored links for about a tenth of URLs.
- Each agent, stopping its own story check, killed a story check that belonged to another worktree. Each run was
  made again. A brief should say: stop only your own process ids.

E3c added fields and changed none (the contract is "Engine changes E3c made" in `docs/v2-sidecar-api.md` on its
branch), so the UI still has to take them up once it is merged. Do that after Links and Sitemaps merge, as it
touches Overview and the inspector:

- Live Overview subtracts `siteVersions`, so an http-only site stops showing "Network error × 1".
- Home, the top bar and the "Crawl stopped" toast use `crawled` and `crawledUrlCount`.
- The finished screen says the time spent crawling (`crawlingMs`), and the chart closes the gaps between runs
  (`/history.runs[]`).
- The inspector uses `linkTotals` (two fewer reads per URL on every tick) and `urlLength`.
- Link rows, and canonical and hreflang targets, show the target's `outcome` where they said "No status".

The briefs are in the 2026-11-10 scratchpad folder that the owner's memory file names. So are `ui-slice-rules.md`
(the rules every UI slice follows, and the kinds of defect the first reviews found) and `checkpoint.md` (the log and
the plan).

How each piece goes:

1. A build agent.
2. A read-only, fresh-eyes review. A review agent can't write its report to a file, so save its findings as
   `<name>-review\findings.md` for the fixer.
3. A fixer, which gives every fix a test seen failing without it.
4. The merge into `ui/step-3`.
5. The full checks, run again on the merge and not taken from the agent: `tools\verify-app.sh` and
   `tools\verify-engine.sh` in the scratchpad folder.
6. The push.

E3c's review also covers the E3b fix round (`2985ece..1adf63e`). That round rewrote how a running crawl's links are
read, and has had no second review.

E3c's eight items:

1. `GET /stats` takes 1.1 to 1.4 s on the 100k crawl.
2. The live progress counts the crawler's probes of the site's other versions, so a crawl of an http-only site shows
   "Network error × 1" from its first seconds.
3. `fetchedUrlCount` includes URLs that weren't checked, so Home, the top bar and the "Crawl stopped" toast show a
   larger number than the grid.
4. Nothing gives the time spent crawling across runs, or where each run began and ended.
5. `/urls/inspect` counts hyperlinks only, and doesn't give the URL length the check measured.
6. Inlink and outlink rows carry no `outcome`.
7. A links text filter takes 0.33 to 0.55 s and a search 0.85 s on the 100k crawl. A table of distinct link texts
   would fix the filter, if it measures cheap.
8. Two reads the E3b fix round left: a filter most links pass on a crawl with gaps in its link ids (not measured),
   and a filter on the target with a link's position (565 ms half way down).

If the work stops before the agents report, to resume:

1. Read each branch's log, and the agent's own `checkpoint.md` (a folder per agent in the scratchpad: `e3c\`,
   `links\`, `sitemaps-robots\`, `shell-pass\`).
2. Relaunch an agent per branch from its brief and its checkpoint. Don't redo what a branch already committed.

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

Left, in this order. Items 3, 4 and 6 were added on 2026-11-10, and items 5 and 8 widened: the reasons are in
[Simple first](#simple-first-advanced-on-a-switch-2026-11-10).

1. Round 2, built and paused (above): E3c; Links, with the Issues "pages affected" wording; Sitemaps and
   robots.txt. Each still to be reviewed, fixed, merged and verified.
2. The UI takes up E3c's fields (the list above), and the shell pass's open findings are fixed.
3. Done on 2026-11-11: [the comparison](v2-comparison.md) with Screaming Frog and Sitebulb, from their own sites
   and screenshots. We cover 71 of Screaming Frog's 348 issues and 7 more in part; 100 of the rest need only what
   we already fetch (about 50 worth adding in the check audit), and 113 need a browser (step 4). For step 3 it
   asks for export on every grid, and for the URL grid to hold more: columns, header sort, a filter builder and
   compact rows. Its "For Simple mode" section is input to item 4.
4. Done on 2026-11-11: the Simple design, [`v2-ui-simple.md`](v2-ui-simple.md), with its mockups published for
   the owner (above). The work goes on under the standing delegation unless the owner steers it.
5. Round 3: New crawl settings, the crawl's settings and the rest of Settings (the Overview's alert actions then get
   their real targets); export (E5, engine and UI); full-text search and favicons (E6, engine and UI). All of it is
   built to the Simple design: the switch's home in Settings, New crawl in both modes, and an export that can carry
   the report. E3c left one engine idea for this round: run the pass over every link on several connections, which
   would bring text filters and search (0.3 to 1.4 s on 100k) down with no cost to the file or the analysis.
6. Round 4: Simple. The summary, what to fix in plain words, the report, the switch and the checks' plain words.
   Each piece goes the way every piece does (above).
7. The screenshot gallery is published for the owner as a private artifact: 36 views from the shell pass. Its
   builder and where it is published from are in the scratchpad's `checkpoint.md`. Re-shoot and republish it when
   the remaining screens land.
8. Before step 3 is called finished: a read-only audit of what is built against the IA and the UI brief (screens,
   the Keyboard table, the definition of done), in both modes.

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
