# Lane "checks": the new checks, batches 1 to 3 (step 3b, item 5)

Read `lanes-common.md` in this folder first. It covers the base, the HOLD on builds, the rules, the verify and the report.

- Worktree: `C:/Projects/ShoutingIguana/.claude/worktrees/3b-checks`
- Branch: `ui/3b-checks`
- First step: `git merge --no-ff ui/3b-newchecks`. It brings the specification `docs/v2-new-checks.md`; it's docs only.
- Ports: verify-app base **51300**, Storybook **51306**
- Logs and checkpoint: `scratchpad/checks/`

## Why
The owner (2026-11-17), "All of them": every old-app backlog check, and every Screaming Frog check that needs only
data we already fetch.

The spec is `docs/v2-new-checks.md`:
- read "How to read an entry", "Rules every new check keeps", "What the parse adds", "Storage, crawl and reader
  changes" and "Build batches";
- read "What changes existing flags and the bench";
- then read each entry in your batches.

## The orchestrator's rulings (these win over the spec)
- `title-same-as-h1` is dropped: Google recommends they match. It's in batch 8, not yours, but don't build it.
- `sitemap-over-limit` is a Warning, not an Error.
- `readability-low` applies to English pages only, as a Notice.
- `deep-page` uses the crawl's recorded depth, and the bench's expected count is computed.
- Change C8 (pixel widths) is already built on your base. Skip it.

## Build: batches 1, 2 and 3, in order, one commit series per batch
1. Indexing and canonicals
2. Links and crawlability
3. The head and served files

Each batch builds what the spec's "Each batch, for each check" lists, minus the status doc, which the orchestrator
updates:
- the catalog entry (What, Why, Fix, LearnMore, `Grouping`, `Subject`, `References`, `Evidence`, `TargetFacts`);
- its plain words;
- its skip reasons;
- the plant and its expectation;
- a rule test seen failing first, then passing;
- the whole-table test;
- a row in `docs/v2-evidence.md`'s per-check table;
- `verify-engine.sh` at the end of each batch.

Simple's words are plain: no jargon and no units. Read the existing plain-words file and match its voice.

The app:
- recapture the engine fixtures (`app/src/test/simple/engine-checks.json`, `garden-analysis.json`) at the end of each
  batch, the way `3721374`, `c6a9b40` and the pixels lane did (git log);
- fix the app unit tests that legitimately change;
- run `npm test` and the Simple and Issues e2e.

## Coordination with the fix loop lane
That lane adds a required `IssueDefinition.Recheck` to every catalog entry, in parallel with you.
- You don't have it yet.
- Don't put the field or comments in code.
- Instead, list each new id with what a recheck must re-read (the URL alone, robots.txt, sitemaps or site versions) in
  a table in `scratchpad/checks/recheck-kinds.md`, following the fix-loop design's "What each check's recheck re-reads"
  (`git show ui/3b-fixloop:docs/v2-fix-loop.md`).
- The orchestrator adds the field from that table at merge.

## Then
Report after batch 3. You'll be resumed for batches 4 to 9.
