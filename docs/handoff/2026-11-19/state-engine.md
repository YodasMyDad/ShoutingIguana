# Engine lane: state at the wrap-up (2026-11-19)

Branch `ui/ux-engine`, worktree `C:/Projects/ShoutingIguana/.claude/worktrees/ux-engine`, base `4a7fab91`, head
**`c71fe5bd`**. `git status` clean. Nothing of this lane is running (sidecar, Vite, test site and the read benchmark
stopped; ports 49924 to 49927 and 5970 free). The scratch worktree of the old engine is removed.
Scratch: `…/scratchpad/engine-fix/` (`checkpoint.md`, `logs/`, `repro/`, `flows/`, `shots/`, `tools/q.py`).
`AnalysisFreshness.VERSION` on the branch: **21** (was 20).

## The lost sitemap list: what is established

**Cause found, and reproduced on the synthetic site.**

- The statement: `DELETE FROM sitemap_urls WHERE sitemap_id = $id` in `CrawlWriter.RegisterRecheckSitemap`, as it stood
  at commit `8cbd8c24` (`engine/src/ShoutingIguana.Engine/Storage/CrawlWriter.Recheck.cs`, about line 427 there).
  It ran as the recheck registered each sitemap, and committed with the writer's batch. The next statement,
  `INSERT OR IGNORE INTO sitemap_urls (sitemap_id, url_id, lastmod)` (`Storage/WriterStatements.cs:128`), failed with
  `table sitemap_urls has no column named lastmod`: the file was made by a build from before that column, and that
  recheck's open added columns to `responses` and `sitemaps` only (the damaged file has `sitemaps.size_bytes` and no
  `sitemap_urls.lastmod`, which shows exactly that). The writer died, nothing put the rows back, and the analysis ran
  on the half-written file.
- Who did it: recheck 3 in `garden-b.sicrawl` (`robots-txt-no-sitemap`, kinds `RobotsTxt,Sitemaps`, status failed,
  18 Nov 06:33 UTC) is the realsites lane's CLI run at `8cbd8c24` on a **copy** of the owner's crawl. Its log is
  `…/a539aa23…/scratchpad/realsites/logs/garden-recheck-c.log` (the stack trace names `InsertSitemapUrl` from
  `ApplyRecheckDiscover`).
- Already fixed on the base before this lane: `2afdf35d` (one schema upgrade for every writer, and a recheck in one
  transaction, rolled back on failure) and `0c866efb` (no answer keeps what the crawl read).
- Reproduction (in `engine-fix/repro/`): crawled `si testsite --pages 250`, dropped `sitemap_urls.lastmod` to make the
  file as a 17 Nov build left it, then `recheck --check sitemap-error`. Engine at `8cbd8c24`: failed, 432 entries
  became 181, `pages-0.xml` says 251 and holds 0 (`old-engine-recheck.sicrawl`). Engine on this branch: completed, 432
  kept (`new-engine-recheck.sicrawl`).
- Files compared: `garden-a` (240 entries, no `rechecks` table) and `garden-b` (0 entries, row says 240, three rechecks).
- **Owner's real crawls:** `docs/v2-rebuild.md:432` says the installed app predated the fix loop, and the install went
  straight to `3aa35e0a` (19 Nov 04:53), which has the fix. So no installed build could do this; only the test run's
  scratch copies were damaged. **I did not open the owner's library** (the rule says don't touch it), so this is from
  the record, not from a scan. How to tell: a `sitemaps` row with `url_count > 0` and no `sitemap_urls` rows; on this
  branch the Sitemaps screen and the analysis say so themselves. To get true figures back: crawl the site again (the
  rows are gone; nothing can rebuild them), or use a copy from before the recheck.

## Jobs of the brief

| Job | State |
| --- | --- |
| Find the statement, reproduce on the synthetic site | **done** (above) |
| Failing test, then fix | **done** for what was still open: `305d39ec`. The original fault was fixed on the base; `RecheckTurnedAwayTests` found three other ways a completed recheck still replaced what the crawl read with nothing (a sitemap answered 429 or by a firewall; robots.txt answered 429 or 5xx; pages refused only because robots.txt couldn't be read), six cases seen failing, all fixed, plus two pins (the garden shape; a stop mid-sitemap) |
| Other tables a recheck rewrites | **partly done**. Checked by test: `sitemaps`, `sitemap_urls`, `robots`, `responses` and `links` (images, hreflang and structured data live in `links` and `response_details`, replaced with the response as one unit). Failure at four stages was already held over every table by `RecheckAllOrNothingTests`. Missing: a sitemap whose body breaks off mid-read (stored as read in part; the test host can't send part of a body), and no test compares `response_details` and `response_html` row by row after a refused recheck |
| Damaged file honest, analysis version up | **done**: `ed3301bb` (engine, version 21 and its test), `3cbc1c45` and `c71fe5bd` (Sitemaps screen), `6b217e1f` (plain words) |
| Recheck's numbers and words (A-08, S-08, S-15) | **done** in `changes/**`: `0b25701c`, `81ef487d`, `c315e050`. S-15's "and 3 more" under the list and the "Check again" form are in the simple and shell lanes' files: **not started** |
| A-23 words from `changes/**` | **partly done**: a failed recheck's line and singulars. The dialog's width and links are the shell lane's |
| Whole `verify-engine.sh` | **partly done**, see below |
| Live app at 2560 × 1380 with shots | **partly done**, see below |

## Decisions, and why

- **Turned away is not an answer.** A recheck replaces what the crawl read only with what the site says now: 429, a
  firewall's challenge and no answer keep the old rows, as pages already did (`31f3e6a8`). A sitemap's own 5xx is still
  stored, as a page's is.
- **A new sitemap state `listLost`**, not a flag: the app shows dashes for any state that isn't read, and an index over
  it loses its counts, with no change to the data lane's count logic.
- **The analysis skips eight checks** (`sitemap-list-lost`): the five that read sitemap entries, `orphan-page`,
  `canonical-unlinked` and `hreflang-unlinked`. Skipping `not-in-sitemap` makes `url_summary.in_sitemap` null, so URLs'
  "In no sitemap" and the inspector stop saying "no" as well.
- **A failed recheck has no score after** (reader only; the stored row is untouched).
- **Button counts** `judged - notRechecked`; the cap is on fetches. Words are in `docs/v2-fix-loop.md`.

## What ran, exactly

- `dotnet build engine/ShoutingIguana.Engine.slnx -c Release` at head: 0 warnings, 0 errors.
- `verify-engine.sh` at `c315e050` (logs in `engine-fix/logs/verify-engine/`): build ok; **test exit 1**: Engine 2,416
  passed, 1 failed (`IssuePlainTextsTests.EveryCheckAndEverySkipReasonHasPlainWords`, no plain words for the new skip
  reason), Sidecar 331 passed, CLI 45 passed; **bench exit 0** (100k, 1,998 pages/s, peak 209 MB, flat); **readbench
  stopped at the wrap-up, not finished: no result.**
- After the fix for that failure (`6b217e1f`): only 38 targeted engine tests ran and passed (plain words, lost list,
  failed recheck, freshness). **The whole engine suite, the bench and readbench have NOT run at head.**
- App at `c315e050`: typecheck, lint, format:check clean; `npx vitest run --maxWorkers=8`: 510 files and 5,529 tests
  passed, 14 files and 189 tests skipped. At head: typecheck clean, Prettier and ESLint clean on the touched files, and
  the Sitemaps tests (4 files, 114 tests) passed. **The whole Vitest suite has NOT run at head; `test:stories` and the
  e2e specs never ran.**
- Live app (this branch's sidecar, before `6b217e1f` and `c71fe5bd`), Advanced, 2560 × 1380, shots in
  `engine-fix/shots/engine-after-*.png`: Sitemaps on the damaged garden copy showed the list-lost status and dashes
  (and the two faults `c71fe5bd` then fixed, which were **not** looked at again in the app); buttons read "Recheck 150
  of 240 URLs", "Recheck 59 of 225 URLs", "Recheck 8 URLs", "Recheck robots.txt and the sitemaps". **Not seen:** the
  line under the button (the garden copy wasn't its site's latest crawl with `garden-a` beside it, so Recheck was
  refused), Simple mode, 1280 × 720, and no before shots of my own (the audit's are the before).

## Known breakages and loose ends

- The export's status cell says "Read: 240 URLs, but this crawl no longer has the list" (`Export/SitemapListWords.cs`,
  held by `LostSitemapListTests`); the screen says "Read, but its 240 URLs are lost". Make them one.
- Crawl ids differ in a copied data folder (they follow the path): the flows in `engine-fix/flows/` use this lane's.
- Other lanes' files touched, in `Shared:` commits: `app/src/api/grids.ts`, `screens/sitemaps/sitemapWords.ts` and
  test, `screens/sitemaps/SitemapList.tsx`, `screens/SitemapsScreen.test.tsx` (data); `screens/rechecks/RecheckControl.test.tsx`
  (shell); `engine/.../Analysis/PlainWords.cs`, `plain-words.json`, `IssuePlainTextsTests.cs` (simple).
- Tests rewritten because they pinned old behaviour: the freshness test (20 current), the Sitemaps tally's shape, the
  capped button and plan line in `recheckWords.test.ts` and `RecheckControl.test.tsx`, the count of skip reasons.

## Next five steps

1. Run `bash docs/handoff/2026-11-13/tools/verify-engine.sh <ux-engine> <logDir>` at head, whole, and read all four lines.
2. Run the app's whole Vitest suite at head (`npx vitest run --maxWorkers=8`), then `test:stories` and the fix-loop and
   sitemaps e2e specs (`npm run build:sidecar` first: the binary in the worktree predates the last two commits).
3. Live app again with only `garden-b` of the garden pair in the data folder, so Recheck isn't refused: see the line
   under the button, Sitemaps after `c71fe5bd`, Simple's "Fixed it?" card (`flows/simple.mjs`), and 1280 × 720.
4. Make the export's list-lost words the screen's, and decide whether a sitemap body that breaks off mid-read should
   also keep the crawl's entries on a recheck.
5. Hand the simple and shell lanes what is theirs of S-15 and A-23 (the "and 3 more", the Check again form, the dialog).
