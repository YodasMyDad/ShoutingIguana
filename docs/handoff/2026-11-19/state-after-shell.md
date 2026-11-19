# UX fix round, shell lane: state after the finishing session (2026-11-19, session 848ba86b)

- Branch `ui/ux-shell`, worktree `C:/Projects/ShoutingIguana/.claude/worktrees/ux-shell`. Started at `1f2419f9`.
- Head: `505c7f94`. Nothing pushed. Tree clean. Every server stopped.
- Shots (2560 × 1380 are `w-*`, 1280 × 720 are `n-*`), flows and the edit/commit tools:
  `…/_handoff/2026-11-19/lanes/shell-fix/after/`.
- **Nothing under `app/src-tauri` was changed.** `npm run build:sidecar` wrote the git-ignored binary to
  `app/src-tauri/binaries/` (and `app/.sidecar-build/`), which is what the e2e specs and the drive ran against.
- `engine/` was not touched, so no engine tests ran.

## Findings

| Id | State | Commits | What |
| --- | --- | --- | --- |
| H-01 | done (earlier) | `142847f2`, `fbccb469` | |
| H-02 | done for scroll (earlier) | `5fd418b5`… | Left: the notes banner's open state across Back. The banner is gone from Overview where the card says it (H-11); where it still shows it reopens folded. |
| H-03 | done, differently (earlier) | `20f0fc99`, `ed2a4ea0` | Not built: landing in Simple on the inspected page's entry (the ruling doesn't ask for it). |
| H-04 | **done** | `37ccdb41`, `b484f6e5` (Shared), `1c893055` | "Crawl again…" is a control in the Not checked card, on a skipped check's page and in the notes banner, and a palette command. "Continue the crawl" was already a palette command. |
| H-05 | done (earlier), one fix now | `1f2419f9`, `a551ac62` | The stacked severity counts in a narrow library were targets under 24 px (axe, e2e): now 24 px. Left: the sort isn't remembered when Home is left. |
| H-06 | done (earlier) | `9c79cf75` | Per-URL new / gone / changed list: a feature, left for the owner. |
| H-07 | **done** | `770d616b`, `25bf2ec7`, `acd96aac`, `a84c8c97`, `95ebfce3` (Shared), `0e346dd3` (Shared) | The sitemap alert's figure is the link; the score sheet's notices count opens its list. Still plain text, on purpose: the comparison's "(4,360 URLs, done)", each moved check's "18 → 257 pages" beside its linked name, and Fix first's fault text beside its linked URL. |
| H-08 | **done, in part differently** | `1c893055`, `51ad92f8`, `fc7df812` | Palette: Crawl again…, Compare with…, Issues by severity, every Settings section and every New-crawl setting by their words, checks by status code, a line when nothing matches outside a crawl. Not built: "Recheck this check" and "Copy URLs" as app-wide commands (they are the Issues screen's, the issues lane's: "copy" finds Copy URLs only with a check open); scrolling the URL grid to the row a palette result inspects (the data lane's grid). "recheck" finds Crawl again…. |
| H-09 | **done** (the line); features left | `d0111cdd` | The engine runs no JavaScript (no browser in `engine/src`; rendering is step 4 in `docs/v2-rebuild.md`). New crawl says so beside Start. Include/exclude patterns, JavaScript rendering and a URL-list mode: for the owner. |
| H-10 | **done** | `1c893055` | "This crawl's settings"; the app's Settings keeps the crawl's switcher, sections and Ctrl+1…8 when opened from a crawl (`layout/contextCrawl.ts`, kept in sessionStorage). |
| H-11 | done (earlier), one fix now | `41a38cbc`, `37ccdb41`, `a551ac62` | The names in the card are 24 px targets. |
| H-12 | **not built: the simple lane's** | | The request is below. |
| H-13 | **left** | | Polish, and almost all in other lanes' files (`UrlsScreen.tsx`, `links/copyWords.ts`, Sitemaps, robots.txt) or in `export/offers.ts`, which no lane owns: one format order would change the default format of eight offers and their specs. "… report.xlsx" is the design's own name (`docs/v2-ui-simple.md`). |
| H-14 | **done** | `95ebfce3` (Shared) | Over Advanced the score sheet uses the check's own title, URLs, error / warning / notice. |
| H-15 | **done** | `e293b333`, `9ac55867` | KB (or "1.5 MB", "2 GB") saved as bytes; "% the same" saved as bits by the engine's own cos(πd/256); groups; two columns when wide; the characters hint once; how many crawls are out of date, with a link Home. Not built: a one-press "analyse them all". The engine's sentence for a refused value is replaced by the range in the unit typed for those two units only. |
| H-16 | **done, less the filter box** | `1c893055` | Another crawl of the same site keeps the screen, the check and the filters that mean the same in any crawl (`layout/switchSearch.ts`). Left: a filter box in the menu (it lists at most 8 crawls, and typing already moves to a match). |
| H-17 | **done** | `1c893055`, `ce13b223`, `a551ac62` | Site → Overview; date → the switcher; status → Overview's "The crawl" (`#the-crawl`); Back and Forward dimmed at the ends; the search shows its whole label from 1700 px. |
| H-18, H-19 | done (earlier) | | |
| H-20 | **done** | `da4a3448` | No Continue card on a finished crawl; non-default values bold with the default beside them and counted; an Analysis card with Analyse again and a link to the limits. Still a form's measure wide at 2560. |
| H-21 | **done** | `d0111cdd` | Sticky Start row (Advanced); two columns of sections from 1700 px; "Crawl <site> again" with which crawl's settings; Shopify's Remember shown from the start, disabled until a signature is pasted; an ended signature said once; Home's Crawl dimmed while the field is empty (pressed, it still says what's missing). |
| H-22 | **done in part** | `ce13b223` | "Paused" badge; 0 a second while paused; "No URL limit. At least N more for the M found so far"; the port in the crumbs. Left: the last URLs fetched (the live stream doesn't carry them: an engine change), and a question before Stop (Continue recovers it). |
| H-23 | done, differently (earlier) | `acd96aac` | |
| H-24 | **done** | `e293b333` | Settings' list and section are centred together, a form's measure wide. |

### H-12: what the shell lane asks of the simple lane (`screens/report/**`, `ReportDocument.cs`)

1. "2 problems fixed, 2 on more." → "2 fixed, 2 got worse" (`ReportDocument.cs:383`).
2. Show every row up to about 25 before "the full list is on sheet N" (`:829`): 8 rows were cut to 5.
3. "Fix these first" rows as anchors to their sections; in the app's preview an "Open in Issues" beside each section
   heading (the report's 134 links all go to the live site); a contents list.
4. Name the buttons for what they do: "Save as HTML…", "Save spreadsheet…", "Print or save as PDF".

## Files other lanes will meet in the merge

| Commit | Files outside the shell's list |
| --- | --- |
| `b484f6e5` Shared | `screens/issues/CheckDetail.tsx` (one `actions` prop on the skipped state, two imports), `screens/issues/AnalysisState.tsx` (optional `crawlId` on `AnalysisNotes`, the banner's actions), `screens/IssuesScreen.tsx` (passes `crawlId`) |
| `95ebfce3` Shared | `simple/ScoreBreakdownDialog.tsx` (mode-aware titles, words and one link) |
| `0e346dd3` Shared | `inspector/Inspector.places.test.tsx` (one line) |
| `1c893055` | `app/sections.ts` (the settings section's title), `styles/tokens.css` (`--search-width` from 1700 px) |
| `d0111cdd` | `styles/tokens.css`: the `wide` custom variant (`@media (min-width: 1700px)`) and `--form-max-wide`; `crawl/options/*` (no lane's list names `app/src/crawl`) |
| `a551ac62` | `styles/components.css` (`.si-name-columns > li`), e2e `framed.spec.ts`, `home-library.spec.ts` |
| `9ac55867`, `505c7f94` | e2e `overview.spec.ts`, `shell-frame.spec.ts`, `fix-loop-advanced.spec.ts`; `screens/compare/ComparisonCard.tsx` (a list gap) |
| earlier (`state-shell.md`) | `ui/Tooltip.tsx`, `ui/SegmentedControl.tsx`, `routing/search.ts`, `mode/*`, `styles/*` |

Things the merge must know:

- The section title "Settings" is now "This crawl's settings": any other lane's test or spec that finds the crawl's
  sidebar item or breadcrumb by "Settings" needs the new name. The palette command reads "Go to this crawl's settings".
- `asksForACrawl` stays in `screens/overview/answer.ts`; the button is `screens/overview/CrawlAgain.tsx`.
- The alert entry for "Sitemaps read" has `detailLink` and `action: null` when it has a figure.
- Tests rewritten to pin new behaviour this session: `CrawlSettingsScreen.test.tsx` (no Continue card on a finished
  crawl; the proxy row), `settings/Analysis.test.tsx` (range words in KB, the field's value 200, the characters hint),
  `Inspector.places.test.tsx` (above), e2e `framed.spec.ts` and `home-library.spec.ts` (the totals strip, H-19),
  `overview.spec.ts` (the Paused badge), `fix-loop-advanced.spec.ts` (the recheck's result line opens its URLs, H-23).
- Stories were not updated or run (see below).

## Click counts, measured in the drive (garden crawl copy; the same at both sizes)

| Task | Audit | Now |
| --- | --- | --- |
| 1. State of the site and the first thing to fix (URL and fault) | 2 | 1: a click anywhere on Home's row; Fix first shows each check's first URL with its fault |
| 3. What changed since the last crawl | 1, and the 18 → 257 jump hidden | 0 to read (the card lists what got worse, both counts); 1 to open a moved check |
| 4. Checks that didn't run: act on it | 3+ | 1 ("Crawl again…" in the card, landing on "Crawl gardenfurnituresets.co.uk again") |
| 5. Palette | actions and settings not found | "crawl again", "compare", "user agent", "speed", "proxy", "limits", "errors" each found; "404" lists Internal URL returns 4xx (second, after "Soft 404", whose title holds "404"); "recheck" finds Crawl again…; "copy" finds no command from Overview |
| 6. Switch to the site's other crawl with the 4xx filter kept | 2, filter lost | 2, filter kept; app Settings and back to the crawl's URLs: 2 (was Back or Home → site) |
| 8. New crawl, every section open | Start scrolled out of sight (3,657 px) | Start in view at both sizes; page 2,575 px at 2560 (two columns), 3,738 px at 1280 |
| 9. Settings › Analysis | 2,926 px, bytes and bits | 1,970 px at 2560; "200" KB and "95.7" % the same |
| 2, 7, 10 | | Not redone in this session: 2 was sampled (the score sheet, the alert, the list of text with digits outside links); 7 is the simple lane's; 10 was driven before the pause (H-03) and not again |

Looked at (Read) from this session's shots: Overview's first screen, New crawl open, Settings › Analysis and This
crawl's settings at 2560 × 1380; New crawl open, This crawl's settings and Settings opened from a crawl at
1280 × 720. Taken and not looked at: the rest in `after/shots/`. Not looked at at all: dark theme, forced colours
(beyond the framed spec), Simple mode's screens, the live crawl's new words on a running crawl.

## What did not run

- `npm run test:stories` (no Storybook was built) and no story was updated: `NewCrawlScreen`, `CrawlSettingsScreen`,
  Settings, `Shell`, `PaletteSearch` stories typecheck, and show the new screens unreviewed.
- The e2e specs outside the list below (grids, links, inspector, sitemaps, robots, simple-*, export, evidence…).
- `cargo check`, `smoke:shell`, the engine's tests and benches: nothing of theirs changed.
- `mode.test.tsx`'s old flake (`state-shell.md`) did not show in either whole run.

## Head and gates

Head **`505c7f94`** (15 commits on `1f2419f9`, 30 on the base `2a2cc140`). All of these ran at that head unless said:

- `tsc -b`: clean.
- `eslint . --max-warnings 0`: clean.
- `prettier --check .`: clean.
- Vitest, whole suite, `npx vitest run --maxWorkers=4`: **532 files passed, 14 skipped; 5,687 tests passed, 189 skipped, 0 failed.** No timeout, nothing rerun alone.
  (Baseline at `1f2419f9`, which the pause never ran: 5,604 passed; its three failures were this session's own half-made edit.)
- E2e against the sidecar built in this worktree (`npm run build:sidecar`, ports 49906/49907):
  - At `a551ac62`, thirteen specs (home, home-library, new-crawl, overview, topbar, shell-frame, framed, mode, states, issues, report, simple-home, fix-loop-advanced): **130 passed, 1 failed, 2 did not run** (the fix-loop spec is serial). The failure: "a recheck of a check from Issues…" waited for the button "URLs of recheck 1", which H-23 replaced before the pause.
  - At the head: `fix-loop-advanced.spec.ts` **4 passed**; `overview.spec.ts` and `home-library.spec.ts` **15 passed**. The other ten specs were not rerun after the last commit, which changed one list gap in the comparison card and that spec.
  - On the way there the first run of nine specs had 20 failures, all fixed: 14 the top bar's two `aria-current` places (the new site link), 3 specs pinned to the old totals row (H-19), 2 axe target-size findings (library counts, Not checked names), 1 (states) the same `aria-current`. A third target-size finding (the comparison card's moved checks) showed in the fix-loop spec and is fixed.
- Not run: stories, the rest of the e2e specs, `cargo check` (nothing under `app/src-tauri` changed), engine tests (nothing under `engine/` changed).

Processes: the lane's Vite (49904) and sidecar (49905) were stopped by the PIDs holding the ports; 49904 to 49907 are free. No crawl, recheck or test site was started outside the e2e runs' own temp folders.
