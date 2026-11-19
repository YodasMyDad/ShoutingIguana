# State of the simple lane at the pause (2026-11-19, 09:10)

Written by the orchestrator from the lane's checkpoints and git: the lane's agent ended on a server error (HTTP 529)
before it could write this itself. Nothing below was verified by the orchestrator beyond what git shows.

## Branches

| Branch / worktree | Base | Head | Commits | Tree |
| --- | --- | --- | --- | --- |
| `ui/ux-simple` / `ux-simple` | `4a7fab91` | `4a7fab91` | **none** | clean, no stash |
| `ui/ux-simple-report` / `ux-simple-report` | `4a7fab91` | `84b2641a` | 8 | clean |

**The Simple screens are not started in code.** In about an hour the lane's own agent set up (worktree, `npm ci`, its
own sidecar binary, a data copy, before shots) and decided how to build, but no file under `app/src/simple/` was
changed: every file there still has the worktree's creation time, and `WhereToFix.tsx` and `fixPages.ts`, which its
checkpoint names, exist nowhere. Only the report sub-lane, run by a helper, produced commits.

## What the lane decided (build these; nothing is built)

- **S-01** (the dead way back): pass `check: undefined` on the problem page's "← What to fix" link, in the sidebar's
  item (`layout/Sidebar.tsx`, a `Shared:` commit: the shell lane owns it) and in `goToSection` (also `Shared:`).
- **Link checks get a page-first card, "Where to fix it"**: a new `simple/WhereToFix.tsx` with a pure model
  `simple/fixPages.ts`, as section 4 of `audit-simple.md` draws it. Other checks keep cause | pages, with the pages as
  links and a visible "Open page".
- **One job for broken links**: the folded mirror check is not a second row; the lead row says "linked from N pages".
- The order it meant to work in: S-01, the card, Copy the list, the words, S-13, S-16, S-09, S-17, S-18, S-14, S-06,
  S-10, S-12, S-15, S-19, S-20, S-21, docs, verify, merge the report branch, shots and tasks, report.

## Findings

| Finding | State |
| --- | --- |
| S-01 to S-04, S-06 to S-10, S-12 to S-21, the words list (section 5) | **Not started** |
| S-05 (Copy the list and the spreadsheet) | **Partly**: the spreadsheet half is on the report branch (`b9f5d757`, a `Shared:` commit: a check's sheet names the page that links to each address in cells of its own). "Copy the list" in the app is not started |
| S-11, and the shell audit's A-12 (the report) | **Built on `ui/ux-simple-report`, not merged into `ui/ux-simple`**: see below |

## The report sub-lane (`ui/ux-simple-report`, 8 commits on `4a7fab91`, head `84b2641a`)

Its helper stopped cleanly and wrote its own state: **[`state-simple-report.md`](state-simple-report.md)** (each piece,
what ran, what it saw live, loose ends, next steps). In short:

- One report in two wordings (`words=plain|technical`): Simple asks for plain, Advanced for technical.
- Each problem is a section the Fix first and score rows link to, with a contents list and an "Open in What to fix" /
  "Open in Issues" link that navigates the app; the frame opens http(s) links in the browser.
- "Where to fix" leads with the page to edit and the link's words for the broken-link problem.
- Plain words for the report's rows in the engine, held to the app's by `plain-rows.json`.
- "2 on more" became "2 problems fixed, 2 got worse."
- The spreadsheet names the page that links to each address in cells of its own (S-05's spreadsheet half).
- Its e2e specs were rewritten and **not run**; the whole engine suite was **not run at head** (it passed two commits
  before: 2,428 + 333 + 45).

## Scratch (copied to `.claude/worktrees/_handoff/2026-11-19/lanes/simple-fix/`)

- The lane's crawl ids differ from the audit's, because an id hashes the file's path. In its copy: garden
  `38db60c2e21d6966` (previous `b193d82fd96fb479`), synthetic `cb0698ddc311d75d`, jitsunami `92fc0065cacda78a`,
  aptitude `b0a5728a5cb5b935`. It renamed `garden-a` to `garden-earlier` so `garden-b` is the newer crawl.
- Re-analysed with the new severities, the garden crawl has 4 errors and scores 86.
- Its own sidecar binary: `scratchpad/simple-fix/bin/si-sidecar.exe` (built from `4a7fab91`).
- Before shots: `shots/before-2560-*.png`.

## Processes

Its sidecar and Vite servers (ports 49914 to 49917) are no longer running; the orchestrator checked.

## Next five steps

1. Start a fresh agent on `ui/ux-simple` with `lane-briefs.md` ("simple"), `audit-simple.md`, this file and
   `state-simple-report.md`. First merge `ui/ux-simple-report` into `ui/ux-simple`, build, and run the engine suite and
   the app's typecheck, lint and `npx vitest run --maxWorkers=8` at that head: the report branch has no whole-suite
   run at its head.
2. S-01, with the test for the link, the sidebar item and the shortcut.
3. The "Where to fix it" card (S-02, S-03, S-04, S-07), then "Copy the list" (the rest of S-05).
4. The rest in the order above; the report's e2e spec and docs; `S-15`'s words that the engine lane left for this lane
   (see `state-engine.md`, step 5).
5. Look at every problem page of the garden crawl and the report at 2560 × 1380 and 1280 × 720 before calling
   anything done, and redo the audit's nine tasks with click counts.
