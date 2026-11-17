# Step 3b checkpoint (session a539aa23, started 2026-11-17)

## Owner's request (2026-11-17, verbatim core)
"Ok I want you to do Step 3b. However, when doing it, I want you to do a UX audit as if you were the site owner ...
some screens don't show the correct information for the user to be able to make actionable decisions, e.g. the broken
links never showed what page they were on ... a large images UI ... does not show the image size OR where the image is
shown! For every single view / UI it must be actually useful, and the UI&UX must be world class, so it's easy to scan,
understand and action and the user fix their site."
Also: "is all the settings we had in the previous app in this one? i.e. Proxy etc..." and "When shall I compact too".
=> Owner's word given for step 3b: crawling the three real sites is authorised (gardenfurnituresets, aptitude, jitsunami).

## Plan
1. Crawl three sites (CLI, polite: -c 2, --rate 2, --respect-crawl-delay, --store-html, --max-urls 5000) into
   scratchpad/audit3b/crawls/<host>.sicrawl. Logs in audit3b/logs.
2. Parallel research agents (read-only, write one file each in audit3b/):
   - settings parity old app vs v2 -> returns in chat only
   - evidence-inventory.md: per check, what the user sees vs what's needed to act (where/what/how much)
   - ux-views.md: view-by-view UX audit as the site owner (gallery 6.1 shots + code)
3. Check-accuracy audit on the real crawls (step 3b proper): per check, sample flags vs stored HTML, sample misses,
   read the words as a non-expert. Tool: `dotnet run audit3b/tools/dump.cs -- <crawl> html|details <id|url>`
   (Brotli HTML in response_html, JSON facts in response_details). Results -> docs/v2-check-audit.md.
4. Synthesize into docs/v2-check-audit.md (+ UX section/doc), publish a readable audit page for the owner.
5. Fix on a new branch off ui/step-3 (ui/step-3b...), per CLAUDE.md rules: planted case + expected count for new/changed
   checks, tests seen failing, verify-app.sh, smoke etc. Never push.

## State
- Branch ui/step-3 at aa2802b, clean. Nothing started on code yet.
- Running: 3 crawls (bg), agents: settings parity, evidence inventory, views UX.

## Settings parity (agent result, 2026-11-17)
All old UI settings present or dropped on purpose (doc v2-ui-discovery.md Crawl settings). Proxy: one address field
http/https/socks5://user:pass@host:port (CrawlOptionsForm.tsx:367), Test button (:368, CrawlSettingsScreen.tsx:392),
creds in Windows Credential Manager. UA: Firefox/Edge/Safari only via Custom. All 27 options wired UI->sidecar->engine.
Problems to fix:
1. No consent step before saving proxy/header secrets to Credential Manager (plan said "with the user's consent"):
   client.ts:179-182, :238; secrets.ts:152-155.
2. Wording says creds "aren't kept" (words.ts:62; NewCrawlScreen.tsx:231 toast) but they are kept in Credential Manager.
3. Quick-start defaults: no URL/depth limit, no rate cap (engineDefaults.ts:8-12); old app 1,000 URLs, depth 5, 1.5 s/host.
   Consider a polite default rate.
4. SOCKS5 with login untested.

## Owner's decisions (2026-11-17, AskUserQuestion)
- Fix loop: "Compare + recheck" — every check/score/list compares with the site's previous crawl (New, Fixed, Fewer,
  score +/-), plus "Recheck these N pages" on a problem (re-fetch just those pages, "4 of 5 fixed"). No manual mark-fixed.
- New checks: "All of them" — every old-app backlog check and every Screaming Frog check that needs only data we already
  fetch (docs/v2-ui-discovery.md#backlog..., docs/v2-comparison.md "Theirs we lack: from the HTML and headers we
  already fetch (100)"). Each with a planted case + expected count + test seen failing.

## Audit inputs (on disk, audit3b/)
- evidence-inventory.md: 87 checks; Blocks 13 / Slows 24 / Nice 21 / None 29; section 4 = 9 cross-cutting patterns
  (evidence columns per check, richer IssueTarget, element lists / image targets, Simple parity, redirect hops inline,
  cross-refs (robots line, sitemaps, found-on), new collection: image px dims + head-closer element, template hint for
  ungrouped, exports follow screens).
- ux-views.md: 12 ranked view findings (before/after; fact per cause; Used-on column for resources; Simple link text +
  position; recheck; Issues check-list sort; related checks nested; importance sort + Inlinks/Depth/Indexable; to-do
  ticks (dropped by owner); Links group-by-target + Images preset + inspector images; robots blocked: linked-from + in
  sitemap; headline counts = things to change).

## Programme (order)
P1 check-accuracy audit on real crawls -> docs/v2-check-audit.md + engine fixes
P2 evidence: engine/API (evidence columns in catalog, richer targets, image targets, element lists, hops, image dims,
   head closer, robots line, used-on) then UI (Advanced Issues, Simple WhereItIs, exports/report)
P3 view UX fixes (sorts, related checks, headline counts, Links/inspector images, robots columns) + settings fixes
   (consent before Credential Manager, wording, polite default rate)
P4 fix loop: compare with previous crawl + recheck N pages
P5 new checks (all), built on the evidence model
Each phase: own branch off ui/step-3 in its own worktree, merge --no-ff, verify-app.sh + engine tests + bench gate.

## Running (as of ~07:20, 2026-11-17)
- P1: 3 accuracy agents (indexing / responses / onpage) -> audit3b/accuracy-<group>.md, brief accuracy-brief.md.
- P2: evidence engine agent, brief audit3b/evidence-brief.md, worktree .claude/worktrees/3b-evidence on ui/3b-evidence
  (off aa2802b). Stage A = contract (docs/v2-sidecar-api.md + new docs/v2-evidence.md), then it STOPS for my review;
  Stage B on my go via SendMessage. After stage A: launch the UI agent for evidence (Advanced Issues, Simple WhereItIs).
- Crawls: aptitude done (33 pages, 11 checks fired); garden ~4,500 URLs finishing; jitsunami stuck on 429 back-off.
- P2 stage A committed a97c31f on ui/3b-evidence (docs/v2-evidence.md + v2-sidecar-api.md "(evidence)"). Reviewed,
  stage B GO sent (notes: images resolved by normalised URL not order; 75 ms gate fallback; contract stable).
  Key decisions: no CrawlSchema bump (pixel cols via RESPONSE_COLUMNS), usedOn in url_summary (needs analysis), object
  column type, targetFacts list in catalog, AnalysisFreshness 8->9.
- P2 UI agent started: worktree .claude/worktrees/3b-evidence-ui, branch ui/3b-evidence-ui off a97c31f, brief
  audit3b/evidence-ui-brief.md; merges ui/3b-evidence when stage B is done.

## Owner (2026-11-17): "make sure this is smart enough we don't hit things like this" / "starting the crawler too fast?"
Diagnosis: jitsunami product pages 429 Retry-After 60 (Cloudflare/Shopify) after ~1.1 req/s start; HostGate backoff
half-life 500 ms -> bursts after pauses re-trip the limiter; 12 product pages lost, recorded as internal-blocked
"firewall". Single cold request later = 200 (not UA). P1b agent started: brief audit3b/polite-brief.md, worktree
.claude/worktrees/3b-polite, branch ui/3b-polite off ui/step-3: slow start, AIMD learned pace per host, host-wide
Retry-After pause, no bursts, 429 URLs deferred to the retry pass, honest "not checked: site asked us to slow down",
progress words, defaults review; testsite rate-limited mode; real re-crawl of jitsunami to crawls2/.
Crawls done: garden 4,360 URLs / 260 pages (22 checks fired); jitsunami 75 URLs / 10 pages (18 fired); aptitude 33 pages.
- P1 indexing done (accuracy-indexing.md): 31 checks, 5 fired (45 flags, all right, no misses). 25 right, 2 fix
  (CanonicalRules.cs:88 + HreflangRules.cs:77-78 treat FetchOutcome.Blocked/429 as non-indexable; hreflang alternate on
  another site blocked by ITS robots flagged), 3 reword (canonicalised-page Simple words re pagination; not-in-sitemap
  login/basket; hreflang-to-non-indexable firewall wording; sitemap-missing Simple), severity sitemap-error Error->Warning,
  optional nofollow-page+noindex -> Notice. Backlog ideas: robots.txt doesn't name sitemap; paginated canonical to p1.
- P1 onpage done (accuracy-onpage.md; re-check scripts in scratch-onpage/): 26 checks, 10 fired, all 303 pages checked,
  0 wrong, 0 misses. 20 right, 3 reword, 2 severity, 1 fix. Fix: RichResultRequirements.cs:12-23/:37 subtypes ignored
  (map to parent + planted case). Low: charset not within 1024 bytes (HtmlRules.cs:26); comment HtmlRules.cs:7; cookie
  banner words PageWalker.cs:44 (optional, rebaselines); stale doc v2-rebuild.md:1045-1047. Reword: title-too-long Simple
  "may be cut off" + detail limit; description-too-long same; near-dup drop bits text (IssueCatalog.cs:681); FAQ date
  claim (IssueCatalog.cs:873) verify/drop; title-too-short skip login/basket. Severity: description-duplicate W->Notice
  (:609); structured-data-invalid Error->Warning (:857).
- P1 responses done (accuracy-responses.md): 30 checks; 11 wrong flags all Shopify login redirect. Fixes: LinkRules.cs:51
  skip temp-redirect targets; RedirectRules.cs:137 skip temp redirect to other site; orphan-page skip when pages unread;
  LinkRules.cs:18 self-link empty anchor. internal-blocked -> "refused the crawler", Notice (given to polite branch).
- P1 FIX agent started: brief audit3b/accuracy-fix-brief.md, worktree .claude/worktrees/3b-accuracy, branch
  ui/3b-accuracy off ui/step-3; writes docs/v2-check-audit.md (87-row verdict table).
- Branches in flight: ui/3b-evidence (engine B), ui/3b-evidence-ui (UI), ui/3b-polite (pacing), ui/3b-accuracy (fixes).
  Merge order plan: accuracy -> polite -> evidence -> evidence-ui into ui/step-3 (resolve catalog conflicts), then P3.
- P4 design agent (docs only, stage A): brief fixloop-brief.md, worktree .claude/worktrees/3b-fixloop, ui/3b-fixloop,
  writes docs/v2-fix-loop.md + sidecar api "(fix loop)"; stops for review.
- P5 triage agent (docs only): brief newchecks-brief.md, worktree .claude/worktrees/3b-newchecks, ui/3b-newchecks,
  writes docs/v2-new-checks.md (deduped list, specs, batches); stops for review.
- Note: concurrent verify-engine runs may make the readbench 75 ms gate flaky; re-run gates on a quiet machine at merge.
- P4 design DONE: 5cf1c67 on ui/3b-fixloop (docs/v2-fix-loop.md + sidecar api "(fix loop)"). Reviewed; accepted all 8:
  recheck updates same crawl file (marked rechecked N on date) + full re-analysis; earlier crawl re-analysed with
  today's rules (Advanced asks); URLs no longer linked get a new skip reason (rows kept); one-step fetch of new links;
  latest crawl only, refused while same site crawling; match by exact URL, else counts only; previous = newest earlier
  crawl of same site with results (prefer same start URL; stopped = partial); cap 1,000 URLs, importance first;
  orphan-page/no-https -> full check. Stages: E1 testsite revisions [after evidence+accuracy merged] -> E2 compare
  reader + si compare, E3 recheck engine [after polite] -> E4 robots/site versions/sitemaps -> S1, S2 sidecar -> U1
  Simple, U2 Advanced -> V verify. Expect api-doc merge conflicts with evidence (both add at top + end).
- P2 UI first pass done on ui/3b-evidence-ui (fb45f5d e7d0aff cdae273 106bb26 470d34a); unit 3,941 pass; e2e not run
  (needs engine). Own checkpoint: audit3b/checkpoint-evidence-ui.md. Shots audit3b/shots-evidence/. Review notes sent:
  px never truncated; state shown-at vs file insight ("10x wider"); Simple One-fix wording; drop redundant per-page
  detail; thumbnails for image subjects (CSP img-src, offline fallback). Merge of ui/3b-evidence waits for engine B.
- P5 spec DONE: a91ae87 on ui/3b-newchecks (docs/v2-new-checks.md): 84 new (1 E, 18 W, 65 N), 8 changes, 27 not built.
  9 batches, SEQUENTIAL, after evidence merges: 1 indexing/canonicals 13+3; 2 links/crawlability/pagination 13+1;
  3 head + served files 9; 4 sitemaps/robots 6+2; 5 structured data incl microdata 3+1; 6 images 6; 7 security/URLs 11;
  8 headings/content/hreflang 13; 9 OG/X cards 10 (og:image becomes a link kind: moves link-count tests).
  My rulings: title-same-as-h1 DROP (Google recommends matching; would flag correct practice); sitemap-over-limit
  Warning not Error; readability-low only for English pages, Notice; deep-page uses crawl's recorded depth, bench
  expectations computed; others as specified. Ask owner: title/description length in PIXELS instead of chars (SF does).
  Note: links to localhost/private IPs from a public site make the crawler hit the user's network -> follow private
  addresses only when the start URL is private (crawler tweak, small; add to polite or a later branch).
- P2 engine B DONE on ui/3b-evidence: 775c6a4 48d6576 439dbba 5d4c3f0 20ceccf. engine 1,542 / sidecar 286 / cli 28 pass;
  bench 100k ok (1,628 p/s, 199 MB flat); readbench: rows-by-importance 18 ms ok; a few unrelated reads missed the gate
  under load (another si.exe running), each passes alone -> RERUN readbench on a quiet machine at integration.
  Image matching 100% on all 3 real crawls. Logs: scratchpad/evidence/verify1/.
- P2 UI second pass DONE: a8307b7 (Advanced + CSP img-src https: http:), 3f13387 (Simple); thumbnails; unit 3,959.
  Told UI agent to merge ui/3b-evidence (20ceccf) + run e2e + recapture engine-checks.json.
- P2 UI DONE: merge 5e1078a (evidence 20ceccf into evidence-ui), b2e60d3 (types), 1e8f37a (stale e2e); e2e 57+75 pass
  after fixes; engine-checks.json recaptured. Not run: full verify-app, smoke:shell, Linux docker. Thumbnail live load
  not seen e2e (test site stopped). ui/3b-evidence-ui is the branch to merge (contains engine).
- P1 fixes DONE on ui/3b-accuracy (10 commits, last 2486074): engine 1,503 / sidecar 286 / cli 28; bench ok; readbench ok
  alone. Orphan skip at >=1 in 20 unread pages (skip reason pages-unread). Scores: jitsunami 76->79, garden 83->85.
  Fixtures engine-checks.json/garden-analysis.json still old -> recapture at integration.
- INTEGRATION 1 agent started (brief integrate1-brief.md): merge --no-ff ui/3b-accuracy then ui/3b-evidence-ui into
  ui/step-3 in the ui-step-3 worktree; AnalysisFreshness -> 10; recapture fixtures; quick verify. Logs scratchpad/integrate1/.
  Then: merge ui/3b-polite when done; full verify (verify-engine, verify-app, smoke:shell, e2e:filled, linux docker,
  installer smoke) on a quiet machine; re-crawl jitsunami; status doc; gallery 7; upgrade owner's install.
- Asked owner (pending): title/description length in pixels? (my rec: pixels in Advanced, chars hint in Simple).
- P1b polite round 1 DONE on ui/3b-polite (9 commits, last fdc357d): HostPace slow start + AIMD + host pause, 429 held
  to end + 3 tries, honest not-checked outcome + notices rate-limited / rate-limited-unchecked, internal-blocked reworded
  ("Internal URL refused the crawler", Notice, Simple "Pages or files that refused the check"), PrivateNetworkGuard
  (0d71468). engine 1,585 / sidecar 286 / cli 28; bench same; verify-engine green. Real re-crawl (crawls2/): 0 blocked
  but 6 product pages unchecked (Shopify ~1 product/min). Defaults kept (Standard, no cap) — reasoned.
  ROUND 2 sent: per-section pacing (path group), keep trying while working (20 min cap), time-based ramp, sitemap 429
  retry, testsite per-section limiter, one more real re-crawl -> crawls3/.
- Polite round 2 DONE (698b18a 2bd2948 f43438c f31795b 13b6eb0 e9b465b): per-section pacing (folder rule; 3 sections in
  10 min = whole site), time-based ramp, sitemap 429 retry, 20-min cap; verify green; bench 2,389 p/s. Real crawl 3
  (crawls3/): 0 blocked, 8 unchecked (all products + /collections/belts), rest of site not slowed.
  My diagnosis 11:24: curl with our exact UA/headers + site cookies (HTTP/1.1) -> product 200. Not IP/UA/headers/
  cookies; likely rolling ~5-10 min window renewed by each refused try. Sent: let section gap keep doubling past 5 min
  within the 20-min cap; then stop. Recheck (fix loop) will collect stragglers later. Third real crawl = owner's call.
- Integration 1: merges ca10014 + 73a2674 + fixtures c6a9b40 on ui/step-3; build/test/bench ok; readbench missed under
  load, rerun pending; e2e pending.
- OWNER 2026-11-17: YES to pixel widths for title/description length checks (title-too-long/short,
  description-too-long/short). Plan: own branch right after this merge round (= P5 "batch 0"), off ui/step-3: embedded
  Arial width table (title 20px, description ~14px), defaults like Screaming Frog (title over 561 px / under 200 px;
  description over 985 px / under 400 px) - verify; Analysis settings in px (keep a chars hint); Advanced detail
  "612 px, over 561 (64 characters)"; Simple "Google will likely cut this title off after '…'" (compute cut point);
  planted cases, expected counts, app settings UI + words.
- Polite FINAL cd49371 (gap doubles to 20 min within the 20-min cap; progress "next try in 8 min"); Debug tests green;
  full Release verify not rerun on cd49371 (last full green e9b465b) -> covered by the integration verify.
  NEXT: when integration 1 reports, send it: merge --no-ff ui/3b-polite, then full verify on quiet machine.
- INTEGRATION 1 GREEN: ca10014 (accuracy), 73a2674 (evidence; AnalysisFreshness 10; parser callback merged; self-link
  target = page id; FoundOnReader prefers textless non-fragment link to self), c6a9b40 (fixtures; garden 85->87).
  engine 1,548/286/28, bench ok, readbench ok alone, app unit 3,961, e2e 87. Logs scratchpad/integrate1/.
- INTEGRATION 2 sent to same agent: merge ui/3b-polite (cd49371), AnalysisFreshness 11, recapture fixtures, FULL verify
  (verify-engine, verify-app ports 62950/7695, e2e:filled, smoke:shell, check:orphans, linux docker).
  After: I update "Where step 3 stands" + commit; installer build + smoke:tauri + upgrade owner's install only if app closed;
  gallery 7 deferred to end of step 3b (views fixes, fix loop, new checks still to come). Then suggest /compact.
- Pixels stage A agent started (brief pixels-brief.md; draft -> audit3b/pixels-design.md; no builds until verify done).
- INTEGRATION 2 merge landed: 9c1e8d2 (polite) + 3721374 (fixture). Full verify running (agent a0c07d8...).
- Pixels stage A DONE: audit3b/pixels-design.md (+ pixels-scratch/). Liberation Sans 2.1.5 widths (OFL), Arial
  20/14 px, word-boundary cut, CJK/emoji = 1 em. MY RULINGS for stage B (send when verify done): title max default
  580 px (measured cut ~580; SF's 561 too strict: garden 97 vs 69 flags), title min 200, description 985/400 (SF);
  word-boundary cut OK; convert saved char limits at 9.4/6.2 px per char OK; renaming SETTING FIELDS
  (titleMaxLength->titleMaxPixels etc.) OK as data-field renames needed for correctness (no function renames);
  approximations OK. Land before ui/3b-newchecks batches. Stray empty folders "UsersLeeMessenger...c67f90ea...qfixmeasures..."
  at ui-step-3 root: untracked/ignored, old session, harmless, left alone.
