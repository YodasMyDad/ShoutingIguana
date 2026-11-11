# Rewrites "Where step 3 stands" for the pause of 2026-11-11. Run again with new branch states by editing STATE.
import io

path = r'C:\Projects\ShoutingIguana\.claude\worktrees\ui-step-3\docs\v2-rebuild.md'
text = io.open(path, encoding='utf-8', newline='').read()
newline = '\r\n' if '\r\n' in text else '\n'
text = text.replace('\r\n', '\n')

# Per-branch state at the pause. Edit these lines as the agents report, then run the script again.
STATE = {
    'sitemaps': 'At `fe51eb0`. All 17 of its review\'s findings are fixed, each with a test seen failing without it (unit 1,105, stories 245, e2e 86 on its own branch). `ui/step-3` is not merged into it yet',
    'e5': 'At `03877b6`, with `ui/step-3` merged in. Built: CSV and Excel export of every grid, the workbook, and the report on the real score, order and plain words, with `si export` and `si report`; the branch builds and its tests pass (1,259 engine, 219 sidecar, 23 CLI; 880 app unit tests). Not done: the 100k bench and the read gate, the report measured again on the 100k crawl, the printed pages looked at past page 1, the API doc for the last changes, and any review. Its `HANDOFF` is at the top of `e5/checkpoint.md`',
    'e6': 'At `87e3ff5`. Nine of its twelve items are built and tested (1,042 engine, 177 sidecar, 17 CLI): `POST /search` for the palette, link passes split over four connections, robots.txt origins searched and filtered, the inspector\'s exact links, `urls` beside `pages` on a check\'s groups, `linkTotals.hyperlinkInlinks`, `GET` and `PUT /api/analysis-settings`, a sitemap index\'s `listedSitemaps`, `respected` on `/robots/file`. Not started: the favicon. Part-done: "links to URLs" and a view\'s distinct ends (written, never applied or compiled: `e6/LinkEndsTests.cs.pending` and `e6/specs/link-ends-*.py`), and the docs and TypeScript types. Not run: the 100k bench, the read gate, anything in `app/`. No review. Its `HANDOFF`, item by item, is at the top of `e6/checkpoint.md`',
    'verified': 'the engine build and 1,353 tests (1,156 engine, 176 sidecar, 21 CLI), the 100k bench (every planted issue, flat memory), and the app\'s typecheck, lint, format, 877 unit tests, script tests, 167 stories in both themes, forced colours and reduced motion, and e2e 62 of 62',
    'verify_note': 'On the Links merge, the full app checks were being run again when this was written: their result is the last line of the PAUSE section of `handoff/2026-11-11/checkpoint.md`, or missing if the session ended first.',
}

heading_old = '## Where step 3 stands (2026-11-10)'
heading_new = '## Where step 3 stands (2026-11-11)'
start = text.index(heading_old) if heading_old in text else text.index(heading_new)
end = text.index('Done on this branch, on top of `v2`:')

section = heading_new + '''

Work on branch `ui/step-3`, worktree `.claude/worktrees/ui-step-3`, pushed to `origin/ui/step-3`. `v2` and `master`
were not moved. This section is the place to resume.

**Paused on 2026-11-11 by the owner, to save usage.** Step 3 is about two-thirds done. The owner asked for everyone
to finish where they had got to and for everything to be saved, so that it is easy to continue after their usage
resets. Nothing was launched after that, and nothing is running. Merging to `master` or releasing still needs an
explicit yes.

**Everything is on GitHub**: this branch, and every working branch under its own name (`ui/links`,
`ui/sitemaps-robots`, `ui/e5`, `ui/e6`, and the merged `ui/e3c`, `ui/score` and `ui/simple-design`). Work can go on
from any computer.

A new session needs this section and one folder in the repo: [`docs/handoff/2026-11-11/`](handoff/2026-11-11/). It
holds `checkpoint.md` (the session's log, with a PAUSE section at its end), every brief (`brief-*.md`),
`ui-slice-rules.md` (the rules every UI slice follows), `tools/verify-app.sh` and `tools/verify-engine.sh`, the
reviews' findings (`reviews/`), each agent's own checkpoint (`checkpoint-<name>.md`, and `e5/` and `e6/` with their
`HANDOFF` sections), and the plain words' notes. The files name paths on the first machine (a scratchpad under
`%LOCALAPPDATA%\\Temp\\claude`, and `C:\\Projects\\ShoutingIguana\\.claude\\worktrees\\...`): on another computer, read
those as "this folder" and "a worktree of that branch". Subagents can't be resumed across sessions: relaunch each
from its brief and its checkpoint.

### On another computer

- Clone, and check out `ui/step-3`. For a branch an agent works on, `git worktree add .claude/worktrees/<name>
  <branch>` keeps each agent in its own folder, as the briefs assume.
- It needs the .NET 10 SDK, Node with `npm ci` in `app/`, and Rust for the Tauri shell.
- Two things aren't in the repo. **The test crawls** live in `%LOCALAPPDATA%\\ShoutingIguana\\dev-crawls` on the
  first machine: a real crawl of the garden site and a 100k synthetic one, which `npm run dev` and several briefs
  use. The synthetic one can be made again with `si bench --pages 100000 --db <file>`; the garden one has to be
  copied across, or the site crawled again. **The design fixtures** (`app/design/data/fixtures`, git-ignored) are
  extracted from those crawls: `app/design/data/README.md` says how. The e2e suite needs neither: it crawls the
  engine's own test site.
- The owner's memory file is per machine. `CLAUDE.md` points a new session here.

### Merged on this branch on 2026-11-11

- **[The comparison](v2-comparison.md)** with Screaming Frog and Sitebulb (item 3).
- **The Simple design** (item 4): [`v2-ui-simple.md`](v2-ui-simple.md), small edits to the IA, the design doc and the
  glossary, and 18 mockups on the garden crawl in `app/design/simple/`. It ends in three lists the build rounds are
  briefed from: what round 3 builds, what round 4 builds, what the engine needs. The owner's gallery of the
  mockups is published, private: https://claude.ai/artifact/R53PTE7wZb7rPZHX6Z3GfN (built by
  `gallery-simple\\build.cjs` in the handoff folder, published from
  `C:\\Projects\\ShoutingIguana\\.claude\\worktrees\\_gallery\\simple\\`: republish from that path to keep the URL).
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
- **The Links screen** (`ui/links`, with its review's 14 fixes and four smaller ones, each with a test seen
  failing without it): every link in one grid, with presets, facets read only while a menu is open, a filter
  editor the engine judges, a column chooser, a whole-view copy and "All N in Links" from the inspector. Shared
  with URLs: the `GridScreen` frame and `minRows` (a grid keeps five rows at 200% zoom), no reads while the window
  is hidden. On Issues, a check's count is worded by its subject: a page check's share is "of the N pages
  analysed" and a URL check's "of the N URLs crawled", both from the analysis's own counts. `npm run dev` keeps a
  crawl's analysed copy while its original hasn't changed.
- **Verified on the score merge by the orchestrator:** ''' + STATE['verified'] + '''. ''' + STATE['verify_note'] + '''
  - The first run of these checks found two faults that every agent's own run had missed: the merged tree didn't
    compile its sidecar tests (the score's test used a helper the E3c fixes had removed), and one file came
    unformatted from the E3c fixes. Both are fixed. `verify-engine.sh` now stops when the build fails, where it had
    gone on to test the previous build.
  - `readbench --gate` failed on this machine while four other agents were running, each time in a different
    class and by a different margin (1,139 ms and 2,078 ms against gates of 200 and 550 in two links classes, then
    245.7 ms against 245 in a facets class), and each class passed when run again alone. Owed: one whole run of
    the gate with nothing else running.

### Built on their own branches, not merged

Each is pushed under its own name. None is merged here: Sitemaps needs a merge with real conflicts, and the two
engine branches are part-built and haven't been reviewed.

| Work | Branch and worktree | State at the pause |
| --- | --- | --- |
| Sitemaps and robots.txt | `ui/sitemaps-robots`, `sitemaps-robots` | ''' + STATE['sitemaps'] + ''' |
| Engine: export, the workbook and the report (E5) | `ui/e5`, `e5` | ''' + STATE['e5'] + ''' |
| Engine: fast search, the favicon, the limits' API, and what round 2 found the engine lacks (E6) | `ui/e6`, `e6` | ''' + STATE['e6'] + ''' |

### To resume, in this order

1. **Sitemaps and robots.txt.** An agent merges `ui/step-3` into `ui/sitemaps-robots` (expect conflicts in
   `GridCore.tsx`, `api/queries.ts`, `keyboard/registry.ts`, `e2e/overview.spec.ts`, `UrlsScreen.tsx` and both UI
   docs; `checkpoint-sitemaps-fixes.md` has its fixer's notes for this merge). 87 checks and "404 Not Found" may
   break tests that count 86 or match "HTTP 404". Its two screens take the Links slice's `GridScreen` frame and
   `minRows`. The list's Crawled and Blocked columns have only been seen against a mock: check them against the
   real sidecar, which now sends the two counts. Then merge, `verify-app.sh`, push.
2. **Export (E5).** Finish from its `HANDOFF`, then a fresh-eyes review of the whole branch (it has had none),
   fixes, the merge, both verify scripts, push.
3. **E6.** Finish its list from its `HANDOFF` (the favicon, "links to URLs", the types and docs, the measurements
   on a quiet machine), then review, fixes, merge `ui/step-3` in, the merge, both verify scripts, push. Two traps
   its agent left: the pending `bySource` edit to `Scan` must be redone by hand, and a sidecar test demands that
   every analysis setting has a described limit in `Sidecar/Options/AnalysisLimits.cs`.
4. **The catalog's fixes**, an engine job that isn't briefed yet:
   - `site-hidden-from-search` must also require the home page (or the start URL's page) to be noindex. As built
     it counts noindex pages only, and a shop with many noindexed filter pages could still be told its site is
     hidden, which costs 60 points.
   - Twelve faults in the technical texts, confirmed by the plain words' review (the list is in
     `reviews\\plain-words-review.json` and `plain-words\\notes.md` in the handoff folder).
   - Three behaviours: `charset-missing` ignores a byte-order mark; `blocked-by-robots` reads as passed, where it
     should be skipped, when the crawl was told to ignore robots.txt; `canonical-to-redirect` gives no final URL.
5. **Three UI slices, briefed and not started**, to run at once after item 1 (they touch the shell and the grid
   that Sitemaps changes): `brief-urls-grid.md` (the URL grid to the IA: filter bar, columns and presets, header sort,
   Ctrl+F, a row menu on every grid, saved views), `brief-round3-ui.md` (the mode and its switch, the top bar's
   order of giving way, Settings, New crawl in both modes, the crawl's settings, and the frame round 4 plugs
   Simple's screens into), `brief-e3c-takeup.md` (the UI takes up E3c's fields and the engine's `fixFirst`, and
   three of the real shell's findings).
6. **Not briefed yet:**
   - After E5 and E6 merge: export on every grid (Ctrl+E, the save dialog, the progress toast), "The report…" and
     "Spreadsheet of everything…", the palette's search, favicons, Settings > Analysis, the inspector's exact links
     to a sitemap and a robots.txt line, "Show these pages in URLs" from Links. Two notes from E5: the `export`
     event isn't in the app's `SidecarEvent` union yet, and a `srcdoc` frame for the report inherits the app's
     CSP, so that policy must allow the report's inline style sheet.
   - Round 4, on the frame `brief-round3-ui.md` builds, three agents at once: the Simple shell, Home and "checking
     a site"; the Summary, the score's breakdown, What to fix and one problem; the Report screen in both modes,
     the finished toast, and the score on Advanced's Overview. The design's "What round 4 builds" is the list.
7. Re-shoot the screenshot gallery, then the closing audit in both modes (items 7 and 8 of the list below).

The machine sets the pace: with four heavy agents and a verify run it was at 50 of 57 GB of committed memory and
90% of its processors, and timings went wrong. Run at most four heavy agents, and one verify run at a time.

### How each piece goes

1. A build agent.
2. A fresh-eyes review. A subagent can't write a file named `findings.md`: save its report yourself as
   `<name>-review\\findings.md` for the fixer.
3. A fixer, which gives every fix a test seen failing without it, and then merges `ui/step-3` into its own branch,
   so the agent that knows the code resolves the conflicts.
4. The merge into `ui/step-3`.
5. The full checks, run again on the merge and not taken from the agent: `tools\\verify-app.sh` and
   `tools\\verify-engine.sh`. Read `summary.txt` line by line: a step that failed can hide behind later ones.
6. The push.

### Decided on 2026-11-11 under the owner's delegation

Each is the owner's to overturn.

- **The score:** notices cost nothing; a fault of the whole site costs 60 points; it can't pass 89 while a serious
  problem stands. It carries a version.
- **The switch** between Simple and Advanced is at the right of the top bar.
- **Simple's severity words:** Serious, Worth fixing and Worth a look, one to one with Error, Warning and Notice.
- **"Fix first":** a fault of the whole site leads its severity, in Advanced as well. The app still uses its own
  order until `brief-e3c-takeup.md` is built.
- **A check's share:** a page check is over the pages the analysis ran on; a URL check is over the URLs crawled
  when the analysis ran.
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

'''
text = text[:start] + section + text[end:]

left_start = text.index('Left, in this order.')
left_end = text.index('After step 3 comes step 3b')
left = '''Left, in this order. Where each stands is above.

1. Round 2: E3c and Links are merged. Sitemaps and robots.txt is fixed and not merged.
2. The UI takes up E3c's fields, and the shell pass's open findings are fixed: briefed, not started.
3. Done: the comparison.
4. Done: the Simple design, with its mockups published for the owner.
5. Round 3: the engine's score is merged; its export and E6 are part-built on their branches; the UI (the mode
   and its switch, Settings, New crawl, the URL grid) is briefed and not started; export and search in the UI are
   not briefed.
6. Round 4, Simple: the plain words are done and in the engine. No screen is built.
7. The screenshot gallery of the built app (36 views from the shell pass, private:
   https://claude.ai/artifact/NWJJ4cCWtaDor2FfkzdHsR) is re-shot and republished when the remaining screens land.
   Its builder is `gallery\\build.cjs` in the 2026-11-10 scratchpad, and it is published from
   `C:\\Projects\\ShoutingIguana\\.claude\\worktrees\\_gallery\\site\\`.
8. Before step 3 is called finished: a read-only audit of what is built against the IA, the Simple design and the
   UI brief (screens, the Keyboard table, the definition of done), in both modes.

'''
text = text[:left_start] + left + text[left_end:]
text = text.replace('#where-step-3-stands-2026-11-10', '#where-step-3-stands-2026-11-11')
io.open(path, 'w', encoding='utf-8', newline='').write(text.replace('\n', newline))
print('ok', len(text.split('\n')), 'lines')
