# Step 3b: the fix loop — compare with the previous crawl, and recheck a problem's pages (DESIGN, stage A)

## The owner's decision (2026-11-17)
"Compare + recheck": every check, the score and every list compares with the site's previous crawl (New, Fixed,
Fewer, score +/-), plus "Recheck these N pages" on a problem, which re-fetches just those pages and says "4 of 5
fixed". No manual "mark as fixed": a recheck proves it.
Why: the view audit found the fix loop breaks at its last step — after a recheck nothing shows what got fixed, and
confirming one fix means re-crawling the whole site (37 min on a 4,500-URL site). Read
`C:/Users/LeeMessenger/AppData/Local/Temp/claude/e2e5beaddde4c070/a539aa23-e856-44c1-bd21-ca484e8740e2/scratchpad/audit3b/ux-views.md`
(findings 1 and 5, and the flow-break section) first.

## Context to read
`CLAUDE.md`; `engine/README.md`; `docs/v2-rebuild.md` (decisions; crawl comparison and list mode were "later");
`docs/v2-ui-ia.md`, `docs/v2-ui-simple.md` (Simple's "Fixed it? Check again" card, What to fix, Summary/score),
`docs/v2-sidecar-api.md` (crawl library, analysis, issues endpoints); the crawl library (how crawls of one site are
grouped: "a site to a row"); `Storage/` (one SQLite file per crawl; only CrawlWriter/IssueWriter write; never both at
once), `Analysis/` (CrawlAnalyzer, ScoreCalculator, AnalysisFreshness), `Crawling/` (CrawlSession, list of URLs to
fetch, the retry pass). In flight on other branches (don't depend on unmerged code, but don't contradict it):
`ui/3b-evidence` (docs/v2-evidence.md: evidence columns, targets), `ui/3b-polite` (adaptive pacing), `ui/3b-accuracy`.

## Design questions to answer (with a recommendation each, grounded in the code)
1. **Which crawl is "previous"?** Same site (how the library decides "same site": origin? start URL?), latest earlier
   completed crawl; what if settings differ (scope, limits, user agent) so the comparison is unfair — detect and say so.
   Can the user pick another crawl to compare with (Advanced)?
2. **What is compared, and where it's computed.** Per check: count then/now, and per flagged item New / Fixed / Still
   (by URL + check + the fault key the groups use — think about URLs that weren't crawled this time: "not checked"
   isn't "fixed"). Score then/now and per score line. Totals on Overview/Summary. Compute in the engine (a reader that
   attaches the other crawl file read-only — SQLite ATTACH, or reads both) and serve through the sidecar; cost on a
   100k crawl must stay within the read budgets in docs (find them).
3. **Recheck these N pages.** Re-fetch only the problem's pages (and what's needed to judge them: e.g. for a broken
   link, the linking pages; for a large image, the image and the pages it's on), re-run the checks, and report
   "4 of 5 fixed". Decide: update the same crawl file in place (CrawlWriter re-records those URLs, then a full
   re-analysis, marking the crawl "rechecked N pages on <date>") vs a new small crawl file compared with the original.
   Consider site-wide checks (duplicates, orphans, sitemaps) that a partial refetch can't settle — say which checks a
   recheck can confirm and how the UI words the rest. Respect the polite pacing (another branch). Concurrency with a
   running crawl. What the library shows afterwards.
4. **UI, both modes.** Simple: Summary score change ("+6 since 3 Nov"), What to fix with Fixed / New / Fewer, the
   problem page's "Recheck these 5 pages" button -> progress -> "4 of 5 fixed; 1 still has it: /x". Advanced:
   Overview deltas, Issues check list with a delta column and "New / Fixed" filters, rows with New/Fixed/Still chips and
   a "Fixed since last crawl" view, the compare-with picker. Report/export: a "Changes since <date>" section. Follow
   the owner's look (framed workspace; current = neutral fill + bold, no accent rule; strong segmentation). Words in
   Simple are plain (glossary in docs/v2-ui-discovery.md).
5. **Edge cases:** first crawl of a site (no comparison: say nothing, no zeros), interrupted/stopped crawls, crawls
   from an older engine/analysis version (AnalysisFreshness), deleted previous crawl, very different sizes, a check
   that didn't exist last time (new checks are coming: "New check" not "New problem").

## Deliverable (stage A): docs only, then STOP
Worktree `C:/Projects/ShoutingIguana/.claude/worktrees/3b-fixloop`, new branch `ui/3b-fixloop` off `ui/step-3`
(`git -C C:/Projects/ShoutingIguana/.claude/worktrees/ui-step-3 worktree add -b ui/3b-fixloop C:/Projects/ShoutingIguana/.claude/worktrees/3b-fixloop ui/step-3`).
Write `docs/v2-fix-loop.md`: the answers above, the engine/storage design, the API contract (endpoints, shapes,
examples) to be added to `docs/v2-sidecar-api.md` (write it there too, marked "(fix loop)"), the UI per screen and mode
(ASCII sketches welcome), the performance plan, the test plan (synthetic site: two crawls with planted changes —
`si testsite` options for a "fixed" variant), and an implementation plan in stages (engine, sidecar, UI) sized for
agents. Commit (message ends `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`), never push. Report: the doc
paths, the 8 decisions you're least sure of, and the plan's stages. Then stop: the caller reviews before any code.
