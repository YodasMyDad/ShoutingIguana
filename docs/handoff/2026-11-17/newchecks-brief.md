# Step 3b: the new checks — triage and specification (stage A, docs only)

## The owner's decision (2026-11-17)
"All of them": every old-app backlog check and every Screaming Frog check that needs only data we already fetch.
Each will be built with a planted case on the synthetic site, its expected count, and a test seen failing.

## Sources (read them)
- `docs/v2-ui-discovery.md` "## Backlog: checks from the old app" (from data v2 already stores; needing parser or
  storage changes; dropped — the dropped ones stay dropped).
- `docs/v2-comparison.md` "### Theirs we lack: from the HTML and headers we already fetch (100)" (a table with their
  names, our nearest, and a verdict per row), "In part (7)", "For the check audit".
- The real-site audit's new-check ideas: "robots.txt doesn't name your sitemap" (Notice); "paginated pages
  canonicalised to page 1" (Warning); in
  `C:/Users/LeeMessenger/AppData/Local/Temp/claude/e2e5beaddde4c070/a539aa23-e856-44c1-bd21-ca484e8740e2/scratchpad/audit3b/accuracy-*.md`.
- How checks are built: `engine/src/ShoutingIguana.Engine/Analysis/IssueCatalog.cs` (definition shape, What/Why/Fix
  style), `Analysis/Rules/*`, `Analysis/IssuePlainTexts.cs` + the plain-words file (Simple's words), `Parsing/PageFacts`
  + `Parsing/PageWalker.cs` (what the single parse keeps), storage schema `Storage/CrawlSchema.cs`, the synthetic site
  (`TestSite`, `SyntheticSite.Plants.cs`) and how tests compare the whole issues table.
- The evidence contract in flight on branch `ui/3b-evidence`: `git -C C:/Projects/ShoutingIguana/.claude/worktrees/3b-evidence show HEAD:docs/v2-evidence.md`
  (or the latest commit touching it): every new check must name its evidence columns / target facts the same way.

## Deliverable
Worktree `C:/Projects/ShoutingIguana/.claude/worktrees/3b-newchecks`, new branch `ui/3b-newchecks` off `ui/step-3`
(`git -C C:/Projects/ShoutingIguana/.claude/worktrees/ui-step-3 worktree add -b ui/3b-newchecks C:/Projects/ShoutingIguana/.claude/worktrees/3b-newchecks ui/step-3`).
Write `docs/v2-new-checks.md`:
1. A deduplicated list (the backlog and the comparison overlap; merge them; note "covered in part" ones as changes to
   an existing check rather than new ids). For each: proposed id (kebab-case, never reusing an existing id), category,
   severity (Error/Warning/Notice, with a one-line reason, mindful that Simple's score treats Errors heavily), subject
   (page/URL) and grouping/references in the catalog's terms, the precise rule and threshold (cite the standard:
   Google Search Central, HTML spec, MDN, schema.org — mark anything you couldn't verify), the data it needs (already in
   PageFacts/responses/links/headers — cite the field — or a small PageFacts addition from the single parse, or storage
   changes), evidence columns / target facts per the evidence contract, the planted case on the synthetic site and its
   expected count, the Advanced title and the Simple title in plain words, and its value to a site owner (High / Medium
   / Low).
2. A list of what is NOT built and why (needs a browser, Lighthouse, GA/GSC, AMP, AI, a dictionary; or "dropped" in the
   backlog; or a duplicate of an existing check).
3. Build batches for implementing agents: ~8-15 checks per batch, grouped to minimise file conflicts (by rules file /
   category), with the PageFacts additions each needs, ordered High value first. Note anything that changes existing
   checks' flags (the whole-table tests) or the 100k bench's planted counts.
Commit (message ends `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`), never push. Docs only — no code.
Report: counts (new / changes to existing / not built), the batches, and the 10 rules you're least sure of.
