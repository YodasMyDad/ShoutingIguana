# Brief: bring Sitemaps and robots.txt up to `ui/step-3`, ready to merge

Shouting Iguana v2: a React + TypeScript app (`app/`) in a Tauri shell, reading a .NET sidecar. The Sitemaps and
robots.txt screens were built, reviewed and fixed (17 findings) on their own branch, which is now 134 commits behind
`ui/step-3`. You bring the branch up to date so it can be merged with no further work.

## Where you work

- Worktree `C:\Projects\ShoutingIguana\.claude\worktrees\sitemaps-robots`, branch `ui/sitemaps-robots` (head
  fe51eb0, clean). Work ONLY there. Commits end with `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`.
  Never push. Never touch another worktree or branch (you merge `ui/step-3` INTO your branch; you do not merge
  your branch anywhere). No bare `git stash`. **Never rename an existing exported function or component.**
- Your scratch folder (logs, `tmp`, `checkpoint.md`):
  `C:\Users\LeeMessenger\AppData\Local\Temp\claude\e2e5beaddde4c070\8e7045a4-9ede-460a-be29-971bc406a6c0\scratchpad\sitemaps-merge\`.
- Ports: `SI_DEV_SERVER_PORT=58874 SI_DEV_SIDECAR_PORT=58875 SI_PREVIEW_PORT=58876 SI_E2E_SIDECAR_PORT=58877`,
  `SI_STORYBOOK_PORT=6606` (never 6406).

## Read first

1. `docs/handoff/2026-11-11/ui-slice-rules.md` in the worktree `C:\Projects\ShoutingIguana\.claude\worktrees\ui-step-3`
   (your branch doesn't have the folder until you merge): every rule there holds, with your scratch folder and
   ports above in place of the ones it names.
2. In the same folder: `checkpoint-sitemaps-fixes.md` (the fixer's notes for this merge), `brief-sitemaps-fixes.md`
   and `reviews/sitemaps-findings.md` (what was fixed and why), `checkpoint-links-fixes.md` (what the Links slice
   changed in the shared grid: `GridScreen`, `minRows`, no reads while the window is hidden).
3. `docs/v2-rebuild.md` on `ui/step-3`, "To resume, in this order", item 1.

## What to do

1. `git merge ui/step-3` into your branch. Expect conflicts in `GridCore.tsx`, `api/queries.ts`,
   `keyboard/registry.ts`, `e2e/overview.spec.ts`, `UrlsScreen.tsx` and both UI docs
   (`docs/v2-ui-architecture.md`, `docs/v2-ui-ia.md`). Keep both sides' behaviour: every test of both sides must
   still pass, and none may be deleted or weakened to get there. If a test of one side contradicts the other,
   decide by the docs and say so in your report.
2. The engine moved under you: the catalog has **87 checks** and a status in a detail reads "404 Not Found" (it
   read "HTTP 404"). Fix tests, stories and mocks of yours that count 86 or match the old words.
3. Your two screens take the Links slice's `GridScreen` frame and `minRows` (a grid keeps five rows at 200% zoom),
   and make no reads while the window is hidden, as Links and URLs do. The status doc notes "Sitemaps' count columns
   start off-screen" at 200% zoom: fix it if the frame doesn't.
4. `GET /sitemaps` now really sends `urls.robotsDisallowed` and `urls.redirectingOrBroken` for each sitemap (your
   branch built the list's Crawled and Blocked columns against a mock). Check both columns against the real sidecar
   on the garden crawl and the 100k crawl (`npm run dev`), with an e2e or a figures test that compares each number
   with where it lands. "Crawled" means the crawl requested the URL; URLs robots.txt refused have their own column.
5. Update both UI docs for what you settle.

## Checks, all seen green by you at the end, on the merged tree

From `app/`: `npm ci`, `typecheck`, `lint`, `format:check`, `test`, `build-storybook`, `test:stories` (also
`-- --forced` and `-- --reduced-motion`), `node --test "scripts/**/*.test.mjs"`, `build:sidecar`, `e2e`; your own
e2e specs with `--repeat-each 10`. The orchestrator runs all of it again after merging, and that run has twice
caught what an agent's green run missed (a tree that didn't compile its tests, an unformatted file): run every
step on the final commit, not on an earlier one.

## Report back

Short: the merge commit and the commits after it; each conflict and how it was settled; what the 87 checks and the
new status words broke; the two columns against the real sidecar (numbers); test counts; screenshots folder
(1280x720 both themes, 200% zoom); what you left out or found in the engine, with evidence. State plainly what was
verified and what was not.
