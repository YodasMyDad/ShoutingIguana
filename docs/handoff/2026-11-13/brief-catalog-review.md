# Brief: fresh-eyes review of the catalog's fixes (engine), read-only

Review branch `ui/catalog-fixes` of Shouting Iguana v2 (a desktop SEO crawler: a .NET 10 engine in `engine/`, a
sidecar API, a React app in `app/`): `git diff a782baf..5f5b83d`, 8 commits. You did not write it and nobody has
reviewed it. It is small: keep the review short and sharp.

- Yours: `C:\Users\LeeMessenger\AppData\Local\Temp\claude\e2e5beaddde4c070\8e7045a4-9ede-460a-be29-971bc406a6c0\scratchpad\catalog-review\`
  (make a `tmp` folder there for `TEMP` and `TMP`; keep a `checkpoint.md`).
- What was asked: `...\scratchpad\brief-catalog-fixes.md`. What the builder says it did: `...\scratchpad\catalog-fixes\checkpoint.md`.
  Claims to check, not facts.

Work in a worktree of your own: from `C:\Projects\ShoutingIguana`,
`git worktree add --detach C:\Projects\ShoutingIguana\.claude\worktrees\catalog-review 5f5b83d`. **Edit, commit or
push nothing in any other worktree.** In your own you may build, test and run anything. Copy crawls before you open
them; never edit `%LOCALAPPDATA%\ShoutingIguana\dev-crawls`. `si testsite` is the only site you may crawl. Other
agents are at work on this machine: one heavy thing at a time. **Stop only processes you started, by their own
process id.** Never run `dotnet build-server shutdown`. Read `CLAUDE.md` (repo root) for the engine rules.

## What to look for

Real defects, each with a concrete failure scenario:

1. **`site-hidden-from-search`**: fires only when the home page (`UrlTable.HomeId`: the page the start URL ends on)
   is itself noindex AND a strict majority of self-standing pages are noindex. Wrong home page (start URL redirects
   across hosts or www/bare, to a non-page, by meta refresh, a chain, a loop; the home page blocked by robots.txt,
   erroring, not HTML; a crawl started on a deep URL; a resumed or stopped crawl; noindex by header
   `X-Robots-Tag` against meta; `none`; a bot-specific directive). False positives cost 60 points of the score:
   hunt for one. Its `subject` changed from `url` to `page`: what else reads `subject` (shares, counts, the score,
   the report, the app's wording)? Do its technical and plain texts say what it does?
2. **`charset-missing` and the byte-order mark**: the fact is captured at fetch and kept in `PageFacts`. UTF-8,
   UTF-16 LE/BE, UTF-32 marks; a mark after whitespace (not a mark); compressed bodies (the mark is in the
   decompressed bytes); a body cut at the crawl's size limit; a mark that contradicts the header's charset; what is
   stored in the crawl file and through `Storage/` only; a crawl made before the change (no fact: still flagged,
   as the text now says); re-analysis; the inspector.
3. **`blocked-by-robots` skipped** when the crawl ignored robots.txt: the new skip reason `robots-txt-ignored`
   everywhere skip reasons are listed (catalog, plain words, API, the report, the app's types and mocks); a crawl
   resumed with the setting changed; the score when a check is skipped rather than passed. The builder says the
   three `robots-txt-*` checks have the same fault and were left: confirm or refute, with what each reads as.
4. **`canonical-to-redirect` naming the final URL**: chains, loops, a redirect to an external URL, to a URL not
   fetched, a redirect past the crawl's redirect limit, relative `Location`; the detail's words ("redirects (301
   Moved Permanently) to", "after 3 redirects, ends at") against the real chain; the cost (a lookup per flagged
   page) on a crawl where it is flagged on every page: measure on a 100k-page case if you can make one.
5. **The eight text changes**: each against its rule's code (does the new text now say exactly what the rule
   does?), and each check's plain words still agreeing with it. Four titles changed: anything in `app/` or the
   docs still quoting the old title.
6. **`AnalysisFreshness.VERSION` 8**: stale analyses are offered "Analyse again" and nothing reads a version-7
   analysis as current; the app's e2e setup and dev script.
7. **The planted cases and the bench**: the expected counts are derived, not adjusted to match; nothing else newly
   flagged on the synthetic site. Tests that actually test; anything weakened to get green.
8. Conventions: `.ConfigureAwait(false)` on every engine await, models one per file, no issue id renamed.

Verify each finding before reporting it (a failing test or a script). Mark each CONFIRMED or PLAUSIBLE. No style
preferences, no speculation reported as a defect.

## Report back

A ranked list, most severe first, at most 15: severity, file:line, defect, failure scenario, CONFIRMED or
PLAUSIBLE, one-line fix. Then the verdict on the `robots-txt-*` checks and a short "looks right" note. **Give the
full list in your final message** (you can't write a file named `findings.md`; the orchestrator saves it). Put
proof scripts in your folder and name them. When done, stop your processes and remove your worktree
(`git worktree remove --force` on your own folder only).
