# E3c take-up review (`ui/e3c-takeup` @ 27e8286): findings

Saved by the orchestrator from the reviewer's final message, with the orchestrator's decision under 3. Paths are
under `app/`, lines at 27e8286. Proofs in `proofs\`, shots in `shots\` beside this file.

1. **Medium, CONFIRMED (unit probe). `src/screens/overview/foundAndCrawled.ts:66-67` (`earlierRuns`).** The run with
   `endedAt === null` is taken as the one being watched, but a run that crashed partway also has `endedAt` null. The
   sidecar reads `runs` from the file per request, the new run's row is written only once the engine opens the
   file, and `historyQuery` is read once (`staleTime: Infinity`) and not invalidated when a crawl starts. A crashed
   crawl continued: the crashed run's time vanishes from the axis and Elapsed, its points pile up at one x, its
   marker is missing, "in 2 runs" for 3. Probe: runs of 100 s (ended) and 60 s (`endedAt` null), `liveFrom` later:
   expected `offsetMs` 160000 and markers `[100000, 160000]`; got 100000 and `[100000]`. Fix: the watched run is the
   one with `Date.parse(run.startedAt) >= Date.parse(history.liveFrom)`; and invalidate the history when a crawl
   starts or is continued.

2. **Medium, CONFIRMED (Playwright, mock app). `src/screens/HomeScreen.tsx:153` and `:176`.** At 640 px with the
   sidebar expanded (224 px), `main` scrolls sideways (scrollWidth 465 against 416): the library card header's
   fixed-width search box (`w-(--search-width-library)`, 260 px) pushes the card past main; the card loses its right
   gutter and is cut at the window edge. The `:176` comment is untrue; `home.spec.ts` covers only the collapsed
   sidebar. Fix: let the search box shrink (`min-w-0 flex-1`, max-width the token) or wrap the header below
   `--container-library`; add the sidebar-expanded case to the e2e.

3. **Medium, CONFIRMED (unit probe, against the contract). `src/inspector/words.ts:142` (`noStatusWords`), used by
   `links.ts`, `parts.tsx`, `redirectChain.ts:58`, `hreflang.ts`.** While a crawl runs, every named URL with
   `outcome: null` reads "Not fetched yet", but `outcome: null` also means skipped (not followed, past a limit,
   external with external links off), which will never be fetched. The inspector's own `fetchState` separates
   queued from skipped. DECISION: for `outcome === null` say "Not fetched" whatever the crawl's state (true either
   way), unless the row carries the other URL's `state`, in which case use it as `fetchState` does; and the redirect
   chain's and hreflang's sentences follow. (Not an engine change this round.)

4. **Low-medium, CONFIRMED in the mock app. `src/ui/Tabs.tsx:53`.** The reveal of the chosen tab depends only on
   `[value]`: when tab labels change width (counts arriving, walking URL to URL) the chosen tab can end up wholly
   outside the strip with nothing showing it's there (`shots/inspector-after-walk.png`: "Source" at 691-743 in a
   strip of 377-631). Fix: re-run the reveal when the strip's `scrollWidth` or its items change (a ResizeObserver
   on the list).

5. **Low, CONFIRMED (Playwright). `src/styles/components.css:780` and the `Tabs.tsx` doc comment.** The strip's bar
   was removed on the grounds that "the wheel and a touchpad scroll them", but a plain vertical wheel doesn't
   (scrollLeft stayed 0 after `wheel(0, 300)`; only Shift+wheel moved it). Fix: an `onWheel` that turns vertical
   into sideways scrolling when the strip overflows, AND an edge fade (from tokens) on the side that has more tabs,
   so a mouse user can see there are more; correct the comment.

6. **Low, CONFIRMED (grep). Storybook lacks the new states**: the probes line (live, done); done Overview's "It spent
   X crawling…" and its "isn't known" variant; the chart with run markers on a screen story; the inspector's
   outcome words on link rows, chain ends and hreflang targets. No story or fake sidecar sets `siteVersions`,
   `crawlingMs`, `runs` or a link `outcome`. Fix: add them to `OverviewScreen.stories` and `Inspector.stories`
   (`overviewSidecar`'s `probes` option exists).

7. **Low, PLAUSIBLE. `src/screens/overview/LiveOverview.tsx:57-59` and `:92`.** Elapsed adds earlier runs' time from
   the history query; before it answers, or if it fails, Elapsed shows only this run's time and then jumps.
   `progress.crawlingMs` is parsed into the live store and never used, though the API says a live client should
   take it. The comment still says Elapsed "starts again when a crawl is continued". Fix: show
   `progress.crawlingMs` when it is a number (fall back to run clock plus offset); update the comment.

8. **Low, PLAUSIBLE (CSS read). `src/styles/base.css:362`.** `.si-grid--windowed::-webkit-scrollbar-thumb` uses the
   `background` shorthand without `padding-box`, which resets the clip, so the grid's horizontal thumb would fill
   the full 10 px under the global 2 px transparent border where other bars draw 6 px. Fix:
   `background: var(--color-border-control) padding-box`, or drop the rule; look at it in `smoke:shell`.

Looks right (reviewer): every Overview figure on the synthetic crawl against the real sidecar lands on its count;
`statusSplit` subtracts the probes right live and done; one `/urls/inspect` read per tick against the real sidecar
and no inlinks/outlinks reads; "N URLs" one helper everywhere; `fixFirst` everywhere with hidden checks out and the
fallback; forced colours keep system bars; the TopBar/Sidebar edits are one value each; the three updated e2e tests
follow real changes.
