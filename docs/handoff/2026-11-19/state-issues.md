# issues lane: state when paused, 2026-11-19

Paused on the coordinator's word (usage), mid-round. Nothing is running; `git status` is clean.

- Branch `ui/ux-issues`, worktree `C:/Projects/ShoutingIguana/.claude/worktrees/ux-issues`, base `2a2cc140`.
- Head: `8c918b47`. Never pushed.
- Scratch: `…/scratchpad/issues-fix/` (`checkpoint.md`, `data/` crawl copies, `flows/`, `shots/`, walk logs
  `g1-2560.log`, `g2-1280.log`). The scratchpad is a temp folder: a reboot can clear it.

## Commits

| Commit | What |
| --- | --- |
| `4b38b12b` | **Shared:** `app/src/ui/Url.tsx` (+ `Url.test.tsx`): `openUrl?: string \| undefined; openHint?: string \| undefined;`, the shape `fix-common.md` gives. |
| `5a52c0ee` | App: the Issues rework (list by default, links in the row, compact head, one scroller). |
| `8cb09623` | App: the screen's tests rewritten and new ones added; five product fixes they found. |
| `8c918b47` | App: the checks filter finds "404"; a "Since" chip stays pressed with the others beside it. |

Also outside my owned files, not in a `Shared:` commit (it is in `5a52c0ee`): `app/src/styles/components.css`, new
rules `.si-issue-url` and `.si-more-toggle`, added in the existing section "Where an issue's URL is found" (the Issues
screen's own styles), not in the URL styles block the data lane owns. The merge should expect it.

## Findings

| Id | State | Detail |
| --- | --- | --- |
| A-01 | **done** `5a52c0ee` | List columns "Linked from" (link + open button at `urlAtText`), "Link text", "Where on the page" (`IssueRowsGrid.tsx`, `ReferenceCells.tsx`, `referenceWords.ts`). The shared-page callout's page is a link, with "Show its N links in Links" (`CheckDetail.tsx`, `sharedLinksFilter`). |
| A-02 | **done** `5a52c0ee` | `groupColumns.ts` counts the comparison's two columns; `groupColumns.test.ts` (compare on/off, four frame widths). |
| A-03 | **done** `5a52c0ee` | Far-right `>` removed; a URL's text inspects; single click still selects; key legend under both tables (`issueGrid.ts` `ISSUE_KEY_HINTS`), left out in a window under 800 px tall. |
| A-04 | **done** `5a52c0ee` | Every URL in Issues goes through `IssueUrl.tsx` (`onGo` = inspect; box sized to its text so the open button is beside it; always visible at half strength by the CSS above). |
| A-05 | **done, one part differently** `5a52c0ee`, `8cb09623` | Sub-rows left-aligned with lead words (`targetLead`: "Links to", "Embeds", "Canonical to", "Alternate", "Loads"). "All N in the inspector" is now a link **"Show all N in Links"**, not the inspector on a tab: the inspector's tab isn't in the route (data lane's `routing/inspector.ts`). |
| A-06 | **done differently** `5a52c0ee` | List is the default (`checkView.ts` `defaultView`); grouped stays the default for image groups and a check with `templateCauses > 0`; a List / Grouped control switches; a route `group` shows the tree. A page with five broken links is a head row plus four rows under it in the same columns (each repeats the page so its open button lands on its own link); lists of up to 20 rows open them all (`rowsTree.ts`). **Not built:** several groups open at once in the tree (it is still one at a time; `groupTree.ts` would need rewriting): instead the first group opens on arrival when there are 20 or fewer. Group that is a URL: link + row menu Inspect / Open / Links (`IssuePageMenu.tsx`). |
| A-07 | **partly done** `5a52c0ee` | `issuesPlace.ts` keeps, per crawl and check for the session, the view, the list's rows opened or closed by hand, the cursor (row + key, restored through `GridCore`'s `startRow`/`startKey`) and the tree's expanded page. Unit tests cover the view and the toggled rows across leaving a check and coming back. **Missing:** the exact scroll offset (only the cursor's row comes back into view); never tried live (Back from Links); no e2e spec; the inspector's history entry is the data lane's. |
| A-08 | not mine | Engine lane (`app/src/changes/recheckWords.ts`). Untouched. |
| A-09 | **partly done** `5a52c0ee` | Head is one block: severity, title, counts line (`[data-counts]`, the count a link to URLs), actions; what's wrong / why / how side by side from `@min-checks-beside`, why and how folded behind a disclosure below it. "Groups" figure gone. `useShortWindow.ts` (under 800 px tall): no key legend, and with the check alone in view the page's sentences and notes hide. **Missing:** at 1280 × 720 the last walk still had the card scrolling on five busy checks (see "Live"); I changed the callout and column minimums after that and did not walk again. |
| A-10 | **partly done** `5a52c0ee` | The card is a flex column; the table's frame takes its rows' height and gives way to the pane (`leastFrameHeight`), list and tree alike. At 2560 × 1380 the walk found no scroller but the grid's on all 31 checks. At 1280 × 720: as A-09. |
| A-11 | **done** `8c918b47` | `checkList.ts` `matchesText` also matches description and id. Not done: matching loaded group details ("hreflang en-UK"). |
| A-12 | **done, tooltip unproven** `8c918b47` | Pressed/unpressed row, labels "New", "Fixed", "Fewer URLs", "More URLs", "Any change". Each passes a sentence as the button's `title` (`changeHint` in `ChecksList.tsx`); my test could not find the tooltip on focus in jsdom, so **whether it shows is not known**. |
| A-13 | **done** `5a52c0ee` | Chips gone; the strip is the one link. Not done: hiding the strip when the group already shows all its rows (the tree's row arithmetic assumes it). |
| A-14 | **done** `5a52c0ee` | `fitColumns.ts` (tested): columns take what their values need, the words give way before a figure; a long detail that is the same on every row is said once above the list. |
| A-15 | **partly done** | Done: "in N groups, by X" tail, "Names", "No other URL" → "No URL named", "Related URL" → named by what it holds (`groupUrlHeader`), a Type column that is the same on every row dropped, "Show it in URLs", "Copy its URL", "N of M checks with issues", "N causes to fix". **Not done:** "(4 points back)", the export option's words (not my file), "On 17 Nov" as a link (there is no compare route). |
| A-16 | **done** `5a52c0ee` | Pixels column hidden when no loaded group has pixels, with one line saying so. Thumbnails never seen (requests blocked in my flows). |
| A-17 | **done** `5a52c0ee` | "Inspect the linking page", "Open the linking page at the link" (exactly one), "Show its links in Links" on a page row. |
| A-18 | **partly done** `5a52c0ee` | The file's address is a link to its own screen (`FileCell`). **Missing:** the addresses inside the Detail sentence are still text. |

Done 12, partly done 5, not started 0 (A-08 is another lane's).

## Decisions, and why

- **List by default, not a tree with every group open**: `/rows` gives one row a flagged URL, and the tree holds one
  open group; a list with rows that open in place shows everything on arrival without rewriting the tree.
- **Five rows headed by the page** for a page with five broken links: each row has its own open-at-the-link button.
- **Session store, not the route**, for the view and open rows: the route's search is not my file.
- **A detail the same on every row** is said once only when it is 28 characters or more, so "404 Not Found" keeps its
  Status column, as the ruling's column list has it.

## What ran

- `npx tsc -b`: clean at `8c918b47`.
- `npx eslint` on `src/screens/issues`, `IssuesScreen.tsx`, `IssuesScreen.fixLoop.test.tsx`: clean at `8c918b47`.
  `npx eslint src/screens`: clean at `8cb09623`.
- Prettier: written on every file touched; `prettier --check src/screens` clean at `8cb09623`.
- Vitest, before `8cb09623`: `src/screens/IssuesScreen src/screens/issues src/styles src/ui/Url.test.tsx`, 592 passed,
  2 failed (one test, two projects: the wording "Show it in Links"); that file then rerun alone, 18 passed.
- Vitest at `8c918b47`: `IssuesScreen.fixLoop.test.tsx` and `issues/checkList*`, 104 passed.

**Not run:** `npm test` (whole suite), `npm run lint` (whole), `npm run format:check` (whole), the Issues set as a
whole after `8c918b47`, `test:stories`, every e2e spec, anything in `engine/`.

## Live, at the owner's size

Looked at with the lane's own sidecar and Vite, **at the code before `8cb09623`** (not looked at since):

- 2560 × 1380, garden crawl, `internal-client-error`: each row shows the linking page as a link, the link's words
  and "Content", with no expanding; the open button's target was `…/rattan-fire-pit-sets/#:~:text=deluxe%207%2Dpiece…`;
  a click on the page's text set `?inspect=15`; no horizontal overflow; the grid the only scroller.
  Shots: `…/scratchpad/issues-fix/shots/a01-404-arrive-2560.png`, `a01-404-table-2560.png`, `a01-404-inspect-2560.png`.
- 2560 × 1380, all 31 fired garden checks walked (`g1-2560.log`, shots `g1-2560-<check>.png`); I read the shots of
  `broken-internal-links` and `empty-anchor-text` only.
- 1280 × 720, same walk (`g2-1280.log`, shots `g2-1280-<check>.png`): rows visible on arrival on every check (1 to 6
  rows); the card still scrolled on `internal-client-error`, `empty-anchor-text`, `non-indexable-inlinks-only`,
  `heading-levels-skipped`, `image-missing-dimensions`; 6 px and 184 px of horizontal overflow on
  `internal-server-error` and `sitemap-non-indexable`. I read the `internal-client-error` shot only.
- Before shots are the audit's: `…/scratchpad/audit/shots/issues-*.png`.

**Not looked at:** the synthetic crawl; anything after `8cb09623`; the "Since" chips; Back from Links; dark theme.
My crawl ids differ from the audit's (they come from the file path): garden `33cc963bf4bbb5f2`, synthetic
`d2fb033f15041f6a`.

## Known or likely breakages

- `app/src/screens/IssuesScreen.stories.tsx` and the e2e specs that walk Issues were not updated or run: any that
  expect the tree on arrival, "Found on …" text, the stat tiles or the `>` button will fail.
- `docs/v2-ui-ia.md` and `docs/v2-evidence.md` still describe the old screen.
- `targetLineWords`, `targetsSummary`, `foundOnSummary` in `whereFound.ts` are now unused by Issues (kept: tested,
  and Simple may use them).

## Next five steps

1. Start the lane's sidecar and Vite, walk the garden crawl again at 2560 × 1380 and 1280 × 720 (`flows/walk.mjs`),
   then the synthetic crawl; fix the 1280 × 720 card scroll and overflow; take the after shots.
2. Run `npm test`, `npm run lint`, `npm run format:check`; fix what the whole suite finds.
3. Update `IssuesScreen.stories.tsx` and run `test:stories`; run and rewrite the e2e specs that walk Issues, and add
   one for the owner's journey (4xx row → linking page at the link; Back from Links keeps the place).
4. Finish A-07 (scroll offset; try Back live), A-18 (links in the Detail sentence), A-15's leftovers, and settle
   A-12's tooltip.
5. Update the docs, then write the lane's report as `fix-common.md` asks.
