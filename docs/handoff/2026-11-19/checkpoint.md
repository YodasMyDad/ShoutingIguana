# Orchestrator's checkpoint: the UX round of 2026-11-19

**PAUSED about 09:15 for usage.** Session `da833503`. Nothing is running, nothing was pushed, every worktree is clean.
The status and the order to resume in are in `docs/v2-rebuild.md`, "Where step 3 stands", the block headed
"PAUSED 2026-11-19". This file is the detail behind it.

## What the owner asked (2026-11-19, on the installed app, their garden crawl of 19 Nov)

- "Still not very happy with a lot of the UX. The UI looks nice in most cases but the UX really falls short." The
  example: Issues > Internal URL returns 4xx shows each 404 and "Found on 1 page · /blog/…" as text; the page that
  holds the broken link can't be clicked; you expand, then "Show the link in Links", then another screen; the only
  direct control is an icon at the far right.
- "You need to do a UX audit. A high-level UX audit… of all the screens… from the perspective of the user and actually
  using them as an SEO to try and find faults, diagnose faults, look for things. Sort the issues out."
- "Similar in simple mode… people are going to be probably more interested as to where the broken link is."
- "Paginated page canonicalised to page 1" is good practice: "it shouldn't really be a warning, it just should be in
  the notices."
- "Links to broken external URLs": "It's a broken link… It should be in the errors."
- On "Internal links without anchor text", which they took for a false positive (it was right: the header logo is an
  inline SVG in a link with no label, on all 260 pages): "if there's an obvious fix, it needs to be very easy to read.
  In a brilliant UI and UX, so the user understands how to fix things."
- Asked whether everything from the old app had moved across. Answered: every check, yes, or dropped with a written
  reason (`docs/v2-new-checks.md`); not yet JavaScript rendering, list mode and custom extraction (step 4). The owner
  was asked whether to pull step 4 forward after this round and **has not answered**.
- Then: usage is running out; stop everyone, document everything, commit, so it can carry on later.

## Decisions

- Audit at **2560 × 1380** (the owner's window, measured from their screenshots: a 4K screen at 150%), then
  1280 × 720. Every earlier audit and gallery was 1280 × 720, which is why the faults were missed.
- The rules every screen now follows: [`fix-common.md`](fix-common.md). Rulings per lane:
  [`lane-briefs.md`](lane-briefs.md). A row's single click still selects; the URL's text inspects. Link faults are
  flat tables by default. Simple's broken-link card is grouped by the page to edit.
- The fix as code ([`fix-lane.md`](fix-lane.md)): the fix sits near the top of the pane as the markup now and the
  markup fixed, likeliest case first, with a "On this site" case where the rows say which.
- Not in this round, for the owner to decide: URL include/exclude patterns, JavaScript rendering, a URL-list crawl
  mode, a per-URL new/gone comparison, a "worst pages" list in Simple.

## What is where

| What | Where |
| --- | --- |
| The four audits | `audit-issues.md`, `audit-data.md`, `audit-shell.md`, `audit-simple.md` (auditors' brief: `audit-common.md`) |
| Each lane's state at the pause | `state-issues.md`, `state-data.md`, `state-shell.md`, `state-simple.md`, `state-simple-report.md`, `state-engine.md`, `fix-lane.md` |
| The driver and an example flow | `tools/drive.mjs`, `tools/f02-advanced-tour.mjs` (paths inside point at the `ui-step-3` worktree's `node_modules` and a `../shots` folder: adjust the shots folder) |
| Crawl copies to drive the app on | `C:\Projects\ShoutingIguana\.claude\worktrees\_handoff\2026-11-19\crawls\` (garden-a is the earlier garden crawl, garden-b the later and the one whose sitemap list was lost; jitsunami; aptitude; three synthetic) |
| The auditors' flows and 2560 × 1380 shots | `…\_handoff\2026-11-19\audit-scripts\`, `audit-shots\` |
| Each lane's checkpoint, flows, before/after shots | `…\_handoff\2026-11-19\lanes\<lane>-fix\` |
| The session scratchpad (temp, may be cleared) | `C:/Users/LeeMessenger/AppData/Local/Temp/claude/e2e5beaddde4c070/da833503-3c78-46eb-9e8a-d618294b5af4/scratchpad/` |

## Lanes at the pause

| Lane | Branch / worktree | Base | Head | Ports | State |
| --- | --- | --- | --- | --- | --- |
| severity | on `ui/step-3` | | `e02b185a`, `4a7fab91` | | Done. e2e and stories updated, not run; readbench not run |
| issues | `ui/ux-issues` / `ux-issues` | `2a2cc140` | `8c918b47` | 49884… | 12 done, 5 partly |
| data | `ui/ux-data` / `ux-data` | `2a2cc140` | `5922ad44` | 49894… | 6 done, 8 partly, 5 not started |
| shell | `ui/ux-shell` / `ux-shell` | `2a2cc140` | `1f2419f9` | 49904… | 9 done, 2 partly, 13 not started |
| simple | `ui/ux-simple` / `ux-simple` | `4a7fab91` | `4a7fab91` | 49914… | Nothing built (agent ended on HTTP 529) |
| simple's report | `ui/ux-simple-report` / `ux-simple-report` | `4a7fab91` | `84b2641a` | 49916… | Built, e2e not run |
| engine | `ui/ux-engine` / `ux-engine` | `4a7fab91` | `c71fe5bd` | 49924… | Jobs done, gates not run at head; `AnalysisFreshness.VERSION` 21 |
| fix as code | `ui/ux-fix` / `ux-fix` | `f2ae0922` | `179f838a` | | Finished, not mounted |

No lane ran the whole Vitest suite, lint, format check, stories or e2e at its head. No lane's "after" was looked at by
the orchestrator. Treat every "done" as "written and unit-tested in its own files", not as verified.

## Things a resuming session must know

- **Never push.** Commit locally only.
- Ports 50000-50059, 52353-53052, 53212-53311 and 57924-58023 are reserved by Windows on this machine: a server on
  them fails with error 10013 / EACCES.
- The app holds an event stream open: a Playwright flow must never wait for `networkidle`.
- A crawl's id hashes its file's path, so the ids differ in every copy of the data folder. Read them from Home.
- Vitest: uppercase `C:/` working directory, and on a loaded machine `npx vitest run --maxWorkers=8` is the run to
  trust (the default parallelism timed out three unrelated tests on each of three runs).
- A long text in a shell heredoc breaks the tool when it holds backticks: write it to a file and use a script.
- `TaskStop` on a background `npx vite` leaves the node process alive: kill it by the PID that holds the port.
- The built sidecar in `ui-step-3/app/src-tauri/binaries` dates from before the severity change: rebuild it
  (`npm run build:sidecar`) before driving the app from that worktree.
- Two other Claude sessions were open on this project during the round (`shoutingiguana-0e`, `shoutingiguana-11`);
  what they were doing is not known to this one. Check `git worktree list` and `git status` before trusting the table.
- This session's context was compacted mid-round and it lost the turn in which the fix-as-code lane was started; that
  lane's record is `fix-lane.md` and the memory note `issue-data-must-help-fix`.

## Next

The order is in `docs/v2-rebuild.md`, "To resume, in this order". In one line: finish the lanes (simple first), merge
(data, issues, fix as code, shell, simple, engine), run every gate on the merge, walk it as the owner at 2560 × 1380 in
both modes, then merge into `ui/step-3`, gallery at the owner's size, installer, upgrade the owner's install while the
app is closed.
