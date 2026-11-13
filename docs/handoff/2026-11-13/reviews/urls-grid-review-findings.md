# URL grid review (`ui/urls-grid` @ 925a4e4): findings

Saved by the orchestrator from the reviewer's final message. Proofs in `proofs\` beside this file: the
`review-*.test.tsx` files fail on 925a4e4 (copy one back to its folder under `app/src` to run it), and
`settings-size.mjs`.

1. **High** - `grid/rowMenuState.ts:25-30`, `GridCore.tsx:596-611`, and each screen's
   `menuRow = rows.getRow(rowMenu.state.index)` (`UrlsScreen.tsx:373`, `LinksScreen.tsx:441`, `IssueRowsGrid.tsx:71`,
   `SitemapDetail.tsx:131`, `RobotsFileView.tsx:489`, `IssueGroups.tsx:458`). The menu remembers row numbers, not
   rows: `menuRow`, `menu.rows()` and `menu.copy()` read whatever row is at that number when an item is pressed.
   During a crawl, sorted by URL, right-click `/2`; a new URL sorts in above it; the menu's name becomes "Menu for
   /1" and Copy, Open, Inspect and "Show its inlinks" act on `/1`; a selection's copy takes the wrong rows too.
   CONFIRMED (`review-rowMenuStale.test.tsx`). Fix: when the menu opens, keep the rows (or their keys and values) in
   its state and act on those.

2. **Medium** - `UrlsScreen.tsx:517-518` (`chooseFacet`). Choosing a facet value removes any filter already on that
   column; for ranged facets (Response time, Issues) the number shown is then not where the click lands. Garden:
   filter "Warnings is at least 2", the Issues menu shows "With warnings, 5 URLs", choosing it gives 73; with
   "Response time > 300 ms", the 250-499 ms bucket shows 9 and lands on 23. CONFIRMED. Fix: add the facet's filter
   alongside the existing one; replace only when the old filter is a list of values the chosen value is part of.

3. **Medium** - `grid/savedViews.ts:30`, `settings/schema.ts` (`readSavedViews`), `src-tauri/src/settings.rs:58` and
   `:82`. Views within the UI's limits (24 x 1,500 characters) can push the preferences file past the shell's 64 KB:
   the UI counts characters not bytes, and the shell writes pretty-printed but checks size on reading. A: plain-text
   views, the write is accepted (38 KB) but the file is 75 KB, so on the next start the shell ignores it and every
   preference resets. B: Japanese text, the save is 67 KB, the shell refuses it, the error is swallowed
   (`settingsStore.ts:70`), and every later preference change fails to save. CONFIRMED (`settings-size.mjs`). Fix:
   budget the views as a whole in UTF-8 bytes (about 32 KB), have the shell write compact JSON or check the same
   size it accepts, and never swallow a failed preference write (say it, once).

4. **Medium** - `screens/links/presets.ts:90`, used at `UrlsScreen.tsx:217` and `:225`. `sortShown` drops the whole
   sort when any one key's column is hidden: sorted by Depth, Inlinks, Response time, hide Response time, and Depth
   and Inlinks are lost too. CONFIRMED (`review-urlsGrid.test.tsx`). Fix: keep the keys whose columns are still
   shown; drop the sort only when none are.

5. **Medium** - `LinksScreen.tsx:444` with `links/linkColumns.tsx:70`. The Links query asks for `from.url` and
   `to.url` only when shown, so the row menu has no URL when they're hidden: `?cols=from.url,kind,text`, right-click
   the anchor text, Open in the browser is disabled and Copy the URL fails. CONFIRMED
   (`review-linksMenuHiddenUrl.test.tsx`). Fix: always ask for `from.url` and `to.url` in `linkQueryColumns`.

6. **Medium** - `GridCore.tsx:660-676`. A right-click while the cursor is on a header leaves the cursor there: Up to
   the header, right-click row 3, close the menu, Enter sorts the column. CONFIRMED (`review-menuFromHeader.test.tsx`).
   Fix: `setHeaderCursor(null)` in `onContextMenuEvent`.

7. **Medium** - `GridCore.tsx:582`. Shift+F10 on a row drawn as one cell looks for the cursor's column, which that row
   doesn't have, so the menu opens at the window's top-left. Issues grouped, cursor on a group row's Share column,
   Down onto a page row, Shift+F10. CONFIRMED (`review-menuAnchorSpan.test.tsx`). Fix:
   `spans(effect.row) ? 0 : effect.col`, as `activeCellId` does.

8. **Low-medium** - `screens/urls/useSavedViews.ts` (Undo of delete) with the 24-view cap in `readSavedViews`. With
   24 saved: delete one, save a new one, Undo: the view just saved disappears silently. CONFIRMED
   (`review-urlsGrid.test.tsx`). Fix: when 24 are saved, refuse the Undo and say why.

9. **Low** - `screens/urls/urlColumns.tsx:236-261` (`CELL_VALUES`), used by cells and the Type facet. The grid
   ignores the engine's `valueTitles`, so its words differ from the export's ("Media" vs "Media file", "Other" vs
   "File", "Start URL" vs "The start URL", "Site version check" vs "A probe of the site's other versions"; the
   chip has a third: "a check of the site's versions"). CONFIRMED against `/api/url-columns`. Fix: use
   `column.valueTitles`, falling back to local words only where the engine gives none; one wording in cell, facet
   and chip.

10. **Low** - `urls/urlPlaces.ts:7` with `routing/search.ts:136`. "Show the page/URL in URLs" filters `url eq` the
    URL, but the route cuts text values at 1,000 characters: a longer URL lands on "No URLs match these filters".
    CONFIRMED (`review-oneUrlFilter.test.tsx`). Fix: past 1,000 characters, filter by the URL's id (if the engine
    has an id filter; if not, open the inspector on it and say so in the report).

11. **Low** - `grid/savedViews.ts:98-100` (`saveProblem`). The over-length message always blames the filters, even
    for a view with no filters, a 1,000-character search and 40 columns. CONFIRMED by reading. Fix: name what takes
    the room.

12. **Low** - `GridCore.tsx:582`. Shift+F10 with the cursor's row scrolled out of view measures the off-screen copy
    of the row, so the menu opens away from it. PLAUSIBLE (jsdom has no layout; prove it in Playwright). Fix: place
    the menu after the scroll to the row has rendered.

13. **Low** - `GridCore.tsx:660-676`. A right-click inside a multi-row selection doesn't move the cursor there; with
    the inspector open, the next arrow key moves from the old row. PLAUSIBLE. Fix: move the cursor to the clicked
    row and keep the selection.

14. **Low** - `settings/schema.ts` `readSettings`. An older build rebuilds the settings without fields it doesn't
    know, so going back to an older build deletes every saved view on its first write. PLAUSIBLE (downgrade only).
    Fix: carry unknown fields through when writing.

15. **Low** - `screens/issues/IssueGroups.tsx` (group-row menu). A group row's menu offers only "Show all N pages
    in URLs", with no copy items, unlike every other row menu. PLAUSIBLE (gap against brief item 5). Fix: add Copy
    the URLs and Copy the rows for the group's pages.

16. **Added by the orchestrator** (from the E3c take-up slice's report): the Links grid
    (`screens/links/linkColumns.tsx`) still says "No status" for a target with no status, where the inspector now
    says what happened to it from its `outcome` (Blocked by robots.txt, Not checked, Not fetched yet, ...). The
    E3c take-up branch, not yet merged, has the inspector's words for each outcome: when `ui/step-3` has it, use the
    same words through the same helper; until then, use `outcome` with the words the URL grid already gives a URL
    with no status, and say which in your report.

Looks right (the reviewer's note): "Show its inlinks in Links" lands exactly on garden (792, 652, 628, 540); values
with quotes, commas, `%` or non-ASCII survive the route; row keys are unique and the `rowKey` tests fail without
it; the sort sent matches the headers, `aria-sort` on the first key only; one page read per change, facets read
only with a menu open; the 10,000-row copy cap is honest with Cancel; the saved-view dialog returns focus; older
settings files are accepted; GridCore changes and Ctrl+F don't break Links, Issues, Sitemaps or robots.txt.
