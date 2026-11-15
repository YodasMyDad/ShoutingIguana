# Brief: polish 2, four small things the gallery found after the polish, and their shots

Read `C:\Projects\ShoutingIguana\.claude\worktrees\_handoff\2026-11-11\ui-slice-rules.md`, `C:\Projects\ShoutingIguana\CLAUDE.md`
and `docs/handoff/2026-11-15/brief-polish.md` (the round before this one, whose way of proving things in the release
shell holds here). Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. The owner's word is
"fix everything".

## The list (shots in `C:\Projects\ShoutingIguana\.claude\worktrees\_gallery\site\shots\`)

1. **The inspector's tabs** (27): the Inlinks tab is cut in half beside "7 more" (its count shows as "32" and fades),
   while "7 more" lists Inlinks among the hidden tabs. A tab is either whole in the strip or in the menu, never both
   and never cut; the menu lists exactly the tabs not shown.
2. **The sidebar at 1280x720** (32): with a crawl paused and an export running, the sidebar's content is 727 px in a
   720 px window and Settings sits on its bottom edge. Every combination of the sidebar's live sections (running,
   paused, analysing, exporting; one and several) fits at 1280x720 and 200%, or scrolls inside the sidebar with
   Settings still whole.
3. **The Library's badge** (18): "Newer crawl paused" leaves room for only "http:…" of the site's address. The address
   stays readable at 1280 px with the badge.
4. **Simple Home's stopped row** (02): "Stopped early · 14,414 pages read" wraps with the second line starting at the
   dot. Wrap between the parts with the separator kept on the first line (or none at a break).

Each with a test seen failing first, proved in the release shell (`smoke:shell`'s way of driving it) with before and
after shots, and a `smoke:shell` step where the release shell is the only place it shows.

## Then the gallery

Re-shoot `02-simple-home`, `18-advanced-library-menu`, `27-advanced-inspector`, `32-advanced-export-toast` and
`42-window-drop-overlay` (its background still has the old "Have a crawl file already?" line) in the release build of
your final commit, as the gallery's builder does (`...\scratchpad\gallery3\`: `checkpoint.md`, `build.cjs`, the
shooting scripts; copy what you need into your own folder). Update their captions, the facts line (head and date) and
the "Seen in these shots" notes (only what is still wrong; say so plainly if nothing is), rebuild the page in place in
`C:\Projects\ShoutingIguana\.claude\worktrees\_gallery\site\` (under 15 MB, lossless compression as before), and run
its preview. Don't publish.

## Where you work

Worktree `C:\Projects\ShoutingIguana\.claude\worktrees\r5-polish2`, branch `ui/r5-polish2`, cut from `ui/step-3` at the
head in your launch message (fixtures copied). Ports 61874-61877, Storybook 7606, DevTools 61878. Scratch folder
`C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\a539aa23-e856-44c1-bd21-ca484e8740e2\scratchpad\r5-polish2\`
(make it with `tmp`; keep `checkpoint.md`). Ports 50000-50059, 50870-51825 and 56376-56475 are reserved by Windows.
Never push. You are the only agent running. Only copies of the garden crawl and the synthetic test site.

## Checks at the end, seen green by you

`verify-app.sh` from `docs/handoff/2026-11-13/tools/` on your final commit (`<repo>/app <logs> 61874 7606`), cargo
fmt, clippy and test if you touched `app/src-tauri/`, `e2e:filled`, the release build and `smoke:shell` (both walks).

## Report back

Each item: cause, fix, test seen failing then passing, before/after shots. Commits. Every check with counts and exit
codes. The gallery: views re-shot, the page's size, the new facts line and notes, anything wrong you saw.
