# Step 3b integration 1: merge ui/3b-accuracy and ui/3b-evidence-ui into ui/step-3

Work in the existing worktree `C:/Projects/ShoutingIguana/.claude/worktrees/ui-step-3` (branch `ui/step-3`, clean, at
aa2802b). Uppercase `C:/` paths always (a lowercase `c:` cwd breaks Vitest: "failed to find the runner"). NEVER push.
Commit messages in the repo's style, ending `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Use a message file
for `git merge -F` (`-F -` with a heredoc fails here).

## Order
1. `git merge --no-ff ui/3b-accuracy` (10 commits off aa2802b: check-audit fixes, rewording, severities,
   AnalysisFreshness 9, docs/v2-check-audit.md). Should be clean.
2. `git merge --no-ff ui/3b-evidence-ui` (contains ui/3b-evidence: evidence columns/targets/groups, details that name
   things, image checks grouped by image, AnalysisFreshness 9; app UI for it). Expect conflicts. Resolve them so BOTH
   branches' intent survives:
   - `AnalysisFreshness.VERSION`: both bumped 8 -> 9 independently: set **10** (analyses written by either branch's 9
     must count as out of date), and fix tests that assert the number.
   - `IssueCatalog.cs`: accuracy changed severities (sitemap-error W, structured-data-invalid W, description-duplicate N),
     What/Why/Fix texts and Simple words; evidence added `Evidence`/`TargetFacts` per check and changed details.
     Keep both.
   - Detail formats are evidence's; accuracy added one detail for charset-missing's late-tag case ("…ends at byte N…")
     — keep it and add it to docs/v2-evidence.md's table (the evidence contract said that check had no detail).
   - Accuracy made a home page's empty link to itself name the page itself as its target (empty-anchor-text): make sure
     evidence's targets/groups read that correctly (a target equal to the page; IssueHit.NO_OTHER_URL semantics) and
     the UI words it sensibly ("a link to this same page").
   - Plain-words file, planted cases, expected issue tables (the tests compare the whole issues table), score tests,
     docs/v2-sidecar-api.md, docs/v2-rebuild.md (Known limitations entries from both).
3. Re-capture the app's engine fixtures from the merged sidecar (`app/src/test/simple/engine-checks.json`,
   `garden-analysis.json` — find how they were captured; the accuracy branch left them on the old engine's words and
   severities) and fix the unit tests that legitimately change (never loosen a test to pass).

## Verify (quick set; the full long verify runs later, after ui/3b-polite also merges)
- `dotnet build engine/ShoutingIguana.Engine.slnx -c Release` and `dotnet test engine/ShoutingIguana.Engine.slnx -c Release --no-build`.
- `si bench --pages 100000` via `bash docs/handoff/2026-11-13/tools/verify-engine.sh <worktree> <logDir>` (if its
  readbench step misses the gate while another agent's benchmark runs — check with `tasklist | grep -i si.exe` —
  rerun readbench alone when quiet, as the other agents did).
- App, from `C:/Projects/ShoutingIguana/.claude/worktrees/ui-step-3/app`: `npm ci` if needed, typecheck, lint,
  format check, `npm test`, `npm run build:sidecar`, then e2e: evidence, issues, issues-subject, found-on, the Simple
  specs, urls, links, report, export, counts-agree, gallery6. Rerun alone anything that fails once under load before
  treating it as real; report which.
- Logs under `C:/Users/LeeMessenger/AppData/Local/Temp/claude/e2e5beaddde4c070/a539aa23-e856-44c1-bd21-ca484e8740e2/scratchpad/integrate1/`.

## Report
The two merge commits, each conflict and how you resolved it (one line each), the fixture/test changes, every result
(counts and exit codes), anything left. Don't touch other worktrees; don't merge ui/3b-polite (still in progress).
Don't edit "Where step 3 stands" in docs/v2-rebuild.md (the caller does).
