# Brief: finish engine E6 (search, the favicon, the limits' API, and what round 2 found the engine lacks)

Shouting Iguana v2: a .NET 10 engine (`engine/`) with a sidecar API, and a React app (`app/`). Engine E6 is a list
of twelve items the UI needs. Another agent built nine of them and was paused on 2026-11-11. You finish the list. A
fresh-eyes review of the whole branch follows you, so leave it in the state you would want reviewed.

## Where you work

- Worktree `C:\Projects\ShoutingIguana\.claude\worktrees\e6`, branch `ui/e6` (head 87e3ff5, clean, 80 commits
  behind `ui/step-3`). Work ONLY there. Small commits, each ending with
  `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`. Never push. Never touch another worktree or branch,
  except that you merge `ui/step-3` INTO your branch. No bare `git stash`. **Never rename an existing function,
  type or issue id.**
- The first agent's scratch folder still exists and is yours now (logs, `base-si\`, crawl copies, the pending
  files):
  `C:\Users\LeeMessenger\AppData\Local\Temp\claude\e2e5beaddde4c070\f209576d-c3b5-4cef-9d6c-2666276ff82b\scratchpad\e6\`.
  Its `checkpoint.md` starts with a **HANDOFF** section, item by item: that is your starting point (the same file,
  with `LinkEndsTests.cs.pending`, `handoff.md` and `specs/link-ends-*.py`, is in the repo as
  `docs/handoff/2026-11-11/e6/` on `ui/step-3`). Keep updating that `checkpoint.md` at each phase boundary.
- Other agents work in sibling worktrees at the same time: `e5` (export and the report), `catalog-fixes` (the
  catalog's texts and three rules: `Analysis/IssueCatalog.cs`, the rules, the plain words), `sitemaps-robots` (UI).
  Stay out of their areas.
- Stop only processes you started, by their own process id, never by a command-line match. Never run
  `dotnet build-server shutdown`. One heavy thing at a time: other agents load this machine, so a timing that fails
  is run again alone before you believe it.

## Read first

1. The HANDOFF section, whole (the twelve items, the next step for each, the traps).
2. `docs/handoff/2026-11-11/brief-e6.md` in the worktree `C:\Projects\ShoutingIguana\.claude\worktrees\ui-step-3`
   (the original brief: each item's scope, the checks, "Report back").
3. `CLAUDE.md` (repo root): only `Storage/` writes a crawl file; HTML is parsed once.

## What to do

1. **Merge `ui/step-3` (a782baf) into your branch first.** It brings the score, 87 checks, the plain words and
   E3c's fixes. Trap from the HANDOFF: a sidecar test demands that every `AnalysisSettings` property has a described
   limit in `Sidecar/Options/AnalysisLimits.cs`: if the merge adds a setting, add its limit. Build and run the whole
   test suite on the merged tree before going on.
2. **Item 6, "links to URLs" and a view's distinct ends**: written by the first agent, never applied or compiled
   (`LinkEndsTests.cs.pending`, `specs/link-ends-*.py`). Trap: its `bySource` edit to `LinkGridReader.Scan` must be
   redone by hand, because item 2 rewrote that method. Build it, with its tests seen failing without it.
3. **Item 3, the favicon**: not started. Do it as the original brief scopes it. A favicon is fetched by the crawl
   and stored through `Storage/` only; it must never hold up or fail a crawl, and it obeys the same rules of
   politeness as any request.
4. **Item 10**: the API doc (an "Engine changes E6 made" section, and each endpoint in its place), the TypeScript
   types and client functions in `app/src/api/` for everything E6 added (no screens), `engine/README.md`, and the
   readbench cases.
5. **The measurements on a quiet machine** that the HANDOFF lists as owed (the split link pass against the base
   exe, interleaved; then set the provisional `link-pass-split` gate and the doc's numbers), the audit of every new
   endpoint's numbers against SQL of your own on the bench and garden crawls, and the sidecar test that helpers
   never take the last read slot. The machine is shared: measure when your own work is the only heavy thing you
   run, repeat a timing before you trust it, and say what the load was.
6. Anything else the HANDOFF shows as not done or not verified for items 1 to 12.

## Checks, all seen green by you on the final commit

`dotnet build engine/ShoutingIguana.Engine.slnx -c Release` (warnings are errors); `dotnet test
engine/ShoutingIguana.Engine.slnx`; `si bench --pages 100000` (every planted page and issue, nothing else, memory
flat); `si bench --read-links --max-log-mb` as CI runs it (see `.github/workflows/engine.yml`); `si readbench <the
bench crawl> --runs 20 --gate`; from `app/`: `npm ci`, `typecheck`, `lint`, `format:check`, `test`.

## Report back

As the original brief's "Report back" asks: commits; each of the twelve items with its contract and its measured
numbers against its budget; what the merge needed; test counts; limitations (the kept search text's idle release,
anything at 1M URLs); what was verified and what was not. No file dumps.
