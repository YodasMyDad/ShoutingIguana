# Step 3b, part 2: every check shows its evidence — engine + sidecar brief

## Why (the owner, 2026-11-17)
"some screens don't show the correct information for the user to be able to make actionable decisions ... the broken
links never showed what page they were on ... a large images UI ... does not show the image size OR where the image is
shown! For every single view / UI it must be actually useful ... easy to scan, understand and action and the user fix
their site."

The bar for every one of the 87 checks: from the check's own views (Advanced Issues, Simple's problem page, the
workbook and the HTML report), a site owner can say (a) WHAT is wrong: the actual value against the limit; (b) WHERE to
fix it: the page; for a resource every page it's on; for a link the linking page + anchor/alt + position; template vs
one-off; (c) HOW: the catalog's fix matches what's shown. Never make them open the inspector or the source to find it.

## Read first
- `C:/Users/LeeMessenger/AppData/Local/Temp/claude/e2e5beaddde4c070/a539aa23-e856-44c1-bd21-ca484e8740e2/scratchpad/audit3b/evidence-inventory.md`
  — section 1 (what each view shows today, with file:line), section 2 (per check: what's shown, what's needed, the gap,
  whether stored), section 4 (the cross-cutting patterns). This brief turns section 4 into a contract.
- `CLAUDE.md` (repo root) engine rules; `engine/README.md`; `docs/v2-sidecar-api.md` (the API's own doc, keep it true).

## Where
Worktree `C:/Projects/ShoutingIguana/.claude/worktrees/3b-evidence`, branch `ui/3b-evidence` (off `ui/step-3` at
aa2802b). Use uppercase `C:/` paths in shells (a lowercase `c:` cwd breaks Vitest). Commit locally, small logical
commits, message style as in `git log`, each ending with the line
`Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. NEVER `git push`. Don't touch other worktrees.
Another branch (`ui/3b-accuracy`, later) rewords catalog texts and fixes rule logic: don't reword What/Why/Fix texts
here; change rule code only where evidence needs it.

## The contract (engine + sidecar). Decide the details from the inventory; keep names consistent with existing code.
1. **Evidence per check.** `IssueDefinition` gains an ordered list of evidence fields: URL column ids from
   `Storage/UrlColumns.cs` (existing: title, titleLength, description, descriptionLength, h1, h1Count, wordCount,
   sizeBytes, contentType, metaRobots, xRobotsTag, canonicalUrl, redirectTarget, finalUrl, indexability...) plus the
   new ones below. Fill it for every check where the inventory says a page fact is needed (e.g. title-* -> title +
   titleLength; description-* -> description + descriptionLength; h1-duplicate -> h1; soft-404 -> title, h1, wordCount;
   noindex/nofollow-page, site-hidden-from-search, snippet-blocked -> metaRobots, xRobotsTag; sitemap-non-indexable,
   sitemap-url-other-host -> indexability + finalUrl/canonicalUrl; canonicalised-page -> canonicalUrl; etc.). Serve it
   in the check's catalog entry from the sidecar (`evidence: [...]`) and make sure `/rows` serves every listed column.
   Duplicate clusters (title/description/h1-duplicate): the group is named by the shared TEXT, not by the first page.
2. **New URL columns** (on `/rows` and in the URLs grid's column registry so any view can use them):
   - `redirectChain`: the hops from this URL, each URL + status, to the end (or the loop / the error), from the stored
     `redirect_url_id`s (the inspector's chain logic already builds it: reuse it). For redirect-chain, redirect-loop,
     redirect-broken, redirect-temporary, redirect-downgrade.
   - `robotsRule`: for a URL robots.txt blocks, the matching line ("Disallow: /cart/", line number, which robots.txt):
     `Storage/RobotsReader.cs` already finds it for the Robots screen; reuse. For blocked-by-robots, internal-blocked.
   - `usedOn`: the number of distinct pages that reference this URL by ANY link kind (hyperlink, image, script, css,
     iframe, canonical...), so an image/CSS/JS shows where it's used (today inlinks counts hyperlinks only, and reads 0
     for an image). Must stay fast on the 100k bench (use/extend the link summary; mind `LINK_SUMMARY_VERSION`).
   - `pixelWidth`, `pixelHeight`: an image's intrinsic size (new collection, item 5).
3. **Richer targets** (`Storage/Models/IssueTarget.cs`, `FoundOnReader` targets, the `/issues/{checkId}/targets`
   endpoint) and the **group's related URL** (`Storage/IssueGroupReader.cs`: a group named by a URL, RelatedUrl
   grouping): each carries what the URL is: status, `sizeBytes`, `contentType`, `pixelWidth/Height`, `finalUrl` +
   final status when it redirects, `indexability` + reason, `canonicalUrl` when canonicalised elsewhere; and for the
   page's link to it: kind, text (anchor / alt / hreflang code), position, `fragment`, flags (nofollow, image link,
   from HTTP header), how many links the page has to it; for an image, the page's `width`/`height` attributes as written
   (ImageFact) so "shown at 400×300, file is 4000×3000" is possible. Groups must be sortable by impact where it means
   something: image-too-large by size (largest first, size × pages as a tie-break); otherwise by pages, desc.
4. **Image checks name each image** (image-missing-alt, image-alt-too-long, image-missing-dimensions): make them
   `References = Targets`, `Grouping = RelatedUrl`, each page's issue targeting its offending images, exactly as
   image-too-large / image-broken already work, so a logo without alt on 265 pages is ONE group with the existing
   "one fix / template" signal. Detail stays a count. (Grouping/targets change what's stored: bump
   `AnalysisFreshness.VERSION`; ids never change.)
5. **New collection, small:**
   - image intrinsic dimensions: in the fetcher, for image responses read only the first bytes needed (PNG IHDR, GIF,
     JPEG SOF0-3/5-7/9-11/13-15 scanning segments, WebP VP8/VP8L/VP8X; AVIF `ispe` best effort; SVG none) without
     buffering the whole body; store on `responses` (new nullable columns: bump `CrawlSchema.VERSION` the way the code
     already handles schema versions, and make sure older crawl files still open with the values null — find how).
   - the element that ended `<head>` early, with its line: a `PageFacts` field set by `Parsing/PageWalker.cs` (HTML is
     parsed once). Used by canonical-outside-head and hreflang-outside-head details.
6. **Details that name the thing, not just count it** (keep each detail short, one line, ~120 chars, texts truncated
   with "…"): title-multiple `2 titles: "A" · "B"`; description-multiple; h1-multiple `3 H1s: "A" · "B" · "C"`;
   canonical-multiple names each URL and where declared (tag / HTTP header); hreflang-duplicate-code names the code and
   both URLs; canonical-/hreflang-outside-head names the element that closed head and its line; broken-fragment-links
   names the missing `#fragment`(s); redirect-loop names the hops. Where a detail is a measurement, give the value and
   the limit ("72 characters, over 60"). Mind PageDetail grouping: a detail that now varies per page must not split a
   template group that should stay one (check `IssueGroupReader` FaultOf).
7. **Template hint for ungrouped checks**: a page-subject check with grouping None that flags at least 90% of the
   audited pages (and at least 10) says so (e.g. `likelyTemplate` on the check's count/summary), so the UI can say
   "probably your site's template: one fix".
8. **Default order of a check's rows: by importance** — inlinks (desc), then depth, then URL — wherever rows/groups'
   pages are listed (Issues rows, a group's pages, exports, report examples). Add `inlinks`, `depth`, `indexability`
   availability to those reads if missing.
9. **Exports follow**: the workbook's per-check sheet gets the check's evidence columns and the richer target columns
   (header named by what they are: "Image", "Links to", "Canonical", not always "Links to"; image size/px; anchor and
   position for links); the HTML report's tables show the evidence and where to fix it (targets too, not only sources).

## Rules (non-negotiable)
- CLAUDE.md engine rules: only `Storage/` writes the crawl file; HTML parsed once (add to `PageFacts`); every check in
  `IssueCatalog`; never rename an issue id; central package versions; warnings as errors.
- Every behaviour change has a test seen failing without it. Any change to what a check flags/targets/details on the
  synthetic site updates its planted cases / expected tables (the tests compare the whole issues table) — update them
  deliberately, never loosen a test to pass.
- Keep the 100k bench gate green: `bash docs/handoff/2026-11-13/tools/verify-engine.sh <worktree> <logDir>` (build,
  test, `si bench --pages 100000`, readbench gate). Run it at the end and paste the summary lines in your report.
- Update `docs/v2-sidecar-api.md` for every API change (shapes and examples).
- Don't change the React app in this task (another agent does it from your contract). Do keep the app's TypeScript
  API types in sync ONLY if the repo generates/keeps them next to the sidecar (check `app/src/api`); if they're
  hand-written in the app, leave them and list the changes in your report.

## Two stages
**Stage A (stop and report after it):** write the contract into `docs/v2-sidecar-api.md` (new/changed endpoints,
fields, examples) plus a per-check table (in a new doc `docs/v2-evidence.md`): check id | evidence columns | target
facts shown | detail format before -> after | sort. Commit it. Report the doc paths and the 10 decisions you're least
sure of. Then wait: the caller reviews and replies before you implement.
**Stage B (on the caller's go):** implement, test, verify, commit. Report: commits, test counts, bench/readbench
summary lines, anything not done and why.
