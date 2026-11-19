# UX audit, 2026-11-19: what every auditor needs

The owner used the installed app on a real crawl and said: "The UI looks nice in most cases but the UX really falls
short." You are auditing one zone of the app **by using it**, as an SEO who has to find, diagnose and fix faults on a
site. You do not change app code. You write findings.

## The complaint that started this (calibrate on it)

Advanced > Issues > "Internal URL returns 4xx". The owner wants the 404s and **where the broken links are**.

- Each row shows the broken URL, then `Found on 1 page · /blog/best-rattan-patio-sets/ (Content · "Buy Now")` as grey
  text. The page that holds the broken link is the thing they need, and it can't be clicked.
- To reach it they must expand the row, read a "Linked from" sub-row, then click "Show the link in Links", which goes
  to another screen. The sub-row's only direct control is an open-in-browser icon at the far right edge of a
  2,500 px-wide pane, cut off by a horizontal scrollbar.
- A single click on a row does nothing visible. Double-click or Enter inspects. Nothing says so.
- Simple mode has the same fault: the address is the only thing with a control, and "Linked from …" is plain text.

Earlier audits missed all this because they judged screenshots at 1280 × 720 and read the code. **The owner's window is
about 2560 × 1380 CSS px.** Judge there first, then at 1280 × 720.

## What "slick" means here (judge against these)

1. **Every URL, page, file, check or count the screen names is a link.** One click does the obvious thing. Text that
   names something you could go to, and can't be clicked, is a finding.
2. **The evidence needed to fix is in the row**, without expanding or leaving: where it is (the page that holds it),
   what it is (the element, anchor text, value), and the limit it broke.
3. **Count the clicks** from "I see the issue" to "I'm looking at the faulty thing on the live page", and to each
   neighbouring fact (who links here, what does this page link to, its other issues). More than one or two is a finding.
4. **Controls sit beside what they act on**, look clickable, and are visible without hover where space allows. Nothing
   important only on double-click, right-click or a keyboard shortcut.
5. **The same thing works the same everywhere.** A URL in Issues, URLs, Links, Sitemaps, robots.txt and the inspector
   should look and behave alike. Note every inconsistency.
6. **Space is used.** No table squeezed into eight rows with its own scrollbar above a blank half-screen, no horizontal
   scrollbar while there is room, no title column cut to 150 px beside a kilometre of blank URL column, no 40-character
   text column in a 1,400 px pane.
7. **No noise.** Figures, labels and words that don't help find or fix the problem are findings ("Groups 1, by detail").
   Words a working SEO wouldn't use, or that need decoding, are findings.
8. **No dead ends.** Back gets you back with your place, filters and selection kept. Jumps between screens land on the
   exact thing, filtered and highlighted.

Also record real bugs you hit (wrong numbers, errors, something that doesn't respond), and anything that made you stop
and think "what does that mean?" or "where do I click?".

## The running app

Already running; do not start, stop or rebuild it, and do not edit anything under `app/` or `engine/`.

- Frontend: `http://localhost:49874` (Vite dev server of `C:\Projects\ShoutingIguana\.claude\worktrees\ui-step-3\app`).
- Sidecar on 49875, serving **copies** of real crawls. Crawl ids:
  - `/crawls/33454603b74b57f1` gardenfurnituresets.co.uk (4,362 URLs, 260 pages; it has an earlier crawl of the same
    site to compare with). The owner's own site: use it most.
  - `/crawls/e6b4b1d9a0bb1541` jitsunami.com (a Shopify shop, 253 URLs).
  - `/crawls/4950b1d1228b1373` aptitude.co.uk (33 pages).
  - `/crawls/0b71328b5d42d1ed` the synthetic test site (`127.0.0.1:…`): nearly every check fires on it.
- Routes under a crawl: `/overview`, `/issues`, `/issues/<check-id>`, `/urls`, `/links`, `/sitemaps`, `/robots`,
  `/report`, `/settings`. App-level: `/`, `/new`, `/settings`. Read `app/src/app/router.tsx` for search parameters.
- Mode and theme live in localStorage key `si.settings`, e.g. `{"mode":"advanced","theme":"light"}`.

**Never** start a crawl of a real site, press Recheck or "Check again" on a real site's crawl, or press anything that
fetches from the internet: those need the owner's word. You may open the recheck menus and dialogs to judge them, then
cancel. Don't delete crawls. Don't change Settings and leave them changed.

## Driving it

Use the driver, which launches its own headless Chromium (several auditors run at once, so don't use a shared browser
tool):

```
cd "C:/Users/LeeMessenger/AppData/Local/Temp/claude/e2e5beaddde4c070/da833503-3c78-46eb-9e8a-d618294b5af4/scratchpad/audit/scripts"
node drive.mjs <your-flow>.mjs advanced 2560 1380      # or: simple 1280 720
```

- Read `drive.mjs` and the example `f02-advanced-tour.mjs` first. A flow default-exports
  `async ({ page, shot, base, log }) => {}`; `page` is a Playwright page, `shot('name')` saves
  `../shots/name.png`.
- **Never `waitForLoadState('networkidle')`**: the app holds an event stream open. Use `waitForTimeout` or wait for a
  locator.
- Put your flows in `scripts/<zone>/` and your shots under a zone prefix (`shot('urls-01-…')`). Look at your shots with
  the Read tool: they are how you see the screen. Shots at 2560 wide are downscaled when you read them, so clip to a
  region (`shot(name, { clip: { x, y, width, height } })`) when you need to read small text.
- To learn what is clickable, don't trust the look: hover and click it in a flow, log `page.url()` before and after, and
  take a shot. To list the controls, log the accessibility tree or
  `page.locator('a,button,[role=button],[role=link]').evaluateAll(...)`.
- Windows opened with "Open in the browser" call `window.open`: catch the popup event and log its URL; don't load it.

## What to hand back

Write `C:\Projects\ShoutingIguana\.claude\worktrees\ui-step-3\docs\handoff\2026-11-19\audit-<zone>.md`:

1. **The tasks you tried**, each as: what an SEO wants, the path you took, the number of clicks, where it broke.
2. **Findings**, ranked, each with:
   - an id (`A-01`…), the screen, and a rating: **Blocks** (can't do it from here), **Slows** (detour, guess or hidden
     control), **Polish**;
   - what you did and what happened, in a sentence or two, with the shot's file name;
   - the code that produces it (`file:line`, found after you saw it on screen, not before);
   - the fix you'd make, concretely: what becomes a link to what, which column appears, which words change.
3. **What you did not test**, and why.

Every finding must come from something you saw on screen in your own shot. Don't list a problem you only inferred from
code, and don't soften: if you had to hunt for a control, say so. Then reply with the ten findings that matter most and
the path of your file, in under 500 words.
