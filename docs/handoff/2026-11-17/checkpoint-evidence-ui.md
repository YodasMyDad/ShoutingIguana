# Checkpoint: step 3b evidence UI (worktree 3b-evidence-ui, branch ui/3b-evidence-ui)

Brief: scratchpad/audit3b/evidence-ui-brief.md. Contract: docs/v2-evidence.md + "(evidence)" in docs/v2-sidecar-api.md.

## Done (committed on ui/3b-evidence-ui, off a97c31f)
- fb45f5d types + src/crawl/evidence.ts formatters + URL registry (object type, new cols, hints, presets)
- e7d0aff Advanced Issues evidence (IssueRowsGrid, CheckDetail, IssueGroups, whereFound, evidenceColumns.ts,
  test/evidenceSidecar.ts, IssuesScreen.evidence.test.tsx, 7 Evidence* stories)
- cdae273 Simple (plainEvidence.ts + test, TargetWords.tsx, WhereItIs, ProblemScreen flat One fix, FoundOnWords,
  ProblemScreen.evidence.test.tsx, 6 ProblemEvidence* stories); linkedFromWords removed (now plainLinkedFrom)
- 106bb26 e2e/evidence.spec.ts (NOT RUN: needs engine)
- 470d34a RobotsRule.origin nullable (matches engine RobotsRefusal)
- Verified: typecheck, lint, format:check, npm test 3941 passed/107 skipped, stories (13 evidence stories, both
  themes, forced colours, reduced motion): no violations. DTOs on ui/3b-evidence (439dbba) checked against types.

- Review fixes (coordinator, 5 items): a8307b7 Advanced (Pixels + Shown at columns, insight, thumbnails via
  ui/ImageThumb.tsx, useShownAt.ts, share bar dropped < 760 px for images, page detail omitted for targets checks,
  CSP img-src += https: http:), 3f13387 Simple (plainImageInsight, plainOthers, oneFixWords.ts One fix rewrite,
  thumbnail in cause cell). Full unit suite 3959 passed; stories axe clean light/dark/forced/reduced motion.

- Merged ui/3b-evidence (20ceccf) as 5e1078a; sidecar rebuilt (app/src-tauri/binaries); b2e60d3 reconcile
  (pixel cols display-only, fetchError wording, 'Used on pages', as-built details, engine-checks.json re-captured:
  27 evidence / 24 targetFacts). e2e run 1 started (log logs/e2e-1.log).

- e2e run 1 (62): 57 passed, 4 failed (all old expectations: grid name 'Large images', found-on page line, subject
  order words x2), 1 skipped; fixed in 1e8f37a; rerun evidence+found-on+issues-subject: all pass. Run 3
  (gallery6, links, export, counts-agree, handover, report, inspector, framed) in progress (logs/e2e-3.log).

## Left
- Engine ui/3b-evidence at 439dbba, no docs/verification commit yet -> caller says when to merge.
- After merge: build sidecar, run e2e issues/simple/found-on/urls/evidence specs; maybe re-capture
  src/test/simple/engine-checks.json.
- docs/v2-rebuild.md not edited (caller/integration).

## Gotchas
- Python edits via tools/ed.py (keeps LF). Vitest from C:/Projects. design/data/fixtures copied (ignored).
- tools/shots.mjs: node <it> outDir WxH scale theme ids... (SCROLL=selector env).
