# Brief: finish engine E5 (export, the workbook and the report)

Shouting Iguana v2: a .NET 10 engine (`engine/`) with a sidecar API, and a React app (`app/`). Engine E5 (CSV and
Excel export of every grid, the workbook of everything, the report as one HTML file, `si export`, `si report`) was
built by another agent and paused on 2026-11-11, part-way through its second phase. You finish it. A fresh-eyes
review of the whole branch follows you, so leave it in the state you would want reviewed.

## Where you work

- Worktree `C:\Projects\ShoutingIguana\.claude\worktrees\e5`, branch `ui/e5` (head 03877b6, clean). Work ONLY there.
  Small commits, each ending with `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`. Never push. Never touch
  another worktree or branch, except that you merge `ui/step-3` INTO your branch. No bare `git stash`. **Never
  rename an existing function, type or issue id.**
- The first agent's scratch folder still exists and is yours now (its crawl copies, `measure.py`, `printpages.py`,
  `excel-open.ps1`, `out\`, logs):
  `C:\Users\LeeMessenger\AppData\Local\Temp\claude\e2e5beaddde4c070\f209576d-c3b5-4cef-9d6c-2666276ff82b\scratchpad\e5\`.
  Its `checkpoint.md` starts with a **HANDOFF** section: that is your starting point (the same file is in the repo
  as `docs/handoff/2026-11-11/e5/checkpoint.md` on `ui/step-3`). Keep updating that `checkpoint.md` at each phase
  boundary.
- Other agents work in sibling worktrees at the same time: `e6` (search, link reads, analysis settings),
  `catalog-fixes` (the catalog's texts and three rules), `sitemaps-robots` (UI). Stay out of their areas.
- Stop only processes you started, by their own process id, never by a command-line match (the HANDOFF's trap about
  headless Edge). Never run `dotnet build-server shutdown`. One heavy thing at a time: other agents load this
  machine, so a timing that fails is run again alone before you believe it.

## Read first

1. The HANDOFF section (above), whole, with its "Exact next step" and "Traps".
2. `docs/handoff/2026-11-11/brief-e5.md` in the worktree
   `C:\Projects\ShoutingIguana\.claude\worktrees\ui-step-3` (the original brief: scope, checks, "Report back").
3. `CLAUDE.md` (repo root), and `docs/v2-ui-simple.md` "The report".

## What to do

1. `ui/step-3` has moved to a782baf (the Links screen and docs; little or no engine change): merge it in.
2. The HANDOFF's "Exact next step", items 2 to 8, all of it: re-analyse the scratch copies, re-measure the report
   and the workbook on the garden and 100k crawls, look at every printed page of the garden report against the
   design's (not only page 1) and fix what is wrong, open the workbooks in Excel (the hostile one too), the docs
   (`docs/v2-sidecar-api.md` "Report (E5)" and "Engine changes E5 made", `engine/README.md`), the Debug test run,
   the 100k bench once, the read gate.
3. Two things the UI will need, which the HANDOFF's notes raise: the `export` live event must be in the API doc's
   "Live events" with its exact shape (the app's `SidecarEvent` union lacks it: add the TypeScript type and any
   client functions the app needs for export and the report in `app/src/api/`, without building any screen), and
   say in the API doc what a `srcdoc` frame showing the report needs from the app's content security policy (the
   report has an inline style sheet).
4. Anything the original brief asked for that the HANDOFF doesn't show as done: check its list against the code and
   finish it. If something can't be finished, say so plainly rather than leaving it half-built.

## Checks, all seen green by you on the final commit

`dotnet build engine/ShoutingIguana.Engine.slnx -c Release` (warnings are errors); `dotnet test
engine/ShoutingIguana.Engine.slnx` in Debug and in Release; `si bench --pages 100000` (every planted page and issue,
nothing else, memory flat); `si readbench <the bench crawl> --runs 20 --gate`; from `app/`: `npm ci`, `typecheck`,
`lint`, `format:check`, `test`.

## Report back

As the original brief's "Report back" asks, with the contract as settled (endpoints, the job, the event, the CLI),
the measurements (time, peak memory, file sizes, page counts on both crawls), what the wording "Checked once, for
the whole site (counts as a few)" is (the first agent's own, not the design's), test counts, and what was verified
and what was not. No file dumps.
