# Brief: the UI takes up the engine's E3c fields, and three findings from the real shell (UI, round 3)

Read `ui-slice-rules.md` beside this file first: it holds the rules every UI slice follows and the defects earlier
reviews kept finding. This brief adds your slice. It is a small one: do it exactly, and prove each figure.

The engine stage E3c added fields the built screens asked for and don't use yet. Its contract is "Engine changes
E3c made" in `docs/v2-sidecar-api.md` (read all of it, and the per-figure filter table it added to "Progress").
You make the screens use them. You also fix three things the pass through the real Windows shell found.

## Where you work

- Worktree `C:\Projects\ShoutingIguana\.claude\worktrees\e3c-takeup`, branch `ui/e3c-takeup` (cut from `ui/step-3`
  with Links, Sitemaps and robots.txt merged). Ports: `SI_DEV_SERVER_PORT=62874 SI_DEV_SIDECAR_PORT=62875
  SI_PREVIEW_PORT=62876 SI_E2E_SIDECAR_PORT=62877`, `SI_STORYBOOK_PORT=7006`.
- Your scratch folder (all your own files, `tmp`, `checkpoint.md`):
  `C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\f209576d-c3b5-4cef-9d6c-2666276ff82b\scratchpad\e3c-takeup\`.
- Two other UI agents work in parallel, in `.claude/worktrees/round3-ui` (the shell, the top bar's layout, the
  mode switch, Settings, New crawl) and `.claude/worktrees/urls-grid` (the URL grid, the grids' shared pieces).
  Stay out of the top bar's layout, the router, Settings, New crawl, the grid screens and `app/src/grid/`. Where
  you must touch a file of theirs (the top bar's status pill takes a new number), change the one value and
  nothing around it.

## What to build

1. **Live Overview leaves out the probes of the site's other versions** (`siteVersions`), so a crawl of an
   http-only site no longer shows "Network error × 1" from its first seconds. Every live figure then lands on its
   count in the grid with the filters the contract's table gives for it: extend the test that walks them.
2. **"N URLs" is the same number everywhere.** Home's library, the top bar's status pill and the "Crawl stopped"
   toast use `crawledUrlCount` (and the live event's `crawled`), which is what the URL grid lists. Check every
   other place a URL count is shown against the grid on a crawl with URLs that weren't checked.
3. **The finished Overview says the time spent crawling** (`crawlingMs`: across every run, standing still while
   paused), in words that say which clock it is; a crawl an earlier engine made, where the time isn't known, says
   so and doesn't guess. **The found-and-crawled chart closes the gaps between runs** (`/history.runs[]`): a crawl
   continued a day later draws as crawling time, with each run's start marked, and a text alternative that says
   the same.
4. **The inspector uses `linkTotals`** (two fewer reads for each URL on every tick of a running crawl: show the
   request count before and after) **and `urlLength`** (the length the check measured, drawn on its limit as the
   other values are).
5. **A link's target says what happened to it** (`outcome`) where rows said "No status": inlink and outlink rows,
   a redirect chain's end, and canonical, meta refresh and hreflang targets, in the inspector's words for each
   outcome (queued while a crawl runs, never reached, refused by robots.txt, blocked, not checked, no response).
6. **Home at 200% zoom** (a 640 px page): the library keeps its Site column and the page doesn't scroll sideways.
   The shell pass's shot of it: `C:\Projects\ShoutingIguana\.claude\worktrees\_gallery\site\shots\37-home-zoom-200-dark.png`.
7. **Scroll bars** in the real shell are Windows' classic ones with arrow buttons (headless Chromium never shows
   them, so no test saw them); the inspector's tab strip shows one under its tabs. Style them from tokens
   (`scrollbar-width`, `scrollbar-color`, and the WebKit parts where WebView2 needs them), thin and quiet in both
   themes, leaving forced colours to the system; a strip of tabs scrolls without showing a bar. Prove it where it
   shows: `npm run smoke:shell` drives the real release build (the architecture doc says how; it needs this
   desktop session and takes about a minute plus the build), with before and after screenshots.
8. **The tray's memory figure.** The architecture doc says the app holds 23 MB hidden in the tray; the shell pass
   measured 66 MB of working set for the shell and WebView2. Find out what each number measured, measure both
   ways now, and make the doc say what is true.

9. **"Fix first" is the engine's order.** The analysis now serves `fixFirst` (the ordered check ids: severity,
   then a fault of the whole site, then how much is affected): see "The score" and "Analysis" in the API doc.
   Overview's Fix first, the first check Issues selects, and anything else that ranks checks take the engine's
   order, falling back to the app's own only for an analysis that doesn't send one (an earlier engine's). Checks
   the user has hidden are left out of the list the app shows, as now. Don't show the score itself: a later slice
   does. Also: Issues says "All 1 check passed." when all but one check is hidden: word the singular.

## Report back

As `ui-slice-rules.md` asks, plus, for each of the nine: what changed, its test seen failing without the change,
and the figures before and after (requests per tick for 4; the two memory measures for 8).
