# Checkpoint: catalog fixes (engine, step 3 item 4)

Brief: `..\brief-catalog-fixes.md`. Worktree `C:\Projects\ShoutingIguana\.claude\worktrees\catalog-fixes`, branch
`ui/catalog-fixes` (from a782baf). Never push/merge. Never rename a function, type or issue id. One heavy thing at a time.
Scratch (this folder): logs, `tmp\` (TEMP/TMP for app runs). Ports: dev 63874, sidecar 63875, preview 63876, e2e 63877,
storybook 7106.

## Read (done)

- Brief, status doc item 4, plain-words notes, review JSON (57 entries, all on plain fields), checkpoint-score.
- The review's own report isn't in the repo; its checkpoint (old scratchpad `f209576d...\plain-words-review\checkpoint.md`)
  lists "Engine findings beside the texts": blocked-by-robots passed-not-skipped, canonical-to-redirect no final URL,
  empty-anchor-text Why names social icons (off-site, untested), hreflang-missing-self Fix says "canonical URL".

## My list of the twelve (9 from notes.md items 1-8, item 6 being two; 3 the review added)

1. robots-txt-missing title "No robots.txt" (fires on 401/403, too many redirects, other origin only)
2. charset-missing: BOM false positive the Why admits (behaviour + text)
3. near-duplicate-content description says "the default" twice
4. image-missing-alt title wider than rule (empty alt passes)
5. orphan-page title wider than rule (sitemap pages only)
6. redirect-broken title (also fires with no usable Location)
7. blocked-by-robots text (also covers robots.txt unreadable)
8. hreflang-missing-self fix says "as its canonical URL"; rule compares URL as crawled
9. security-headers-missing: Subject page + reported once (NOT a text fault; reportedOnce already settles it) -> check, report
10. empty-anchor-text Why names "social media icons" (external, not tested)
11. blocked-by-robots passed, not skipped, when robots.txt ignored (behaviour)
12. canonical-to-redirect no final URL (behaviour + description)

## Plan / status

- [x] P1 site-hidden-from-search: c5721db. Rule = home page (UrlTable.HomeId, the page the start URL ends on) observed
      noindex AND noindex*2 > pages - variants. No home page read -> nothing. Subject Url -> Page. Plants: HiddenFromSearch
      (home + products noindex: fires), NoindexProducts (home indexable: must not). Mutation seen failing (5 tests).
- [x] P2a charset BOM: 8b9c2e7. CharsetDecoder.ByteOrderMarkOf -> FetchResponse.ByteOrderMark -> HtmlPageParser.Parse(..,
      byteOrderMark) -> PageFacts.ByteOrderMark -> HtmlRules. Plants charset-bom, charset-bom-utf16. 3 mutations seen failing.
      Old crawls: no fact, still flagged until recrawled (text says so).
- [x] P2b blocked-by-robots skip: b1ba061. Reason `robots-txt-ignored` (16 skip reasons). Mutation seen failing.
- [x] P2c canonical-to-redirect final URL: f03c89e. CanonicalRules(urls, readUrl). Plant canonical-chain + moved-twice.
      Mutation seen failing.
- [x] P3 technical texts: 31341df (8 texts + IssueCatalogTests.TheTextsSayWhatTheirRulesDo, seen failing before; the
      three texts fixed in earlier commits mutated back one by one, each seen failing).
- [x] P4 VERSION 8: b677ce2 (test seen failing before the bump). Docs: 4ec2c57 (API doc new last section "Engine
      changes the catalog's fixes made" + skip table + head bullet; README; v2-ui-simple.md two rows).
      NOT touched on purpose: docs/v2-rebuild.md status section (the orchestrator's).
- [ ] P5 verify. DONE: full `dotnet test` slnx at b677ce2 = Engine 1172, Sidecar 176, Cli 21, all passed (log full-1.log).
      Bench 100k ONCE at 4ec2c57 (engine = b677ce2): Completed, every planted issue found, 34.1 s, 2,963 pages/s, peak
      194 MB, flat +1 MB, analysis 11.5 s peak 324 MB (bench/bench.log).
      Garden: before (baseline CLI built from `git archive a782baf` in scratch/baseline) vs after: issues CSV identical
      (933 rows), score 85 -> 85, same 4 skipped checks, same notes (garden/).
      RUNNING: verify-app.sh on ports 63874.. / 7106, logs in verify-app/ (summary.txt). App edits UNCOMMITTED: 7 files,
      mock title 'Orphan page' -> 'Orphan page in a sitemap' (tests + 2 stories). Commit as "App: ..." once green.
      THEN: final Release build + dotnet test once more only if anything in engine/ changes; final report.

## Verified so far

- See P5. Every fix's test seen failing without it (mutations listed above).

## Tools here

- `t.sh <log> <Engine|Sidecar|Cli> <filter>` build + filtered tests. `mut.py apply|restore`. `stage.py <file> <needle>`
  stages only matching hunks. Edit scripts p1_*.py / p2_*.py (already applied; don't re-run).

## Gotchas

- Mutation checks must restore files with a fresh timestamp (mut.py does).
- No `\` / mixed quotes in bash heredocs: write scripts with the Write tool.
- Tests read repo files at run time (plain-words.json, docs): don't edit sources while a test run is going.

## FINAL (all verification done, 2026-11-12)

- Branch ui/catalog-fixes, 8 commits on a782baf, last 5f5b83d. Working tree clean. Nothing pushed.
- Seen green at 5f5b83d: Release build --no-incremental 0 warnings; dotnet test slnx Engine 1172, Sidecar 176, Cli 21
  (final.summary.txt). App (verify-app/summary.txt, at the same tree but for one prettier reformat, re-checked after):
  ci, typecheck, lint, format:check (failed once on my long line, fixed, green), test 1055 passed + 1 skipped, scripts 38,
  stories 208 x (2 themes, forced, reduced), build:sidecar, e2e 86 passed.
- Bench 100k once (engine sources = HEAD's): Completed, every planted issue found, flat memory.
- Garden: no difference (933 issue rows identical, 85 -> 85).
- Only the final report remains.

# ROUND 2: the review's eight findings (../catalog-review/findings.md)

Commits on 5f5b83d: 6ce2206 (F1 robots skips), 7b20419 (F4 start-not-root), 8ea2668 (F3 empty charset), 7d17d60 (F5
prepared lookup), 2c84acb (F7 inspector charset), bd7f75b (F2 unchecked end), f5fcc1a (Docs, F6), e6aef12 (Design, F8),
99b9ca7 (plant canonical-refused removed again: it moved every crawl's blocked count). Commits from round 2 end with
Opus 5.5 co-author (from f5fcc1a; earlier round-2 ones say Fable, written before the coordinator's message).
- Every engine fix seen failing by mutation (mut-r1-*, mut-r4-*, mut-r5, mut-r2a/b, mut-r3, mut-r7 logs).
- At 99b9ca7: Release --no-incremental 0 warnings; Engine 1179, Sidecar 176, Cli 21 all passed (r-final2.summary.txt).
- Garden: issues CSV identical to the a782baf baseline, score 85 both (garden/issues.diff empty).
- Bench once at 99b9ca7: Completed, every planted issue, flat (bench/bench-2.log).
- App verify running: verify-app-2/.
- App verify at 99b9ca7: every step exit 0 (verify-app-2/summary.txt). Round 2 done; only the report remains.
