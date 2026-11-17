# Lane "fixloop": the fix loop's engine, stages E1 to E4 (step 3b, item 4)

Read `lanes-common.md` in this folder first. It covers the base, the HOLD on builds, the rules, the verify and the report.

- Worktree: `C:/Projects/ShoutingIguana/.claude/worktrees/3b-fixloop-engine`
- Branch: `ui/3b-fixloop-engine`
- First step: `git merge --no-ff ui/3b-fixloop`. It brings the design `docs/v2-fix-loop.md` and the API marked
  "(fix loop)" in `docs/v2-sidecar-api.md`; it's docs only.
- Ports: verify-app base **51200**, Storybook **51206** (only if you touch the app; this lane shouldn't need to)
- Logs and checkpoint: `scratchpad/fixloop/`

## Why
The owner chose "Compare + recheck":
- compare each crawl with the site's previous crawl (New / Fixed / Fewer, the score's change);
- "Recheck these N pages";
- no manual "mark fixed".

The design is `docs/v2-fix-loop.md`. Its eight open decisions were all accepted as the design recommends; they are
listed in `docs/v2-rebuild.md`, step 3b block, "Designed, not built yet". Section 10 is the implementation plan, and
section 9 is the test plan.

## Build: stages E1 to E4, as section 10 defines them
- **E1 Test site revisions** first:
  - revision 1 crawls to exactly its expectations;
  - revision 0 stays unchanged;
  - the bench stays unchanged.
- Then **E2 Comparison reader** (`Storage/` readers, `si compare`, the readbench `compare` class, `si bench --revisions`)
  and **E3 Recheck engine (URLs)** side by side.
  - The design says they only meet in the catalog.
  - You may run them as two sub-agents in their own worktrees, branched off your E1 commit, then merge both into
    `ui/3b-fixloop-engine`. Or run them one after the other yourself if that's simpler.
  - E3 adds `IssueDefinition.Recheck` to every catalog entry. Make it required, with a test that every definition
    declares one. Another lane is adding about 84 checks in parallel, and that test is how the merge catches any
    without it.
- Then **E4 Recheck kinds** (robots.txt, site versions, sitemaps).
- Each stage meets its "Done when" from section 10, and every case in section 9's engine list is seen failing first.

## Verify
- `verify-engine.sh` after E1 and after E4.
- readbench must show the compare queries within the design's section 8 budgets at 100k. Say so if a budget is missed
  only under the other lanes' load.

## Out of scope
- S1 and S2 (sidecar), U1 and U2 (app) and V: the orchestrator launches them after reviewing this lane.
- Don't run a recheck against a real site.

## Report
The common report, plus:
- `si compare` output on revision 0 against revision 1 of the 1,000-page test site;
- `si recheck` on a planted problem: before and after counts.
