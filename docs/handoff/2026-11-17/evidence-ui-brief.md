# Step 3b, part 2 (UI): every check shows its evidence — app brief

## Why (the owner, 2026-11-17)
"some screens don't show the correct information for the user to be able to make actionable decisions ... the broken
links never showed what page they were on ... a large images UI ... does not show the image size OR where the image is
shown! For every single view / UI it must be actually useful, and the UI&UX must be world class, so it's easy to scan,
understand and action and the user fix their site."

Bar for every check's views (Advanced Issues: the check's rows / groups / a group's pages / the expansion; Simple's
problem page "Where it is" and its One-fix callout): a site owner sees (a) WHAT is wrong — the value against the limit,
(b) WHERE to fix it — the page; for a resource every page it's on; for a link the linking page + anchor/alt + position;
template vs one-off, (c) HOW — the fix text matches. Scannable at a glance: the key fact in the row, not behind clicks.
Never make them open the inspector or the page source to find what's wrong.

## Read first
- The contract (engine agent, in progress on `ui/3b-evidence`): `docs/v2-evidence.md` (per-check table: evidence
  columns, target facts, detail before -> after, order) and the "(evidence)" parts of `docs/v2-sidecar-api.md`
  (`/api/checks` `evidence` + `targetFacts`, `counts[].likelyTemplate`, new URL columns incl. type `object`
  for `redirectChain` / `robotsRule`, groups `related` / `sharedText` / `sort` / `likelyTemplate`, richer `/targets`).
- What the views show today, with file:line:
  `C:/Users/LeeMessenger/AppData/Local/Temp/claude/e2e5beaddde4c070/a539aa23-e856-44c1-bd21-ca484e8740e2/scratchpad/audit3b/evidence-inventory.md`
  section 1 (IssueRowsGrid.tsx, IssueGroups.tsx WhereCell/TemplateCallout, WhereFoundLine, whereFound.ts,
  simple/ProblemScreen.tsx, simple/WhereItIs.tsx, FoundOnWords) and section 4.
- The view audit (for context on Issues and Simple's problem page only; other views are a later round):
  `.../scratchpad/audit3b/ux-views.md`.
- Design rules: `docs/v2-ui-brief.md` (definition of done), `docs/v2-ui-design.md`, `docs/v2-ui-simple.md`,
  `docs/v2-ui-architecture.md`. The owner's look (2026-11-15): framed workspace; no accent rule along the edge of a
  current/selected item (current = neutral fill + bold); strong segmentation, light theme never "wishy washy";
  Simple vs Advanced changes how the app is used, never what settings exist. Simple uses plain words (glossary in
  `docs/v2-ui-discovery.md#glossary`), no jargon (say "the image file is 1.8 MB, 4000×3000 pixels; it's shown at
  400×300"), Advanced is dense and precise.

## Where
Worktree `C:/Projects/ShoutingIguana/.claude/worktrees/3b-evidence-ui`, branch `ui/3b-evidence-ui` (off the contract
commit a97c31f). ALWAYS run npm/vitest from an uppercase `C:/Projects/...` path (a lowercase `c:` cwd makes every Vitest
file fail with "failed to find the runner"). Commit locally, small logical commits in the repo's message style, each
ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. NEVER `git push`. Don't touch other worktrees.
The engine implementation lands on `ui/3b-evidence` while you work: build the UI against the contract with the app's
existing fixtures/mocks (find how stories and unit tests fake the API) first; when the caller tells you the engine branch
is done (or you see new commits on `ui/3b-evidence` past a97c31f that complete Stage B), `git merge ui/3b-evidence` into
your branch and finish against the real sidecar (e2e). Update the hand-written API types in `app/src/api` to the
contract.

## What to build
1. **Advanced Issues**
   - Flat rows (grouping none): request and show the check's `evidence` columns after URL and Detail; render types
     properly (text with length, numbers right-aligned, `object` columns: a compact redirect chain "→ /b 301 → /c 200",
     the robots rule "Disallow: /cart/ · line 12"). Default order = the engine's importance order; show Inlinks.
   - Groups: the group row shows the `related` facts inline (an image: `1.8 MB · JPEG · 4000×3000 px · shown at
     400×300` with the size as the sort key for image-too-large; a redirect: final URL + status; a canonical target:
     its status/indexability), or the `sharedText` for duplicate clusters ("“Garden Furniture Sets | Shop” on 14
     pages"). Pages count stays. The "One fix" / template callout uses `likelyTemplate`.
   - A group's pages and the expansion (WhereCell) show `targetFacts` per target type: link text and position
     ("in the footer · “Old range”"), #fragment, nofollow, image attrs (alt, shown-at size, srcset/loading), final URL.
   - Ungrouped checks with `likelyTemplate`: a callout "Probably your site's template: on N of M pages — one fix".
   - The URLs grid's column registry learns the new columns (usedOn, pixelWidth/Height, redirectChain, robotsRule,
     fetchError) with labels and tooltips; put `usedOn` and the image size columns in the relevant presets.
2. **Simple's problem page** — "Where it is" shows what Advanced shows, in plain words: per cause the fact (image size
   and pixel size and shown-at, the link text and where on the page, the title text and its length vs the limit, the
   redirect's end), per page the row detail; the One-fix callout from `likelyTemplate`. Keep it calm and scannable:
   the fact on one line, secondary details muted.
3. Words: every new label in Simple goes through the plain-words layer the app already uses; Advanced uses the
   glossary's terms.
4. Quality bar: keyboard and screen reader (accessible names on new cells), forced colours, reduced motion, both
   themes, 1280×720 at 150% without clipping the key fact (truncate secondary text, never the number). Follow the
   existing story/e2e patterns: add or update stories for the changed components and e2e for the image and redirect
   cases on the synthetic test site (`si testsite`). Unit tests for new formatters.

## Verify before reporting
From `C:/Projects/ShoutingIguana/.claude/worktrees/3b-evidence-ui/app`: `npm run typecheck`, `npm run lint`,
`npm run format:check` (or the repo's equivalent), `npm test`, the stories run for the components you changed, and the
e2e specs for Issues and Simple. The full `docs/handoff/2026-11-13/tools/verify-app.sh` runs at integration (by the
caller), so don't run it unless asked. Report: commits, what each view now shows (a short before/after per pattern),
test counts, anything not done and why, and screenshots' paths if you took any (save them under
`C:/Users/LeeMessenger/AppData/Local/Temp/claude/e2e5beaddde4c070/a539aa23-e856-44c1-bd21-ca484e8740e2/scratchpad/audit3b/shots-evidence/`).
