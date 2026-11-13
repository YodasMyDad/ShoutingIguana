# urls-grid review checkpoint
- Worktree C:\Projects\ShoutingIguana\.claude\worktrees\urls-grid-review (detached 925a4e4). Diff be294da..925a4e4.
- Ports 57874-57877, SB 6506. TEMP=this/tmp.
- Phase: setup (npm ci), reading.
## Findings so far
- npm ci done. Subagent (row menu + rowKey) returned 6 findings: 1 HIGH stale menu row by index on refresh (CONFIRMED review-rowMenuStale.test.tsx); 2 MED Links menu URL undefined when from/to.url hidden (linkColumns.tsx:70, CONFIRMED); 3 MED right-click while header cursor leaves headerCursor (GridCore 660-676, CONFIRMED); 4 MED Shift+F10 spanned row anchor (GridCore:582 CONFIRMED); 5 LOW url eq >1000 chars (urlPlaces.ts:7, CONFIRMED); 6 LOW offscreen anchor PLAUSIBLE. Proofs in proofs/.
- Mine next: UrlsScreen, urlColumns, urlFacets, sort, GridCore sort, savedViews/schema, copies.
- Mine CONFIRMED: (a) sortShown drops whole multi-key sort when one key hidden (presets.ts:90, UrlsScreen 217/225) proof review-urlsGrid.test.tsx; (b) undo-delete past 24 silently drops newest view (useSavedViews remove + schema readSavedViews slice) same proof; (c) settings size: 24x1500 views -> pretty file 75KB > 64KB read cap (settings.rs read) => all prefs reset; non-ASCII compact 66KB write refused silently (proofs/settings-size.mjs).
- Suspected: facet chooseFacet replaces same-column filter (UrlsScreen:517) -> count != landing for responseTime/severity vs editor filters; verify on real sidecar. Engine title/valueTitles ignored by urlColumns words?
- Dev started (bg byqyu05sx) ports 57874/57875; token 'shouting-iguana-development-token-not-a-secret'.
- DONE: facet replace CONFIRMED in UI on garden (Warnings>=2 -> "With warnings, 5" lands 73; responseMs>300 bucket 250-499 = 9 vs 23). Engine valueTitles ignored (media/other/source words differ). Dev stopped (pids 28124, 47272). Worktree removed. Report sent.
