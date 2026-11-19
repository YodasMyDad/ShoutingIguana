# issues lane: state when stopped, 2026-11-19 (second session)

Stopped on the coordinator's word (usage), with one e2e failure unsettled. Nothing is running (ports 49884-49887 and
5966 free; the e2e `si.exe testsite` from this worktree was killed). `git status` is clean. Never pushed.

- Branch `ui/ux-issues`, worktree `C:/Projects/ShoutingIguana/.claude/worktrees/ux-issues`.
- Head `2b915000` (a `WIP:` commit: e2e specs only). `ui/step-3` is merged in (`6b65274a`).
- Scratch (temp, may be cleared): `…/848ba86b-…/scratchpad/issues/` (flows, shots, logs, `e2e-sub/run*.log`).
  Crawl ids in that copy: garden-b `15cad4540ac9d06a`, garden-a `78ad60558119213d`, synthetic `c05b13698a42a897`.

## Commits this session (oldest first)

`6b65274a` merge of ui/step-3 with items 1 and 2 and the one-scroller layout · `92bbb2fb` item 3 · `4b368007` A-07
scroll, A-15, A-18 · `c68fb6e1` the short pane's reading state, spare width to a long detail · `0b0da433`, `eda675d1`,
`b095c5c0` a URL's click gives the table the keyboard (real bug), IA doc, the owner's 404 spec · `a1bad3d1` text
fragment only for body links, "linked URL" · `3c3e4635` row menu test · `d7e6bdda` every address in a detail is a link
· `78134f4e` related checks fold after two · `2b915000` WIP e2e rewrite.

## The three fix-as-code items

| Item | State | Detail |
| --- | --- | --- |
| 1 fix near the top | **done** | `CheckDetail.tsx`: `<HowToFix>` in the head (`[data-explain]`). With code it takes the wider column (`@min-checks-beside:grid-cols-(--how-to-fix-columns)`, a container query at 1344 px, not the `wide:` viewport variant the block names), what's wrong and why one over the other beside it; words only stay three across. Narrow pane: folded whole behind the head's button, fix before why. Short window (<= 800 px tall): what's wrong folds too, and opened the explanation has the pane with the rows behind "Show the rows". `Explain` from the base is gone. |
| 2 "On this site" | **done** | `usePreferredFixCase.ts` reads the `issueTargets` already in the query cache (no request). Tests: image case selected and marked, targets asked for once; unmarked until read. Seen live on garden: icon case marked. |
| 3 one thing, not 260 | **done** | `TemplateCallout`: "1 link, to / in the header, repeated on 260 pages: one fix clears them." (URL is a link; no comma after it, the open button sits there). Pages under a template group say "The same link" (hyperlinks only; images keep how each page shows them). Ungrouped: "On 13 of the 14 pages analysed, so it's likely 1 cause in the site's template". |

## Findings

| Id | State | Detail |
| --- | --- | --- |
| A-01..A-06, A-11, A-13, A-14, A-16, A-17 | done (first session) | Unchanged, except A-14: a words column capped under its sentence takes spare width (`fitColumns` `want`). |
| A-07 | **done, one part not mine** | Scroll offset kept and restored (`useKeptScroll.ts`); live at 2560: scrolled 900, Links, Back, 900. In the 404 e2e spec. Opening the inspector still replaces history: data lane's `routing/inspector.ts`. Toggled rows across Back: unit-tested, not seen live (garden's 404s have none). |
| A-09 | **done** | See item 1. |
| A-10 | **done, two exceptions** | Card never scrolls; head `overflow-y-auto shrink`, rows keep `--check-rows` (10rem, 14.5rem with a template callout). Garden: 29 of 29 checks, both sizes, only the grid scrolls. Synthetic at 1280 x 720: `redirect-broken` head scrolls 12 px beside its rows (shared-source callout); `sitemap-non-indexable` 12 px sideways (9 columns). |
| A-12 | **done** | Tooltips seen live on hover and focus, each chip its own sentence. |
| A-15 | **done, one left** | "(+4 to the score)"; the change is a link to the check on the crawl compared with (`withCrawlId`); one-group checks don't offer "This group's pages". Left: the export option's own words (`export/offers.ts`, not mine). |
| A-18 | **done** | `DetailWords.tsx`, `detailParts.ts`: sitemap to Sitemaps, robots.txt to its screen, any other address looked up on click (`/urls/lookup`) then inspected, toast when the crawl hasn't it; the row's own file by name. |
| A-08 | not mine | Engine lane ("Recheck 1,000 of 225 URLs" still shows). |

Also fixed: a click on a URL's text left the grid without focus so arrows did nothing (`IssueUrl.tsx`); `openAtLink`
no longer adds `#:~:text=` for head links and image alt text; more than three "Fixing this also clears" lines fold.

## What ran, and results

- `tsc -b`: clean at `78134f4e`. `npm run lint`, `npm run format:check`: clean at `c68fb6e1`; eslint on
  `src/screens/issues`, `IssuesScreen*`, `e2e` clean after each later change.
- Whole Vitest, `--maxWorkers=4`, at `a1bad3d1`: 5,821 passed, 2 failed, 189 skipped (556 files). The 2 were one test
  in two projects, `rowMenus.moving.test.tsx > on a check's groups`: rewritten (`3c3e4635`), then 12 of 12 alone.
  **Not rerun whole since.** Issues set (`IssuesScreen*`, `issues/`, `styles`) at `a1bad3d1`: 612 passed;
  `IssuesScreen.test.tsx` at `78134f4e`: 138 passed.
- Stories: `build-storybook` ok, `test:stories --filter=issues` at about `3c3e4635`: 62 stories, 2 themes, no
  violations. `--forced`, `--reduced-motion`: not run.
- E2e (sidecar built from the merged engine, this worktree): first run of 8 Issues specs 22 passed, 19 failed (old
  tree expectations). After the rewrite: `issues`, `what-matters`, `issues-layout`, `issues-subject`, `found-on`,
  `evidence`, `fix-loop-advanced`, `counts-agree`: **42 passed, 0 failed** (run3, and again run4 after `a1bad3d1`).
  Wider set (run5): 136 passed, 3 failed, 1 skipped: `export.spec.ts:332`, `gallery6.spec.ts:137`,
  `inspector.spec.ts:244`. Last run (run7): 75 passed, **1 failed: `gallery6.spec.ts:137`** ("Error: the first row",
  the cursor-in-view check after its rewrite). Stopped there. **No e2e run since `d7e6bdda` and `78134f4e`.**
- Engine: not touched, not run.
- Live, garden-b copy, light and dark, 2560 x 1380 and 1280 x 720: the owner's list of checks shot and read
  (`shots/after-*`, `own-*`, `fold-*`). 404 task: **1 click** from the check to the linking page at the link (open
  button beside "Linked from"), no expanding; 1 click on its text inspects it. Shots predate `d7e6bdda`/`78134f4e`.

## Next steps, in order

1. `e2e/gallery6.spec.ts:137`: decide test or product (does Up from the last row bring the first row into view in
   the grid's own scroller at 1280 x 460?). Log: `e2e-sub/run7.log`.
2. Rerun the wider e2e set at head, then whole Vitest, lint, format check; drop "WIP" by a follow-up commit.
3. `smoke:shell`, `test:stories --forced` and `--reduced-motion`, `e2e:filled`: not run here.
4. Subagent's notes not acted on: specs pin nothing of the template callout (seeded crawl has no template cause).

## Merge notes

- `app/src/styles/tokens.css`: new `--check-rows`, `--check-rows-callout` beside `--explain-columns`.
- `app/src/ui/Url.tsx`: untouched this session (data's `openUrl`/`openHint` shape stands).
- `CheckCompare` gained `withCrawlId` (set in `IssuesScreen.tsx`).
- `app/e2e/support.ts`: new `showGroups(page)`; nine spec files changed, other lanes' specs among them (`export`,
  `inspector`, `gallery6`).
- Evidence columns drawn by `urlColumns.tsx` ("Final URL" of an external URL) are still not links: data lane's.
- `docs/v2-ui-ia.md` Issues bullets rewritten; `docs/v2-evidence.md` not touched.
