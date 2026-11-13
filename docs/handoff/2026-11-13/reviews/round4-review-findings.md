# Round 4 review findings (ui/r4-integrate 37a58c3), fresh-eyes reviewer, 2026-11-13, with the orchestrator's decisions

Reviewer's proofs (failing tests at 37a58c3, live notes): `...\01db96e9-...\scratchpad\r4-review\proofs\` and
`proof-*.log` beside it. Paths below are under `app/`.

**Decision: fix all 20.** Where the reviewer offered a fix, use it unless a better one is obvious; decisions on the
open ones are marked DECIDED.

1. HIGH `src/simple/SummaryScreen.tsx:109-134` (banners only at :149). A stopped/failed check not yet analysed shows
   only "No score yet... Work out the results": no stopped-early banner (Continue checking, N still to check) and no
   "couldn't finish" with cause and Try again; `analysisFailed` excludes `status==='failed'`. Tests at
   SummaryScreen.test.tsx:181,227 only feed an analysed STOPPED_ANALYSIS. CONFIRMED (P1,P2). Fix: in the
   neverAnalysed branch draw the stopped/failed banners (item.pageCount and queued) above NoScoreYet for stopped,
   interrupted, failed.
2. MED `src/live/liveBridge.ts:264-288`, `src/simple/finishedWords.ts:43-47`, Rust `src-tauri/src/live/notify.rs:374-412`.
   Cancelling a first analysis in Simple toasts "{site} is checked. The results are ready." with See the summary /
   Open the report though nothing was worked out. CONFIRMED (L1). Fix: when crawled but analyzedAt null after an
   analysis began, say it was checked but not worked out (offer Work out the results); mirror in notify.rs with a
   Rust test.
3. MED `src/simple/SimpleOverview.tsx:27` `useAnalysisControl(crawlId, analysing, false)` hard-codes neverAnalysed:
   Cancel during a first analysis announces "Analysis cancelled. These are the issues from the last analysis."
   CONFIRMED (C1). Fix: pass the real state; Simple's sentences in useAnalysisControl when mode is Simple.
4. MED `src/live/liveBridge.ts:226-235`, `crawl/status.ts statusAnnouncement`, `liveBridge.ts:202`. Failure toasts and
   live status announcements in Advanced's words in Simple ("the crawl failed", ": crawling", ": analysing") while the
   Rust notice says "The check of ... couldn't finish". CONFIRMED (L2) for toast. Fix: branch on getMode() like
   toastFinished, using the Rust notice's Simple words; Simple variant of statusAnnouncement.
5. MED `src/simple/SummaryScreen.tsx`, `SimpleOverview.tsx:33`. Ctrl+E does nothing on the Summary (Keys table: "the
   report on the Summary"); mod+e reserved so silently swallowed; comment "the Summary has its own" false. CONFIRMED
   live. Fix: register useExportCommand(() => item ? overviewOffer(item, false, true) : null, ...) preset to report.
6. MED `src/simple/ProblemScreen.tsx:373,402-403`. Passed/skipped check pages show plainWhat[0] (a "what's wrong" fact)
   under "What it looks for" ("Every page has a title" then "These pages have no title..."). Only 12/87 checks have
   plainLooksFor. CONFIRMED (P5). Fix: plainLooksFor ?? plainTitle only.
7. MED `src/simple/SummaryParts.tsx:205-214`. "Nothing to fix" with notices says "There are 3 things worth a look
   below" but doesn't list them (summary-perfect.html does); test at SummaryScreen.test.tsx:175 checks only the
   sentence. CONFIRMED (P6). Fix: render the notice rows when there are no problems.
8. LOW-MED `src/simple/plainWords.ts:209`. Tiny-site line ("Only 6 pages were found... may need JavaScript") added
   whenever covers.pages < 10 even when covers.complete is false (also in the breakdown dialog). CONFIRMED (P4).
   Fix: gate on covers.complete.
9. LOW-MED `src/simple/ScoreBreakdownDialog.tsx:72-80`, `SummaryScreen.tsx:183`. `?score=1` on a never-analysed check
   in Simple shows "Reading the score..." for ever (not-analyzed 404 leaves data undefined). CONFIRMED (P3). Fix:
   render ScoreBreakdownFromRoute only with an analysis, or not-analysed words on error (DECIDED: both: gate it, and
   the dialog shows an error/not-analysed state instead of loading forever on any error).
10. LOW-MED `src/simple/SummaryScreen.tsx:279,358` vs :200. Head uses pagesAnalyzed (37a58c3) but stopped-early and
    turned-away banners still say item.pageCount (265 vs 268). CONFIRMED (P7). DECIDED: banners about what the crawl
    read keep the crawl's count but must not contradict the head: use scoredPages(item) where the sentence is about
    pages in the results, pageCount only where it is about what was fetched, and word each so the two can't read as
    the same number.
11. LOW-MED `src/simple/home/SimpleHome.tsx:81`. Bad address shows the engine's "Start URL must be an absolute http or
    https URL."; non-address errors also land under the address field. CONFIRMED live. Fix: Simple's words for
    startUrl errors; other fields' errors said with Options... as where to fix.
12. LOW-MED `src/simple/plainWords.ts:161-163`, `WhatToFixScreen.tsx:318`. With hidden checks covering every error and
    warning: "Nothing to fix. Every check that ran found nothing wrong." while the ring is held down. PLAUSIBLE.
    DECIDED: when anything is hidden, "Nothing to fix among the checks you show" plus "N checks are hidden".
13. LOW `src/simple/ScoreBreakdownDialog.tsx:37`. Close uses replace:true on the entry opening pushed: open, Esc,
    Back does nothing visible. CONFIRMED live (Advanced; same component in Simple). Fix: history.back() when opened
    in-app, replace only for a deep link.
14. LOW `src/simple/checking/checkingWords.ts:232-237`, `CheckingScreen.tsx:113-123`. "About N minutes... stops at its
    limit" compares limitLeft (internal) with queued (all addresses); never checks internalFound as Advanced does
    (liveFigures.ts:68). PLAUSIBLE. Fix: pass internalFound; "about"/limit only when internalFound >= maxUrls.
15. LOW `src-tauri/src/live/notify.rs:377`. Simple stopped notice counts fetched() while toast and Home use
    crawledUrlCount ?? fetchedUrlCount; tests use 1,234 for both. PLAUSIBLE. Fix: add crawled_url_count to Rust
    CrawlItem and prefer it; test with different numbers.
16. LOW `src/simple/checking/checkingWords.ts:49-69`, `CheckingScreen.tsx:93-106`. Blocked callout's "N pages were read
    first" is live htmlPages, recomputed each progress event, keeps rising. PLAUSIBLE. Fix: freeze at the alert's
    raisedAt (or the alert's own figure).
17. LOW `src/simple/WhereItIs.tsx:125-127,186-188`. Grouped check: all = causes ?? 0 while loading or after a failed
    read: "All 0 are shown." under "The list couldn't be read". PLAUSIBLE. Fix: hide footer until groups are read.
18. LOW `src/simple/WhatToFixScreen.tsx:171`. "N checks are hidden" counts hidden ids the catalog no longer has;
    results.hidden computed and unused. PLAUSIBLE. Fix: count results.hidden.
19. LOW `src/simple/SummaryScreen.tsx:198` vs :523. Head "Checked on {finishedAt}", facts line "on {startedAt}".
    PLAUSIBLE. Fix: one date (finishedAt, startedAt while none).
20. LOW leftovers: `src/simple/results.ts:105-112` problemsInOrder, checkById unused; PlainResults.all/.hidden unread
    (18 uses .hidden); stale comments about removed SIMPLE_SCREENS.report gates at live/liveBridge.ts:32,
    live/liveBridge.simple.test.tsx:3; ProblemScreen.tsx:255 saveCheckSheet(item, check) without crawling (export
    dialog loses its while-crawling note). CONFIRMED (grep). Fix: delete, pass crawling.

## Looks right (reviewer)
Overview row dispatcher and hand-over; no stand-ins left; one ScoreRing; Advanced breakdown focus; shell has one
reveal/open_export_file with grants and tests; Notice::new everywhere; Report and problem export through the
hand-over's dialog (right choice, cut, file name); numbers agree across Home, Summary, Report, Overview on the
re-analysed garden crawl (85, Good, 265 pages, 2 serious, 4 worth fixing, 85 -> 98); hidden checks consistent;
tokens only.
