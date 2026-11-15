# Brief: the last polish of step 3, found by the gallery in the real shell

Read `C:\Projects\ShoutingIguana\.claude\worktrees\_handoff\2026-11-11\ui-slice-rules.md` and
`C:\Projects\ShoutingIguana\CLAUDE.md` first. Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

Step 3 is merged and verified whole on `ui/step-3`. The gallery re-shoot on 2026-11-15, in the **release shell**,
found the list below. The owner's word is "fix everything". Several of these pass in Chromium and fail only in
WebView2 (the Report frame's scroll bar was "fixed" in round 5 and is still Windows' classic one in the release
build), so **every fix is proved in the real shell**: the release build, driven as `app/scripts/smoke-shell.mjs` and
`app/scripts/lib/shell.mjs` do, with a screenshot before and after in your folder. Where you can, add a step to
`smoke:shell` that fails without the fix.

The shots that show each one are in `C:\Projects\ShoutingIguana\.claude\worktrees\_gallery\site\shots\` (`<nn>-…-light.png`,
`-dark.png`); the gallery agent's notes and explore shots are in the session scratchpad's `gallery2\`.

## The list

1. **The top bar, Advanced, 1280 px.** With a crawl paused, the search field shrinks to an icon and its "Ctrl K" hint
   pokes out from under the Simple | Advanced switch (shot 32 dark). With a crawl running it folds the other way: Home
   and the date go, but the search box stays wide and empty (21). Make the bar give way in the order `v2-ui-ia.md`
   gives, every state (running, paused, stopping, analysing, none), 1280 and 1024 px and 200%, both themes; nothing
   overlaps, the hint goes with the field.
2. **The Report screen** (12, 31): the report frame shows Windows' classic scroll bar with arrow buttons in the
   release shell; the subtitle wraps with "read" alone on a line, and the toolbar wraps "Print or save as PDF" to a
   second row at 1280 px. Find why round 5's scroll-bar style doesn't reach the frame in WebView2 (a `srcdoc` frame's
   own document; `color-scheme`; WebView2's scroll-bar rendering; the engine's report CSS: engine allowed for the
   report's CSS only, and the saved report file must stay as the engine makes it if the style is screen-only).
3. **Settings › Updates** (37): says "Check the internet connection, or try again later" when GitHub answers 404
   because no release with `latest.json` exists yet. Tell the cases apart (no connection; GitHub answered but there
   is no update information yet; a bad signature; a newer version) in plain words, in both modes (shell allowed).
4. **The inspector's tabs** (27): at 1280 px 3½ of 9 show; the rest scroll sideways with no sign. Make every tab
   reachable and visibly so (an overflow menu, wrapping, or scroll affordances), keyboard included.
5. **Two numbers under one word** (18, 21): a crawl's card says Queued 92,111 ("5 on their way") and the bar under it
   Queued 92,116; the same on Overview live. One read, one number, or two words for two things.
6. **The Library and Simple Home** (18, 02): a site whose newer crawl is paused shows "Paused" beside its name and
   "Stopped" for the crawl in its row; that status cell ("Stopped. Analysed so far. Continue") takes four lines;
   Simple Home cuts the same row to "5,710 pages re…". Say the row's state once and truthfully, fit the column.
7. **Simple's words** (01, 42): Home asks "Have a crawl file already?" while Simple calls it a site check file; extend
   `simple/simpleWords.test.tsx` so it would have caught this.
8. **Simple's finished toast** (06): offers "See the summary" while the Summary is already on screen. Offer what's
   useful from where the user is.
9. **Minor**: Overview live's chart spans clock time including paused minutes (3 min 53 s) beside Elapsed 13 s (21):
   one clock, said which; Delete's question reads "Delete 127.0.0.1:49877, 15 Nov 2026, 01:42's crawl file?" (19):
   reword it plainly.
10. **The tray** shows plain "Shouting Iguana" for a few ms between a crawl's end and its analysis (the live view drops
    the crawl for that moment): keep the crawl in the live view across the hand-over (shell allowed).

## Where you work

Worktree `C:\Projects\ShoutingIguana\.claude\worktrees\r5-polish`, branch `ui/r5-polish`, cut from `ui/step-3` at
the head in your launch message (fixtures copied). Ports 61874-61877, Storybook 7606, DevTools 61878. Scratch folder
`C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\a539aa23-e856-44c1-bd21-ca484e8740e2\scratchpad\r5-polish\`
(make it with `tmp`; keep `checkpoint.md`). Ports 50000-50059, 50870-51825 and 56376-56475 are reserved by Windows.
Never push. You are the only agent running.

## Checks, all green and seen by you at the end

A test seen failing before each fix. Then on the final commit: both verify scripts from
`docs/handoff/2026-11-13/tools/` (`verify-engine.sh <repo> <logs>/engine`, `verify-app.sh <repo>/app <logs>/app
61874 7606`; the engine one only if you touched `engine/`), `cargo fmt --check`, `clippy -D warnings`, `cargo test`
if you touched `app/src-tauri/`, `e2e:filled`, the release build and `smoke:shell` (both walks), and the
before/after screenshots of each item in the release shell at 1280x720, both themes.

## Report back

Each item by number: the cause, the fix, the test seen failing then passing, the before/after screenshots' paths.
Commits. Every check with counts and exit codes. What was not verified.
