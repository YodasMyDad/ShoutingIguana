# Round 3 review (`ui/round3` @ 12f43ef): findings

Saved by the orchestrator from the reviewer's final message, with the orchestrator's DECISIONS (under the owner's
delegation; the owner can overturn each). Proofs beside this file: `unit-filled.log` (the table filled with stubs),
`unit-filled-noreport.log`, `simpleScreens.filled.ts`, `sweep.json`, `shots\`, `proof-review.test.ts`, `scripts\`.
The reviewer checked against the mock dev server (`vite --mode mock`), not the real engine.

1. **HIGH - `app/e2e/states.spec.ts:437`, commit 12f43ef.** Meant to rewrite only the 200% zoom test, it deleted 237
   lines below it: the whole "the design system's states" group and "a crawl's error in the library wraps", 11
   tests (long section title wrap, tab scrollbar, indeterminate bar under reduced motion, hover edges, focus ring
   inside a field, link underline contrast light and dark, message tones light and dark, forced-colours GrayText,
   the library error wrap). `fixture` (`:71`) left unused. CONFIRMED (`git show --stat 12f43ef`: +16 -253).
   Fix: restore lines 431-674 from `be294da`, keeping only the new 200% test body; run them (the E3c take-up slice
   changed scroll bars to WebKit parts, so the tab-scrollbar test may need to follow `ui/step-3` once that merges).

2. **HIGH - `app/src/layout/TopBar.tsx:121` (crumbs shrink from step 7) and `app/src/layout/useGiveWay.ts:51-52`
   (stops at step 9 and accepts overflow).** At the minimum window (960 px, `window.rs:42`) at 200% zoom with a crawl
   running the bar is 424 px: the section crumb disappears and the switch is cut to "Advan" at the window edge
   (`shots\window-200pct-advanced-running-480css.png`). At 520 CSS px the section is the letter "S". Overflow from
   960 to ~1000 px; the clipped section up to ~1140 px. Tests cover only 640 CSS px. CONFIRMED.
   - DECISION: the switch still never gives way. Two more steps, after step 9, in this order: **step 10**, Pause /
     Resume, Stop and the search icon go into one "More" menu button (its accessible name says what's in it; each
     item keeps its key); **step 11**, the status pill shows only its dot and state colour, with its words kept for
     a screen reader and a tooltip. The current section's crumb gets a least width (shortened with an ellipsis, full
     name in its tooltip and accessible name) and never shrinks to a letter; it goes only as a whole, after step 11,
     if it still doesn't fit. Test the sweep every 10 CSS px from 1440 down to 400 in both modes, idle and running,
     at 100% and 200%: no overflow, no clipped switch, no one-letter text. Update the design's order in
     `docs/v2-ui-simple.md` ("Top bar") and the architecture doc.

3. **HIGH (frame) - `app/src/mode/mode.test.tsx:125` and `:141`** assert no Report link exists, in both modes; round
   4's Report agent sets its row and these fail, so it must edit outside its row. With Home, Summary and What to fix
   stubs set, all 1388 pass; Report is the only row that breaks anything. CONFIRMED (`unit-filled.log`). Fix: make
   both checks depend on whether the table's Report row is set. Also: run the whole unit suite and the e2e with
   `simpleScreens.filled.ts`-style stubs for EVERY row as a test of its own (a test that fills the table and checks
   the frame), so round 4's rows can't break the frame.

4. **MEDIUM (frame) - `app/src/app/router.tsx:110`.** The overview route uses `validateNoSearch`, so
   `/crawls/:id/overview?score=1` (the score breakdown, per the Routes table) loses `score`; the Summary agent would
   have to edit `router.tsx` and `search.ts`. CONFIRMED. Fix: `validateOverviewSearch` with `score?: 1` now, and
   check every other search param the design's Routes table names for round 4's screens (What to fix, one problem,
   Report's `cut`) is accepted now.

5. **MEDIUM - `crawl/options/form.ts:139` with `CrawlSettingsScreen.tsx` UsedSettings (~139) and `words.ts:262`.** A
   crawl through `http://user:pw@proxy:8080` shows "Proxy: None (its user name and password aren't kept)". CONFIRMED.
   Fix: show `withoutCredentials(stored.proxyUrl)` in UsedSettings; blank it only in the form.

6. **MEDIUM - `screens/NewCrawlScreen.tsx:173-184`.** In Simple, `focus=userAgent`, `headers` or `robots` switches to
   Advanced and returns before putting focus anywhere: focus on BODY; its "Advanced is on" toast has no "Back to
   Simple" and no `ADVANCED_ON_TOAST` id. CONFIRMED. Fix: after `switchMode`, `focusSetting` in a
   `requestAnimationFrame`; reuse the shared toast (id and action).

7. **MEDIUM - `CrawlSettingsScreen.tsx` ContinuePanel `request()` (~224-237).** `Number('3x')` is NaN, sent as null,
   which the engine reads as "unchanged": a typo continues silently with the old value. CONFIRMED. Fix: validate
   through `/validate` or `useValidation`, inline, and block Continue on a value that isn't a number.

8. **MEDIUM - `form.ts:153-156` (`headersOf`) and `NewCrawlScreen.tsx:152`.** New crawl sends saved or copied header
   names with empty values (`X-Token:`) on every internal request; Home's `startCrawl` filters them. Client side
   CONFIRMED. Fix: drop headers with no value when building the request, in one shared helper both paths use.

9. **MEDIUM-LOW - `NewCrawlScreen.tsx:54`.** `/new?from=<deleted crawl>` makes the whole form an error that Retry
   can't fix. CONFIRMED (`shots\new-from-missing-crawl.png`). Fix: on crawl-not-found, fall back to the saved
   defaults with a note.

10. **LOW - `CrawlOptionsForm.tsx` SimpleQuestions limit field (~150-165), `FieldError` (185-191), `HeaderRows`.**
    Errors in Simple aren't tied to their fields (no `aria-invalid`, no `aria-describedby`), and the message is the
    engine's ("Max URLs can't be negative."), not Simple's words. CONFIRMED. Fix: pass `error` to the TextField and
    give Simple-worded messages for the Simple fields.

11. **LOW - `form.ts:101-104` and `:169`.** MB rounding changes a byte limit that isn't a whole number of MB
    (1,000,000 -> 1,000,342 after "Crawl again with these" or a defaults auto-save). CONFIRMED. Fix: keep the byte
    count unless the MB field was edited.

12. **LOW - `TopBar.tsx:105` (Forward) and `:130` (Home)** use `hidden`, not `sr-only`, when they give way, so a
    screen reader loses them. CONFIRMED. DECISION: keep them reachable (`sr-only`), as the brief says.

13. **LOW - `NewCrawlScreen.tsx:242-255`.** The global Ctrl+Enter command keeps an old `starting`; with focus outside
    the form and a slow sidecar a second Ctrl+Enter starts a second crawl. PLAUSIBLE. Fix: guard with a ref; test
    with a slow fake sidecar.

14. **LOW - `settings/schema.ts:86`.** A settings file from before the mode existed reads as Simple (and `mode.test`
    asserts it). DECISION: a settings file that already exists without `mode` reads as **Advanced** (that person
    has used the app as it was, which is Advanced); only a first start, with no settings file, is Simple. Change
    the test.

15. **LOW - `app/modeRoutes.ts:84-89`.** Turning Advanced on doesn't check the screen exists: `/settings/analysis`
    in Simple turns Advanced on with "Simple doesn't show Analysis." while General is shown;
    `/crawls/<deleted>/urls` turns Advanced on over "This crawl isn't in the library". CONFIRMED. Fix: guard only
    sections that are listed and crawls that are known.

16. **LOW - Simple's words in shared pieces.** In Simple the `?` sheet lists "Open a crawl file…", "Close the
    inspector" and the "In grids" keys under a group "Crawl"; the palette groups by "Crawl"; Data folder and
    Updates say "your crawls"; an error reads "The engine couldn't say". CONFIRMED. Fix: Simple titles from the
    glossary's Simple words ("site check", "checks"), and leave out in Simple what doesn't work there.

17. **LOW - `shell/diagnostics.ts:132-134`.** Copy diagnostics includes the data, state and log folder paths with
    the Windows account name. CONFIRMED. DECISION: replace the user profile prefix with `%USERPROFILE%` (and any
    other occurrence of the account name in what is copied), with a test.

18. **LOW - `layout/ModeSwitch.tsx:22-31`.** The Tooltip wraps a `div`, not a focusable trigger; a screen reader
    never hears the description or Ctrl+Shift+A, and the radios have no `aria-keyshortcuts`. PLAUSIBLE. Fix:
    `aria-keyshortcuts` and the description on the radiogroup.

19. **LOW - `AppShell.tsx` `useCrawlSettingsOpener` and `CrawlSettingsScreen`'s focus effect.** "Open its settings"
    (shown when a proxy password is needed) opens with `focus=headers`. CONFIRMED by code. Fix: a `proxy` focus.

20. **LOW - `NewCrawlScreen.tsx:191`.** "Use these choices every time" saves the defaults before starting; a refused
    start still changed them. CONFIRMED by code. Fix: save after a successful start.

Looks right (reviewer): four branches each setting one row merged with no conflicts and no import line needed; with
stubs set the router, sidebar, Ctrl+1-3 / Ctrl+8 and the inspector left out in Simple behave as designed, search
params kept across the switch; the guard (deep link, Back, reload) and switching from Advanced-only screens; all 27
options round-trip (apart from 11); the one-file-a-section registry; defaults saved only after the engine validates
them; secrets never saved; the top bar at 100% from 1440 to 960 px; the new 200% test body is fair.
