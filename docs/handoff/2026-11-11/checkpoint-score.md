# Checkpoint: score agent (engine, round 3)

Brief: `...\scratchpad\brief-score.md`. Worktree `C:\Projects\ShoutingIguana\.claude\worktrees\score`, branch `ui/score`.
Never push/merge. Never rename a public function or issue id. One heavy thing at a time; 100k bench once, at the end.
Scratch: `...\scratchpad\score\` (tools/ = edit helpers: `edit.py` sub(), `eol.py` CRLF normaliser to run before every
commit, `stage.py` partial staging, `mutate.py` mutation checks; crawls/before = dev crawl copies as they were;
crawls/v6 = analysed by baseline engine; crawls/v7/garden.sicrawl = analysed by the new engine; cli-before = baseline
`si.exe`; libtime = library-read timing harness, `out-before` = baseline build).
GOTCHA: never put `\\` or mixed `'''`/`"""` in a bash heredoc (the tool collapses backslashes / fails to parse): write
edit scripts with the Write tool, then run them.

## Orchestrator's mid-task additions (taken in, DONE in engine + sidecar)

- A. `plainLooksFor` (IssuePlainText.LooksFor): optional noun phrase <= 60 chars; null for all ten now.
- B. `reportedOnce` (IssueDefinition.ReportedOnce): nine checks = five siteWide + robots-txt-missing, sitemap-error,
  sitemap-missing, security-headers-missing. All nine confirmed against their rules: each is ONE hit on the home page
  (several failing origins/sitemaps are listed in one detail); duplicate-site-version is one per other version (<= 3).
- C. Limit tokens `{limit:<camelCaseSettingName>}`: `PlainLimits.Fill(string|IssuePlainText|SkipReasonPlainText,
  AnalysisSettings)`, `PlainLimits.Describe(name, settings)`, `PlainLimits.Names` (12 names = every numeric
  AnalysisSettings property). `GET /api/checks?crawl={id}` fills from that crawl's analysis settings (current settings
  if never analysed; `crawl-not-found` 404; `bad-request` for empty/repeated). Analysis skip reasons filled from the
  analysis's settings.

## Decisions (and why)

- Score inputs: pages = analysis `pagesAnalyzed`; urls = URLs crawled when analysed (`UrlTable.CountCrawled()`,
  = library `crawledUrlCount` definition), stored in `analysis.crawled_urls`.
- Stored columns on `analysis`: `crawled_urls`, `score_version`, `score`, `score_partial`, `score_json`.
- Fix first = app's rankChecksToFix EXACTLY (severity, count desc, catalog order). OPEN POINT to report: a site-wide
  Error has count 1, so sorts after errors with bigger counts and can fall outside the first five / `ifFixed`; design
  says a site-wide Serious problem "leads the sentence and the list". Left exact as briefed; one line to change.
- JSON names: band `veryGood|good|needsWork|poor`; extent `few|some|many|most|site`; unit `pages|urls|site`;
  `noScoreReason` `noPages|notScored`; `covers.reason` `stopped|failed|urlLimit|depthLimit`; fixer `you|developer|either`.
- Plain texts: ten checks word for word from the design doc (tools/gen_plain.py); 17 category titles (words.mjs);
  skip reasons only `no-sitemap`, `site-versions-unchecked`.
- New check edges: see SiteVisibilityRules doc comment. RISK to report: a faceted shop where most crawled pages are
  canonicalised variants would be flagged (design's definition followed).
- Depth limit also makes the score partial (`depthLimit`), like the URL limit. Start-folder scope does not.

## Commits so far (branch ui/score, on top of ec4d2e7)

230f8e4 Engine: status code names in details
2476498 Engine: siteWide + site-hidden-from-search (rule, plant, tests)
d327863 Engine: plain words (files, models, tests)
619cc1d Engine: the score formula (ScoreCalculator, FixFirstOrder, reference cases)
97bd2eb Engine: an analysis stores its score (storage, freshness, VERSION 7)
12de6b9 Engine: reportedOnce
df7dd4d Engine: plainLooksFor + limit tokens (PlainLimits)
(amended) Cli: si score, si issues head  [amended once to fix stray CRs: my own unpushed commit]
403d790 Sidecar: API serves score, catalog fields, plain words

## Baselines measured (before any change)

- Library summary read (`CrawlSummaryReader.Read`, 40 reads, 3 runs), files analysed by baseline engine (crawls/v6):
  garden median 2.7-2.9 ms (p95 3.0-3.2); synthetic-100k median 36.4-37.2 ms (p95 37.8-40.6).
- Baseline `si analyze` of the 100k copy: 13.0 s.

## Verified so far

- Debug build clean. Engine.Tests: 1105 passed + additions (PlainLimits, ReportedOnce) pass; Cli.Tests 20 passed;
  Sidecar.Tests 174 passed. (Full `dotnet test` of the solution NOT yet re-run after the last additions.)
- Mutation checks (tools/mutate.py): ceiling removed -> 7 ScoreCalculatorTests fail; site-wide rule removed -> 7 tests
  fail (5 calculator, ScoreStorageTests.TheAnalysisStores..., CrawlEngineTests.FindsASiteHiddenFromSearch); stored URL
  count read live -> ScoreStorageTests.AStoppedCrawls... fails; URL count not stored -> 3 ScoreStorageTests fail. All
  restored and green.
- Garden copy analysed by new engine: `si score` = 85, Good, ifFixed 98.

## Still to do

- [ ] Docs: `docs/v2-sidecar-api.md` (Checks, Analysis, Library, Conventions if needed, new "Engine changes" section),
      `app/src/api/types.ts`, `engine/README.md` (si score, testsite option, 87 checks, score, plain words),
      CLAUDE.md mentions? (86 -> 87 only where stated), docs/v2-ui-simple.md table names? (leave design doc; report names).
- [ ] App: copy fixtures, npm ci, typecheck, lint, format:check, test, build:sidecar, e2e; fix tests/fixtures/mocks only.
- [ ] Final: Release build, full dotnet test, bench 100k once, readbench --gate on it, library timing "after",
      analysis time of 100k copy with new engine.
- [ ] Final report (names list, decisions, garden score, app changes, numbers, not verified).

## Update (after docs + app)

- Committed since: f9ffe4b Docs (contract + README); 1056c8f Tests (app types, mock heuristic + test, sample details,
  inspector fixture patched by mapping). Cli commit is fa0bdef-amended (see `git log`).
- App checks seen green once: typecheck, lint, format:check, test (90 files, 876 passed, 1 skipped), build:sidecar,
  e2e (62 passed, real sidecar) — e2e ran BEFORE the mock/test-data edits; re-run all at the end.
- Library read after (back to back, loaded machine): garden 3.4-3.6 ms before vs 3.1-3.6 now; 100k 41.7-44.2 vs 40.9-42.7.
- Analysis of 100k copy: 12.9 s baseline vs 12.9 s new (second analyses back to back); first analyses 23.3 vs 20.7 s.
  (A 34.9 s first run was machine load.)
- NOT touched on purpose: docs/v2-rebuild.md status section (orchestrator's), comments mentioning "HTTP 404" in
  app/src/screens/issues/IssueGroups.tsx and app/src/grid/filterSentence.ts (not tests/mocks/types).

## Remaining

1. Final: Release build, full `dotnet test` of the solution, bench 100k ONCE (keep crawl with --db in scratch), readbench
   --gate on it, app checks again (typecheck, lint, format:check, test, build:sidecar, e2e).
2. Put bench/readbench numbers into README (bench paragraph) and API doc "Engine changes the score made"; commit Docs.
3. Final report.

## Update (final verification under way)

- Commit added: 9e17d9a Engine: score breakdown read by name, and only when it matches the headline.
- Release build (non-incremental): 0 warnings, 0 errors.
- Full `dotnet test engine/ShoutingIguana.Engine.slnx`: Engine.Tests 1129 passed + 1 skipped (round-4 test, by design),
  Cli.Tests 20, Sidecar.Tests 174 (run before 9e17d9a; after it the affected classes were re-run green:
  ScoreStorageTests + AnalysisStateTests 31, sidecar ScoreTests/AnalysisState/Library 21).
- Bench 100k (ONCE, done; log scratch/score/bench/bench.log): Completed, every planted issue found; crawl 79.4 s
  (machine loaded; crawl code untouched), 1,273 pages/s, peak 198 MB, flat (-15 MB), analysis 22.8 s, 479 MB file.
  Crawl kept at scratch/score/bench/bench.sicrawl.
- readbench --gate run 1 (bench/readbench.log): ONE class failed under load: `text` "Title matches a common regular
  expression, by followed inlinks (no index)" 219/203/199 ms vs gate 190. Repeated alone 3x: 94-102 ms (baseline
  engine on its copy 71-124 ms, new on its copy 80-106 ms): load, not the change. Run 2 of the full gate in
  background (bench/readbench-2.log): must be seen green.
- Then: app checks again (typecheck, lint, format:check, test, build:sidecar, e2e), docs numbers
  (tools/d05_bench_docs.py "<readbench sentence>"), commit Docs, final report.

## FINAL (all verification done)

- Branch ui/score, 13 commits on ec4d2e7, last 319ff9d (Docs: bench numbers). Working tree clean. Nothing pushed.
- Seen green at the final state: Release build 0 warnings; `dotnet test` solution: Engine.Tests 1133 passed + 1 skipped
  (round-4 test), Cli.Tests 20, Sidecar.Tests 174; bench 100k once (every planted issue found, flat memory);
  readbench --gate passed on its second full run (first: one read over its gate under load, 94-102 ms when repeated);
  app: typecheck, lint, format:check, test (877 passed, 1 skipped, pre-existing), build:sidecar, e2e (62 passed).
- Large scratch copies (100k crawls, bench crawl, export tree) deleted; logs kept in scratch/score/bench and
  scratch/score/e2e-*.log, app-final.log.
- Processes: the si-sidecar and si.exe still running belong to the `links` worktree (not mine): left alone.
- Only the final report remains.

# ROUND 2 (orchestrator's follow-up, before ui/score merges)

Tasks: (1) FixFirstOrder: within a severity, siteWide checks first (catalog order among themselves), then count desc,
then catalog; update score.mjs + design code that orders "Fix first"; regenerate cases.json; test; contract "The score".
(2) site-hidden-from-search counts NOINDEX ONLY: hidden = noindex pages (as noindex-page counts); total = audited pages
less those canonicalised to another URL (my existing which-canonicals-count rule); strict majority; never when total 0;
detail "212 of 240 pages are noindex"; update rule, tests (+2 named cases), texts, plant, design doc row + "lies" row,
contract. VERSION stays 7.
(3) Load plain words for all 87 from scratchpad/plain-words/plain-words.json: generator script in repo beside the score
reference generator + JSON kept in repo as the data file; generate IssuePlainTexts.cs + PlainWords.cs; turn on the
all-87 test; list each template-test failure (don't loosen silently, don't rewrite texts; fix test only for the 3 rule
changes in reference-changes.json: plainLooksFor, tokens, Notices "can wait"); check site-hidden plain text vs rule;
edit docs/v2-ui-simple.md "Ten checks" per reference-changes.json + template/rules; sidecar test showing ?crawl= fills
visibly differently.
Then: Release build, dotnet test, app typecheck/lint/format:check/test/e2e, bench 100k once (+ base run in the same hour
if out of range). Report short.

## Round 2 status
- [x] 1 fix-first: DONE, 3 commits (a4b5da4 Design, 824717a Engine, 0480d89 Docs). Tested: engine FixFirst/Score tests
  (61 passed), Cli 20, sidecar Score/App 11. Scripts: tools/r2_01..r2_03. Full suite NOT yet run in round 2.
- [x] 2 noindex-only: DONE, 2 commits (c2b9bcf Engine, 9d2fcce Docs). Rule: noindex*2 > pages - variants; detail
  "212 of 240 pages are noindex" ("1 of 1 page is noindex"). A noindex page with a canonical elsewhere counts as noindex
  (literal reading of the brief; flag in the report). Whole Engine.Tests seen green after tasks 1+2: 1139 passed, 1 skipped.
  Scripts: tools/r2_04, r2_05.
- [x] 3 plain words: DONE, 3 commits (a81a33c Design doc, 6f9bb71 Engine+tests, 2779968 Docs). Data file +
  generate.mjs in engine/tests/ShoutingIguana.Engine.Tests/Analysis/PlainWords/ (node ... generate.mjs [--check]).
  New tests: PlainWordsDataTests (JSON==code, verbatim ten from the doc, limits, six Notices, doc examples),
  Support/RepoFiles.cs; all-87 test on; sidecar: ?crawl= sentences (70 characters / 2.5 seconds), later-reason null.
  Template failures among the 87: NONE in the template tests as written; my own category-title regex (3 words) failed on
  "Extras in search results" -> allows 4. Added: no digit in a title; sentences average <= 20 words. No test for "step
  starts with a verb" (68 of 263 steps start "If"; 11 of 31 in the design's ten). site-hidden plain text: what[0] and
  fix[2] lost their canonical clauses (nothing added). Doc edits beyond reference-changes: the categories/skip-reason
  example paragraph (no-sitemap words, pointer to the data file), a paragraph naming the data file under "Ten checks",
  contract's plainFix row ("a verb or If"). Mutation: doc line changed -> verbatim test fails; JSON changed -> data
  test fails + generate --check fails. Seen green so far: engine Plain*/IssueCatalog filter (43), Sidecar.Tests 175.
  Scripts: tools/r2_06..r2_11.
- [x] final checks DONE. Final commit 20579fc (Docs: bench after the three changes); 9 round-2 commits on 319ff9d;
  working tree clean; nothing pushed.
  - Bench 100k ONCE (at 2779968, the commit before the docs-only last one; log scratch/score/bench/bench-r2.log, .json):
    Completed, every planted issue found; crawl 56.8 s (in the 36-58 s range, so no base run), 1,779 pages/s, peak
    193 MB, flat (+1 MB), 479 MB file, analysis 19.6 s peaking at 331 MB (earlier runs 253-327 MB; one run, not repeated).
  - At 20579fc: Release build 0 warnings (non-incremental at 2779968, incremental after the docs commit); dotnet test
    slnx: Engine.Tests 1147 passed 0 skipped, Cli.Tests 20, Sidecar.Tests 175; app typecheck, lint, format:check, test
    (877 passed, 1 skipped pre-existing), build:sidecar, e2e 62 passed (logs scratch/score/app-r2.log, e2e-r2.log,
    dotnet-test-r2.log).
  - Garden copy (scratch/score/crawls/r2/garden.sicrawl) re-analysed with the final Release CLI: 85 Good; fix first
    internal-client-error, broken-internal-links, image-too-large, description-duplicate, title-duplicate; ifFixed 98.
  - No process of mine left running; ports 59874-59877 free.
  - Only the final report remains.
