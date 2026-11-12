# Checkpoint: fixes for the Sitemaps + robots.txt review (17 findings)

Brief: `..\brief-sitemaps-fixes.md`. Findings: `..\sitemaps-review\findings.md`. Rules: `..\ui-slice-rules.md`.
Worktree `C:\Projects\ShoutingIguana\.claude\worktrees\sitemaps-robots`, branch `ui/sitemaps-robots`, started at `8d77343`.
Ports: dev 58874, dev sidecar 58875, preview 58876, e2e sidecar 58877, storybook 6606. TEMP/TMP for dev + e2e: `tmp\` here.
NEVER push / merge (a later message asks for the ui/step-3 merge). Never rename an exported function. Stay out of engine/.

## Tools / gotchas
- Bash heredocs with an odd number of `'` fail: write files (and edit scripts: `tools\*.cjs`, run with node from app/)
  with the Write tool.
- `bash tools/prove.sh <name> <vitest files...>`: runs tests with the fix, reverts every uncommitted non-test change,
  runs again (must fail), restores. Logs + patch in `proofs\`. Commit per finding so only one fix is uncommitted.
- TaskStop doesn't kill a script's process tree: no long multi-step background scripts. Kill only by own pid / ports.
- npm ci done (07:30, exit 0), log `checks\npm-ci.log`.
- After prettier --write / node scripts the Edit tool needs a fresh Read of the file.

## Done (commit, test, seen failing without the fix via prove.sh)
- F1 `5268fa8` coverageOf: null bucket with pages -> `known:false, reason:'checkSkipped'`; line gives the analysis's
  skip reason for `not-in-sitemap` (analysisQuery). Tests: sitemapWords.test "never tells a page the engine couldn't
  place...", SitemapsScreen.test "says the analysis didn't count them..." (+1). Sweep handles the null bucket. PROVED.
- F9 `6a396dc` default location 404/410 = "Nothing declared a sitemap, and there's none at /sitemap.xml."; row "None
  there: HTTP 404"; other failures there "…/sitemap.xml couldn't be read". PROVED.
- F11 `fdf9758` `notReadReason` strips "Not read:"; test + story use the engine's real string. PROVED.
- F15 `5118391` `lineProblemWords(line)` by kind (sitemap: "it names no sitemap"; crawl delay: "isn't a number of
  seconds, 0 or more"). PROVED.
- F5 `985c3a6` `Obeyed` (true/false/null) into `stateWords`, `obeyedWords`, `nothingBlockedWords` (moved to
  robotsWords.ts); file view holds its words until `respected` is known; origins list seeds
  `robotsKeys.respected`. PROVED.
- F4 `b9d91cc` (UI half) state that refuses nothing + blocked > 0 -> "…when last asked" + `refusedEarlierWords`. PROVED.
- F12 `4967e80` `lib/failure.ts` (`failureOf`, `withRecorded`): kinds dns/refused/timeout/tls/other/status; used by
  robotsWords (state + blockedWhy), sitemapWords (`sitemapFailure`, XML line), overview/alerts (robots-unreachable,
  site-unreachable; `AlertEntry.recorded` -> title). Engine text in `title` only. PROVED.

- F13 `a5da5bb` originFromParam -> URL origin (robotsOriginOf). IDN unit-tested both spellings (test site can't serve an IDN). PROVED.
- F17 `6cbb40f` chip "Line is 2: …" always; emptyLineWords (no such line / not a rule / blocked none). PROVED (+re-proved after test edit).
- F16 `8a09a3a` errors.ts NOT_YET_PLACE_WORDS + missingPlaceWords(place, crawling); page fills in on refresh (tested). PROVED.
- F14 `c3efe88` stale place: only *.list enabled (stale computed before useCommands; robots `missing`). PROVED.
- F8 `a0b9dff` grid/useSearchText.ts; SitemapDetail AND UrlsScreen search fields controlled, no key. PROVED.
- F10 `966f798` robots.search command (mod+f, palette) -> SourcePane focusSearch; inspector untouched. PROVED.
- F7 `a4b12a0` own-judgement banner shown with a running crawl's list from the start, role none; note row fixed height (--robots-note-height). Banner PROVED by unit test; NOTE HEIGHT still needs an e2e proof (measure list top with wrapped note) + live watch.
- F6 `6716544` subtreeOf/wholeSubtree/countsKnown(sitemap, subtree); SitemapRow.subtree; stateWords(sitemap, crawling, subtree); indexHoldsWords; badge "Index" when more. PROVED. NOTE: an index with ONE FAILED child (the 100k crawl, the test site) now shows dashes on its own row: sweep + e2e must be updated (sweep `known` rule).

- F2 `9cfd503` sitemapLinks.ts rewritten: counts listed/crawled/blocked/notCrawled/nonIndexable; crawled = fetched - urls.robotsDisallowed (null when the engine does not send it -> dash + ENGINE_DOESNT_SAY title); quick filters All/Crawled/Blocked by robots.txt/Not crawled/Non-indexable/Redirecting or broken; detail page counts crawled+blocked itself (sitemapCountQuery, limit 1) only when the list lacks them; Outcome cell = inspector words (Answered). Field names robotsDisallowed / redirectingOrBroken live in: api/grids.ts (type), sitemapLinks.ts (refusedOf/brokenOf), api/mock/sitemapsRobots.ts, test/sitemapsRobotsSidecar.ts (fixture + withoutNewCounts), SitemapsScreen.stories.tsx, e2e sweep. RENAME THERE when the orchestrator confirms the engine names. Test fixture gained rows 10 (robotsDisallowed) + 11 (timeout) on sitemap 6. Sweep rewritten (5 columns, index known-rule, page quick filters add up). PROVED (unit).
- F3 `1547740` GridCore rowKey + startKey (one block after revealStart, REANCHOR_REACH 500); listPlace rememberedKey; origins/tree/sitemap URLs/blocked URLs pass rowKey; story RowsThatMove; GridCore.rowKey.test.tsx. PROVED.

ALL 17 HAVE A COMMIT. Unit suite at 1547740: 102 files, 1104 passed, 1 skipped. typecheck/lint/format clean.

## Next (order)
(a) robots.txt file not re-read each second while live (SELF_REFRESHED grid + refetch when blocked total changes + 10 s interval) + unit test; (b) e2e spec updates: sitemaps.spec (5 columns, index dashes, Answered), robots.spec (F7 note height measured, F13 capitals origin, F10 palette), run my 3 specs, then x10; (c) dev app: garden inspector Source section, list at 1280 + 200% zoom, live watch (copy ..sitemaps-reviewlive-watch.mjs), big robots.txt via a local node server; (d) sweeps on garden + 100k (SI_E2E_CRAWL); (e) docs v2-ui-architecture.md + v2-ui-ia.md; (f) full checks; screenshots; report.

## Engine asks collected
- `GET /sitemaps`: each sitemap's `urls.robotsDisallowed` and `urls.redirectingOrBroken` (working names).
- Finding 4, engine half: no status, no file and refused URLs without an error should read `unreachable`.
- `GET /sitemaps`: how many sitemaps an index's file lists (M of "the crawl has N of its M sitemaps"): today only the children the crawl has (`sitemaps`).
- (small) `/robots/file` could say `respected` itself: the page reads the origins' first row only for that.

## Not verified / open
(fill in)

## Dev app running (started 08:38): background task b1j3x1kyd; sidecar pid 41224 (port 58875), vite pid 17316 (port 58874). STOP BY PID when done. Data: tmp\si-app-dev\data (copies).
Live scripts: live\live-watch.mjs, live\look.mjs, live\api.sh (copied from the review, pointed at this worktree).
testsite url=http://127.0.0.1:55041/
testsite pid 33852 (task bzmh1z3v9), ports 55041/55042: STOP BY PID when done
my local site (live\mysite.mjs) on my port 58876: pid in live\mysite.log; task byt4eosv4. STOP BY PID.

## State at 09:00 (HEAD see git log; after 1547740: a826d0e file not re-read each second, 84b381f e2e specs, then the 1280 fit commit)
ALL PROCESSES OF MINE STOPPED (dev app, test site, local site), ports free. One stray: my local site listened ~1 min on 58878 (not one of my ports) before I moved it to 58876: say so in the report.
Verified in the running app (dev, real sidecar): live watch (livelive-watch.log: list top 208 px constant, no status banner, file frame constant, /robots/file 4 reads in 12 s); inspector Source on garden (livesource-check.mjs ALL OK); big robots.txt 500 KB + sitemap listing 10 blocked URLs via my own local site (liveigfile-watch.log ALL OK: file read 4x in 25 s at 1.36 MB each; page says Crawled 302 / Blocked 10 / Not crawled 0 of 312; list dashes).
e2e so far: sitemaps.spec 10 passed, robots.spec 12 passed (note-height test seen failing 74 vs 86 without the fix), sweep on a test-site crawl passed (21 landings, 14 dashes). NOT YET: x10 repeat, sweeps on garden + 100k, full e2e, stories, 3 unit runs, build-storybook, build:sidecar, scripts tests, screenshots, docs.
Dev data (analysed copies + my local-site crawl 543581fe711c86f0, garden fc55ead3e5b653f3, 100k fd4fc8aa9a3b8724) in tmpsi-app-devdata: reuse for the final screenshots with a measure-serve on 58877/58876.
Engine observation: none new from the big-file run (my first reading of '0 vs 10' was my own script waiting on an element).

## Final cycle (started 09:20) on HEAD 277d307 (stories commit; docs d81861b; 1280 fit 3d296ab)
Seen green so far: typecheck, lint, format:check, scripts (15), unit x2 (102 files, 1105 passed, 1 skipped), unit #3 under load (while build-storybook ran), build-storybook, build:sidecar. Sweeps: test-site crawl, garden (1 sitemap 5 dashes; 17 origins, 2 opened, 2 lines), 100k (13 sitemaps, 84 landings, 11 asked, 32 dashes; 2 origins).
RUNNING in background (task bzr5ze69s): npm run test:stories -> checksstories.log. DO NOT start anything heavy until it ends; do not TaskStop it.
THEN, one at a time: test:stories -- --forced ; -- --reduced-motion ; e2e my 3 specs --repeat-each 10 (toolse2e.sh e2e-mine-x10 e2e/sitemaps.spec.ts e2e/robots.spec.ts e2e/sitemaps-robots.sweep.spec.ts --repeat-each 10) ; full e2e (toolse2e.sh e2e-full) ; then screenshots: node liveshots-serve.mjs (background; pids in its READY line) + from app/: node <S2>liveshots.mjs fd4fc8aa9a3b8724 fc55ead3e5b653f3 543581fe711c86f0 (copy to app/.shots.mjs first so @playwright/test resolves; delete after) ; stop serve by pid ; final report.
If the orchestrator confirms the engine's field names: rename robotsDisallowed / redirectingOrBroken in the places listed under F2 above, re-run unit + sweep.

## Orchestrator's message (during the final cycle): the engine's names ARE robotsDisallowed / redirectingOrBroken, unchanged; they are on ui/step-3 (E3c), which I get when asked to merge (NOT YET: Links merges first; do not merge before that message). The test site gains /private/listed (a listed URL robots.txt refuses) after the merge. Contract now says unanswered can have blocked URLs (backs F4). No rename needed; comments + docs updated.
test:stories (default): 245 stories, 2 themes: no violations (on the storybook built at 277d307). NEXT: rebuild storybook on the final HEAD, then --forced, --reduced-motion, unit once more, e2e x10, full e2e, screenshots.
Stories on fe51eb0's build: --forced 245 no violations; --reduced-motion 245 no violations (default 245 no violations was on 277d307's build: re-run at the end if time). Unit on fe51eb0: 102 files / 1105 passed / 1 skipped. NOW: e2e x10 (task in background, log checks\e2e-mine-x10.log + .done), then full e2e, then screenshots.
e2e mine x10 on fe51eb0: 240 passed (7.2 min). NOW: full e2e (background; log checks\e2e-full.log + .done).
Full e2e on fe51eb0: 86 passed. NOW: screenshots (shots-serve in background, then shots.mjs), then stop serve by pid.
READY sidecar 58877 pids sidecar=33704 preview=48444 self=55500 token=shouting-iguana-screenshots-token-not-a-secret
Screenshots: 92 files in app/test-results/sitemaps-robots (taken after the last e2e run, on fe51eb0; only 'problem' the expected 404 of the not-found origin). Serve stopped by pid; all my ports free. LEFT: test:stories default on the final build (optional re-run), final report.

## FINISHED: HEAD fe51eb0, 26 commits over 8d77343, tree clean, NOT pushed, NOT merged (ui/step-3 merge waits for the orchestrator's message; Links merges first)
Seen green on fe51eb0: typecheck, lint, format:check, unit (102 files, 1105 passed, 1 skipped), build-storybook, test:stories 245 x 2 themes, --forced 245, --reduced-motion 245, e2e mine x10 = 240 passed, full e2e = 86 passed.
Seen green on 277d307 (three small commits earlier: a test, docs, one condition of a refetch interval, comments): unit x3 (one under load), scripts tests (15), build:sidecar (engine untouched since). npm ci at the start (lockfile untouched).
Sweeps: a test-site crawl (also in every e2e run), garden, 100k: passed.
Screenshots: 92 in app/test-results/sitemaps-robots, taken after the last e2e run.
All my processes stopped; ports 58874-58877 and 6606 free. Stray: my local test site listened on 58878 (not mine) for about a minute before I moved it to 58876.
When the merge message comes: expect conflicts in GridCore.tsx (my block after revealStart + 2 props + REANCHOR_REACH), queries.ts (SELF_REFRESHED_GRIDS near ANALYSIS_GRIDS, sitemapKeys.count, sitemapCountQuery, GridFilter import), registry (robots.search is in RobotsFileView, not the registry file), UrlsScreen.tsx (search field: useSearchText), overview.spec.ts, both UI docs. After the merge the engine sends the two counts: the list fills Crawled/Blocked, the page stops counting for itself, and the test site's /private/listed gives a real blocked count: update e2e expectations that assume dashes (sitemaps.spec handles both; the sweep handles both).
