# r4-home checkpoint (Part A of round 4)

Worktree C:\Projects\ShoutingIguana\.claude\worktrees\r4-home, branch ui/r4-home, from ui/step-3 e8176c6.

**Ports: the brief's 65874-65877 are invalid (> 65535). Using 55874 dev, 55875 sidecar, 55876 preview, 55877 e2e
sidecar; Storybook 7306; 55879 a static server of the mockups (python http.server, pid unknown; bound 127.0.0.1).**
TEMP/TMP = this folder's tmp. Crawl copies for SI_E2E_CRAWL in `crawls\`.

## Commits

- 54bfe3c score ring (`app/src/ui/ScoreRing.tsx`, `score.ts`, story `Design system/Score ring`). B and C merge this.
- 2016ab2 Home + checking + dispatcher; rows: home -> simple/home/SimpleHome, overview -> simple/SimpleOverview
  (Summary = TODO(round4-merge) Part B; Advanced OverviewScreen until then).
- 7b99abd finished toast both modes.
- 6bf3329 Rust notification in Simple words; tests.
- a4b9aed stories, e2e spec `e2e/simple-home.spec.ts`, time-left with limit.
- a0bd382 docs (architecture section at the end) + switch measure test on Home.

## Verified

- typecheck, lint, format:check green; vitest x3 (one under storybook build load): 266 files, 3028 passed.
- e2e simple-home spec green on the seeded crawl; figure tests green on garden copy (85 Good 2/4/265, first paint
  164 ms) and 100k copy (first paint 151 ms).
- cargo fmt/clippy clean; cargo test 114 passed (before a no-logic clippy fix).

## Engine finding

- library pageCount vs analysis pagesAnalyzed differ by 1 on the seeded synthetic crawl (156 vs 155) and on the 100k
  (101,107 vs 101,106); equal on garden (265). Home shows pageCount (as Overview's "pages read").

## Next

- test:stories (+forced, +reduced-motion) running; then node --test scripts, build:sidecar, cargo test, full e2e,
  e2e:filled; merge ui/step-3 (no new commits at a0bd382 time); final report.

## 2026-11-13 later

- More commits: 06149ff, 8d7e07e (24px target), 13abbc3 (Cancel words); merged ui/step-3 a426614 (urls-grid) as
  ac1c9a3, clean.
- On the merged tree: typecheck, lint, format, scripts (43), vitest x3 (290 files, 3182 passed), build:sidecar,
  cargo fmt/clippy/test (115+3) green. Stories x3 running (bfbffhcrk); then full e2e, then e2e:filled.
- Pre-merge: stories 356 x2 themes, forced, reduced-motion all no violations.

## Final (head 5d0eda4, ui/step-3 0a79de0 merged)

- Stories x3 on the merged build: 386 stories x 2 themes, no violations (plain, forced, reduced motion).
- e2e:filled 29 passed; full e2e 158 passed, 1 skipped (not mine); simple-home --repeat-each 10: 50/50.
- Home first paint 134-164 ms (seeded, garden, 100k); Home mode switch median 8-10 ms, max 16 ms.
- DONE. Report written as the final message.
