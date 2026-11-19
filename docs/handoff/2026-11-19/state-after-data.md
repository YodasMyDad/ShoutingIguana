# Data lane: state after the finishing session, 2026-11-19

Stopped at the coordinator's word (usage). Nothing is running (the servers on 49894-49897 were killed by PID),
the worktree is clean, nothing was pushed.

- Branch `ui/ux-data`, worktree `C:/Projects/ShoutingIguana/.claude/worktrees/ux-data`.
- **Head `e254de30`**, 13 commits on `5922ad44` (`git log --oneline 5922ad44..ui/ux-data`).
- Shots: `shots-after/` beside this file (2560 x 1380 and 1280 x 720; `urls-after-*`, `t1/t4/t5/t10/t11-after-*`,
  `*-after-2560`, `*-after-1280`). Flows: `flows-after/` (`urls-tour.mjs`, `urls-tasks.mjs`, `data-tour.mjs`; `lib.mjs`
  holds this session's crawl ids, which depend on the data folder's path).

## Findings

| Id | State | What |
| --- | --- | --- |
| D-02 | **done** | URLs grid: the URL, Canonical, Redirects to and Final URL are links (`79ea93e9`; the last three look the URL up, a toast says when the crawl hasn't it); `si-open-on-row`; "Click a URL to inspect it" in every grid's foot. The Url width fault is fixed (`7f3d8ef6`: a sizer holds the shown text). |
| D-03 | done (before) | |
| D-04 | **done, part left** | `grid/columnFit.ts` + `GridCore` read `fit` (`url`, `text`, new `value`) (`3e65b127`, `d37e6f35`). URLs and one sitemap's grid use it; `urlColumnWidth` is gone. **Left:** Links, the sitemaps list, robots lists and the inspector's lists still compute their own widths as well (`fittedLinkWidths`, `URL_SHARE`…): now only the widths before rows are read where the columns have `fit`; the dead sharing code and its tests weren't deleted. The robots origins list still gives the origin all spare width. |
| D-05 | done (before) | |
| D-06 | **done** | `991206f0`, `f35ca8a0`; "Copy the rows of this view" on URLs (`db3bcaad`). |
| D-07 | done (before) | |
| D-08 | **done, part left** | The grid's highlight follows the walk (`followKey`), or is taken off the row left (`991206f0`). Left (engine): a status filter for lists over 1,000 links. |
| D-09 | **done** | Inlinks/outlinks/issues counts open the inspector's tab, Used on opens Links; row menu gains outlinks and issues (`79ea93e9`). Back returns to the row left (`db3bcaad`). |
| D-10 | **done, differently** | Indexability is tick-boxes counted among pages, "Not indexable (any reason)" first; choosing a value narrows to pages; Indexability and Content tabs bring "Type is page" + "On this site is yes" as chips (not a switch) (`4c977d65`, `79ea93e9`). "Not a page" is not a facet value: an image is simply never under Indexable now. |
| D-11 | **partly** | Content tab: Title, Description, H1 facets from the analysis's counts (2-3 clicks). **Not done:** Title sort and duplicates grouped side by side (the engine refuses a sort by text; needs an engine change), typing to find a column in Add filter, the help line. |
| D-12 | **done** | Redirects tab (3xx; Redirects to, kind, Final URL, final status, chain, inlinks). No hops column exists, so no sort by hops. The facet button now reads "Status class", as its chip does (the wider rename to "Status group" touched Overview and Links and was dropped). |
| D-13 | done (before) | |
| D-14 | **done for URLs** | "Clear all" (filters, search, sort; tooltip says so), search as a chip, the view starts on the row left (`db3bcaad`). **Links not done** (still "Clear filters", no search chip, no row kept). Exact scroll offset isn't kept, the row is brought into view. |
| D-15 | done (before) | |
| D-16 | **partly** | Done: one chip for the pages no sitemap lists, `linkEnds` words (`b4d08ff5`). Not done: engine ids in a filter refusal. |
| D-17 | **partly** | Columns give way by need at 1280 (URL 240+, values tight). Still: URLs Overview scrolls sideways about 60 px at 1280; Links' Kind and Target status columns stay fixed; the filter bar still wraps with the inspector open. |
| D-18 | **partly** (as before) | Its columns fit now and no longer leave 640 px blank; still no tabs, chooser or Add filter. |
| D-19 | done (before) | |
| D-20 | **mostly** | Done: URL text one size (12.1 px) everywhere, file name-first on every tab, double-click a column edge fits content. Not done: the export toast in another window (shell's). |

## What ran

- `npm run typecheck`, `npm run lint`, `npm run format:check`: clean at head.
- Whole Vitest (`npx vitest run --maxWorkers=4`) at `d37e6f35`: **528 files passed, 14 skipped; 5,751 tests passed, 189
  skipped, 0 failed.** After it only CSS (forced colours), e2e specs and docs changed; `src/styles` + `src/ui` rerun
  at head: 304 passed.
- E2e at head against a sidecar built in this worktree (`npm run build:sidecar`): `urls.spec urls-sweep urls-measure
  r5-grid links.spec links-grouped links-sweep inspector sitemaps.spec robots.spec sitemaps-robots.sweep found-on
  grid-zoom`: **108 passed, 2 skipped, 0 failed.** The first run had 13 failures; specs rewritten to the new behaviour
  (`e254de30` says which), and one real fault fixed (forced colours: the open button at 0.6, `04a4f584`).
- Live at 2560 x 1380 and 1280 x 720 on garden-b, synthetic: the flows above; shots read: URLs Overview, Redirects,
  Content + Title too long, Indexability menu, 404s + inspector at 2560; URLs and Links Broken at 1280.

## What did not run

- Every other e2e spec (`issues*`, `overview`, `states`, `export`, `evidence`, `handover*`, `home*`, `simple*`…). Ones
  likely to need the same edits: any that clicks a URL grid's **first gridcell** to select a row (it now opens the
  inspector: `issues-layout.spec.ts:23`, `issues.spec.ts:70,112,135`, `issues-subject.spec.ts:507`, `states.spec.ts:111`
  only if those grids pass `onGo`), and any that presses "Filter by status" (now "Filter by status class").
- `npm run test:stories`, `smoke:shell`, engine tests (no engine file touched).
- No stories were added for the new pieces (multi facet, search chip, fit columns).
- Links, Sitemaps, robots.txt and inspector screens were only measured (widths, scroll), not read shot by shot.

## Click counts (garden-b, 2560 x 1380)

| Task | Before | After |
| --- | --- | --- |
| 1a every 404 and who links | 2 + 2 a URL | 2, then 1 a URL (the Inlinks count) |
| 2 inlinks / outlinks of a URL | 2 each, hidden | 1 (the count) |
| 3 redirects and where they go | 3+ | 1 (Redirects tab) |
| 4 every non-indexable page | 8 x 3 | 2 (Indexability, Not indexable) |
| 5 missing / duplicate / long titles | 8, wrong rows | 3 (Content, Title, value); duplicates not side by side |
| 9 Shift+click, Ctrl+C | wrong text | header + rows, tab-separated |
| 10 clear all; Back | search and sort left; row lost | 1 press; row kept |
| 11 walk | grid marks the URL left | mark follows, or is taken off |

## Next steps

1. Run the unrun e2e specs and fix the first-gridcell clicks; run `test:stories`.
2. Links: `clearsAll` + `search` on its `FilterBar`, `rememberPlace` / `startRow` as `UrlsScreen` does.
3. Delete each screen's own width sharing and its tests; give Links' Kind/Position/Follow and Target status `fit: 'value'`.
4. D-11's rest needs the engine (sort by title within a narrowed view, or a grouped read).
5. Read the Links, Sitemaps, robots.txt and inspector shots at both sizes.

## Merge notes

- `Shared:` `d2d34fcf` `app/src/ui/Menu.tsx` (`MenuCheckboxItem` takes `detail`).
- `Url`'s props are unchanged (`openUrl` / `openHint` as agreed); its DOM gained `.si-url__sizer` and lost the class
  `si-url--file`; `.si-url__full` is now out of flow.
- `GRID_KEY_HINTS` starts with "Click a URL to inspect it": any test or story that reads a grid's foot sees it.
- `GridColumn.fit` gained `'value'`; `applyColumnLayout` clears `fit` on a column the user sized.
- `urlQueryColumns` always asks for `type`. `URL_PRESETS` has `redirects`, and `filters` on three presets.
- Likely conflicts: `app/src/styles/components.css`, `base.css`, `docs/v2-ui-ia.md`, `docs/v2-ui-architecture.md`,
  `app/e2e/found-on.spec.ts`, `r5-grid.spec.ts`, `screens/sitemaps/sitemapWords.ts` (the coverage link's filter).
- For the owner, as features: sort by title / duplicates grouped, a status filter on long link lists, URL
  include/exclude patterns, a per-URL new/gone comparison.
