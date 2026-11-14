# Round 5 review, R1 (the app: parts A, B, C and the joins): findings

`ui/r5-integrate` at `008b4b2`, diff `8ec31e6..008b4b2`. 12 findings, one high. Proofs in the session scratchpad
`a539aa23-…/scratchpad/r5-review-1/` (`proof-*.txt`, `proof-delete-retarget.test.tsx`, `shots/`).

1. High. `app/src/screens/home/LibraryTable.tsx:111` (with `CrawlMenu.tsx:34`, `:81-89`): the site's latest-crawl
   row has no `key={item.id}`, so an open Delete confirmation re-targets to whichever crawl becomes the site's latest.
   A crawl of a site runs; open "Delete file…" on that site's older crawl; the running one finishes and becomes the
   latest: the dialog names the new file and Delete file deletes it (`/api/crawls/e000000000000001/delete-file`).
   CONFIRMED (vitest). Simple's `SiteRow` (`SimpleHome.tsx:205`, keyed by site) has the same pattern from before
   round 5. Fix: `key={group.latest.id}` (Simple's too), or capture the item when the dialog opens.
2. Medium. `app/src/grid/valueFilter.ts:27`: "Exclude this value" on a boolean column filters `is !value`, which the
   engine compiles to `= 0/1`, dropping empty rows too (garden, Noindex "No": 0 of 6,286 instead of 6,021; `indexable`
   is 4,472 true, 30 false, 1,784 empty; Links' `followed` loses 7,151 empty rows). CONFIRMED. Fix: `any of: is
   !value, is empty` (the engine may need an OR or `ne`), and a unit test for `valueFilter.ts` (it has none).
3. Medium. `HomeScreen.tsx:153-157`, `:257`, `home/LibraryTable.tsx:189`: Home's heading over the running cards uses
   `isCrawling` (counts paused) and the row badge always says "Crawling now"; neither uses `runningTitle`. Pause the
   only crawl: Home says "Crawling now", the sidebar "1 paused". CONFIRMED. Fix: heading from `runningTitle`, the
   badge from the live crawl's status.
4. Medium. `home/RunningCrawlCard.tsx` (~44, 132-173): a crawl only being analysed gets the live-crawl card with no
   figures ("Analyse again" on 100k: Found –, Queued – "Waiting their turn", URLs a second –, an empty bar). CONFIRMED.
   Fix: an analysing variant: the library's figures, no queue, rate or bar.
5. Low. `grid/useColumnLayout.ts:65` and the header menu's Pin: pin/unpin moves the column but leaves the header
   cursor at the old index (Pin Depth, then Ctrl+Shift+Right moves Title). CONFIRMED. Fix: cursor to the new index, as
   `moveBy` does.
6. Low. `grid/GridCore.tsx:763-797`: a header press released off the header (no sideways move) leaves `headerDrag`
   set; hovering later turns "moving" on and draws the drop line with no button held; a click doesn't clear it.
   CONFIRMED. Fix: clear on a window-level pointerup, or check `event.buttons === 0` on move; always `setDropAt(null)`
   on pointerup.
7. Low. `grid/valueFilter.ts:79`: a status cell with no code and no outcome ("Not crawled") filters `statusCode
   isEmpty`, which also matches robots-blocked rows (100k: 2,337 incl. 11 "Blocked by robots.txt"; Exclude drops them
   too). CONFIRMED. Fix: `statusCode isEmpty` and `outcome isEmpty`.
8. Low. `home/RunningCrawlCard.tsx:125`: the card's "N on the site" (`internalFetched`) leaves out the site-version
   probe that Overview's Internal (`totals.ts:14`) counts (1,341 vs 1,342 on one crawl). CONFIRMED. Fix: count the
   probe as `v2-ui-ia.md`'s "URLs" says, or word it differently.
9. Low. `crawl/checkSubject.ts:179`: a check reported once for the whole site (`reportedOnce`, e.g. sitemap-error)
   reads "1 URL, <0.1% of the 1,409 URLs crawled" in Advanced; Simple says "Once, for the whole site" (gap 28's class).
   CONFIRMED. Fix: treat `reportedOnce` like `siteWide` in `checkReach`.
10. Low. The running card at 640x360: height 338 -> 438 -> 418 px in the first half second (mount, then -20 px when
    live figures arrive ~0.47 s later), moving the Library. CONFIRMED. Fix: reserve the status line and bar text from
    the first frame.
11. Low. `crawl/status.ts:106` (`canContinue`): Continue offered, enabled, for a crawl whose file is missing (ends in
    "The crawl file … isn't there any more."). CONFIRMED. Fix: `&& !item?.missing`.
12. Low. `IssuesScreen.tsx:266-270`, `CheckDetail.tsx:135`: "Show this check in the list" on a check hidden in every
    crawl un-hides it everywhere, with no Undo and no words saying so (`setCheckHidden(…, null)` clears both lists;
    the `scope === null` branch returns before `notifyUndo`). PLAUSIBLE. Fix: "Show in every crawl" (or "this crawl
    only"), and an Undo.

Also noted: the URLs grid's Issues column and its exports still count hidden checks (C reported it, nobody accepted
it); `UrlsScreen.tsx:893`, `LinksScreen.tsx:769`: the refusal message indexes `shown` while the query sends a sorted
column list (can name the wrong column briefly, before the column catalogue loads).

Looks right: stop -> analysis -> "Score so far" and the toast said again, both modes; Simple's "so far" Summary;
cancelling the analysis leaves "Work out the results" and no "Open the report"; hidden checks (one crawl and every
crawl) out of Home's rows, sidebar, Overview, Summary, What to fix, report; every Overview figure with a URLs link lands
on the grid's count (garden, a live test-site crawl); a bare address started on http; window titles with host and
port; axe clean on Home (paused card, rows, menu, Delete dialog), URLs header and menus, Issues, both themes; Simple's
text, names and title free of Advanced words on Home, New check, Summary, stopped Summary, Report, Settings; the
running card's Remove rescues focus, Undo restores, at most 4 updates a second. Not exercised: a busy refusal through
the UI (analyses after a stop took under 0.5 s), Ctrl+F in Source, saved views.
