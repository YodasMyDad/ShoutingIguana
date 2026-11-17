# Step 3b, part 1: fix what the real-site check audit found, and write docs/v2-check-audit.md

## Inputs (read all three; they cite file:line and url ids)
`C:/Users/LeeMessenger/AppData/Local/Temp/claude/e2e5beaddde4c070/a539aa23-e856-44c1-bd21-ca484e8740e2/scratchpad/audit3b/`
`accuracy-indexing.md`, `accuracy-responses.md`, `accuracy-onpage.md` (+ the shared method in `accuracy-brief.md`;
re-check scripts in `scratch-onpage/`). Crawls (read-only; analyse copies only) in `audit3b/crawls/`.
Plan for step 3b: `docs/v2-rebuild.md` "Step 3b, the check audit" (verdict per check in `docs/v2-check-audit.md`:
right, fixed, reworded, severity changed or dropped; each fix gets a planted case and a test seen failing without it).

## Where and rules
Create worktree `C:/Projects/ShoutingIguana/.claude/worktrees/3b-accuracy` on new branch `ui/3b-accuracy` off
`ui/step-3`: `git -C C:/Projects/ShoutingIguana/.claude/worktrees/ui-step-3 worktree add -b ui/3b-accuracy C:/Projects/ShoutingIguana/.claude/worktrees/3b-accuracy ui/step-3`.
Uppercase `C:/` paths. CLAUDE.md engine rules (only Storage writes; HTML parsed once -> PageFacts; checks in
IssueCatalog; never rename ids; planted case + expected count for every check change: the tests compare the whole
issues table). Small logical commits, each ending `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. NEVER
push. Run `bash docs/handoff/2026-11-13/tools/verify-engine.sh <worktree> <logDir>` at the end (green, bench gate).
Simple mode's words live in the plain-words file the audits cite (`plain-words.json`); keep Advanced and Simple in step.
If the app has unit tests/snapshots over those words or severities (score caps etc.), update them and run them from an
uppercase `C:/Projects/...` path (lowercase `c:` breaks Vitest).

## Other branches in flight (avoid collisions)
- `ui/3b-evidence` changes check DETAIL strings (e.g. "78 characters, over 60"), groupings/targets of the image checks,
  adds evidence fields to IssueDefinition. Don't change detail formats or groupings here.
- `ui/3b-polite` owns 429/rate-limit handling AND the wording/severity of `internal-blocked` and the
  "blocking the crawler" notice. Don't touch those here.

## Fix (each with a planted case on the synthetic site where it changes what's flagged, and a test seen failing)
Logic:
1. CanonicalRules.cs:88 — a canonical target that refused the crawler (FetchOutcome.Blocked / 429) is unverified, not
   non-indexable (as SitemapRules.cs:65 already treats it); drop "or a firewall" from the text (IssueCatalog.cs:390).
2. HreflangRules.cs:77-78 — same Blocked fix; an alternate on another site disallowed by THAT site's robots.txt isn't
   flagged (mirror CanonicalRules.cs:87-88): only internal robots.txt blocks count.
3. LinkRules.cs:51 links-to-redirects — don't flag links whose target answers a temporary redirect (302/303/307): the
   linked URL is the right one (Shopify's /account/login). Update its What text.
4. RedirectRules.cs:137 redirect-temporary — don't flag a temporary redirect whose target is on another site (hosted
   login / checkout). Update its What text.
5. CrawlAnalyzer.cs:385-413 orphan-page — skip (with a skipped-check reason, as the code does elsewhere) when internal
   pages weren't read (ended Blocked, Timeout, NetworkError, rate-limited): their links are unknown.
6. LinkRules.cs:18 empty-anchor-text — a page's links to itself are tested for empty text too (the logo).
7. RichResultRequirements.cs:12-23/:37 — schema.org subtypes (Store, Restaurant, MusicEvent, NewsArticle...) inherit
   their parent's required properties; plant one.
8. HtmlRules.cs:26 charset-missing — a `<meta charset>` beyond the first 1024 bytes doesn't count when the HTTP header
   has no charset (needs the byte offset as a PageFacts field from the single parse); plant one. Fix the comment at
   HtmlRules.cs:7.
Severity: sitemap-error Error->Warning (IssueCatalog.cs:470); description-duplicate Warning->Notice (:609);
structured-data-invalid Error->Warning (:857). Update score tests/expectations deliberately.
Words (Advanced + Simple): canonicalised-page Simple (pagination: each page in a series should name itself);
not-in-sitemap (login/register/password/basket pages belong out of search, not in the sitemap); hreflang-to-non-indexable
(drop the firewall aside; Simple bullet adds robots.txt); sitemap-missing Simple (say what the recheck adds);
title-too-long Simple title "Titles that may be cut off in search results"; description-too-long Simple "Search
descriptions that may be cut off"; near-duplicate-content drop the "24 of the 256 bits" sentence (IssueCatalog.cs:681);
structured-data-missing-required: verify the claim that Google stopped FAQ rich results "in May 2026"
(IssueCatalog.cs:873) against Google's own documentation with a web search — keep it with the right date if confirmed,
else remove the date; title-too-short: "Skip pages nobody searches for, such as login and basket pages.";
broken-external-links What: links refused by the other site's bot protection or its robots.txt aren't counted;
security-headers-missing Why: browsers already default to a safe Referrer-Policy (optional, if it reads well).
Docs: delete the stale `charset-missing` byte-order-mark entry (docs/v2-rebuild.md ~1045-1047; verify it's stale).
NOT doing (say so in the audit doc): cookie-banner words in word count (PageWalker.cs:44; would re-baseline);
nofollow-page severity split. New-check ideas from the audits go to the doc's "new checks" list, not built here
("robots.txt doesn't name your sitemap"; "paginated pages canonicalised to page 1").

## Write docs/v2-check-audit.md
Method (three sites, dates, crawl settings: -c 2, --rate 2, --respect-crawl-delay, --store-html, max 5,000 URLs; how
flags and misses were verified); a table of all 87 checks: id | fired (aptitude / jitsunami / garden) | wrong flags |
misses | verdict (right, fixed, reworded, severity changed, dropped; "not seen on a real site" where it fired on none) |
the change and commit. Then: what wasn't done and why; the new-check ideas; the jitsunami rate-limit note (product
pages unaudited because of 429s; fixed on `ui/3b-polite`, re-audit after it merges).

Report: commits, test counts, verify-engine summary lines, and anything not done.
