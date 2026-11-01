# Stage 3: the visual directions brief

Stage 3 of [the UI brief](../../../docs/v2-ui-brief.md#how-to-work). Three directions take the same four screens,
the same layout and the same real data, each in its own visual language. One is picked, or the best parts are mixed,
and its tokens become the app's design system.

## Read first

- [The UI brief](../../../docs/v2-ui-brief.md): the bar, "Visual design system", "Accessibility", the states matrix
  and the grid spec.
- [The IA](../../../docs/v2-ui-ia.md): the shell, the routes and the screens.
- [The glossary](../../../docs/v2-ui-discovery.md#glossary): use its words exactly, in UK English.
- [The data README](../data/README.md): the garden crawl's real numbers. The fixtures are in `../data/fixtures/`.

## Fixed for every direction

- **The shell, as the IA draws it.**
  - The sidebar: Home; the open crawl's sections with their counts; crawls running in the background; Settings.
  - The top bar: back and forward; where you are (the site with its favicon, the crawl, the section); the status
    pill; the crawl's controls; search, with its Ctrl+K hint.
  - The inspector on the right.
- **One accent: the logo's blue, `#2B93FF`.** Derive the tints and shades each theme needs for text, focus rings and
  fills, and check their contrast. The iguana's dark is `#162832`.
- **Semantic colours, used only for their meaning.** Severity (error, warning, notice) and status-code classes
  (2xx, 3xx, 4xx, 5xx).
- **Type.** Inter for text, JetBrains Mono for URLs and code, tabular figures for numbers. Load both from Google
  Fonts in the mockups; the app will bundle them.
- **Both themes from the same HTML.** `prefers-color-scheme` by default, overridden by `data-theme="light"` or
  `"dark"` on `<html>`.
- **Tokens.** Every value lives in the direction's `tokens.css` as a CSS custom property: colour, a 4 px spacing
  scale, radii, elevation, type sizes and motion timings. Components use tokens only.
- **WCAG 2.2 AA.**
  - Text contrast of at least 4.5:1, or 3:1 for large text, component boundaries and focus indicators, in both
    themes.
  - A visible focus style.
  - Nothing told by colour alone: severity also has an icon or a label.
- **Words.** UK English, sentence case, the glossary's terms (crawl, URL, page, check, issue, alert, inspector).
- **Real data.** Every number, URL, title and check comes from the fixtures. Home needs more crawls than the two
  real ones to look like an agency's library. Add a few clearly fictional sites (invented names, never real
  companies) and list them in your README as illustrative.
- **Static.** HTML and CSS. JavaScript only for small things like the theme toggle and tabs, inline or in a local
  file: no frameworks, no scripts from a CDN. Pages render offline, apart from Google Fonts.
- **Size.** Design for a 1440×900 window, and check that it holds at 1280×800.

## The screens: one HTML file each

1. **`home.html`: the crawl library.**
   - The URL field.
   - A running crawl pinned at the top with its live progress.
   - The library grouped by site, with the garden crawl's real status, pages and issues by severity.
2. **`live.html`: the garden crawl's Overview while it runs, about 60% of the way**, from `garden/live.json`.
   - The counters, a URLs-per-second sparkline, the status breakdown and the controls.
   - The alerts area. This crawl raised none, so show that honestly.
3. **`issues.html`: Issues with a check selected.**
   - The checks list, grouped by category, with severity and counts. One skipped check is shown as skipped, with
     its reason, never as 0.
   - The selected check: what's wrong, why it matters and how to fix it, then its rows grouped by detail. Pick a
     check from the fixtures whose grouping means something.
4. **`urls.html`: the URL grid with the inspector open.**
   - The grid: the Overview column preset, filter chips, the "x of y URLs" count, a sort indicator and a selected
     row.
   - The inspector, on one of the six inspector fixtures: the Summary tab, with the tab strip visible.
5. **`urls-loading.html`: the same screen as a skeleton.** The brief's rules: it appears after 150 ms, it's shaped
   like the content, and reduced motion means no shimmer.

## Deliverables

In `app/design/directions/<direction>/`:

- **The five HTML files**, with `tokens.css` and any other CSS.
- **`README.md`.**
  - The idea in two sentences, and five principles.
  - The tokens: each theme's colours as a table with hex, the type scale, spacing, radii, elevation and motion.
  - Contrast ratios for every text and background pair, and for the severity and status colours. Compute them with
    a small script; don't guess.
  - What sets it apart from the other two leads, and its known trade-offs.
- **`screens/`.**
  - Screenshots of every page in both themes at 1440×900, with a device scale factor of 1, named
    `<page>-<theme>.png`.
  - Take them with Playwright: the spike has it in `app/spike/node_modules/playwright`, and Chromium is in the
    shared cache.
  - `screens/` is gitignored.

Look at your own screenshots and fix whatever looks off before you report. Judge them against the bar: "super slick,
enterprise level".

## Don't

- Touch anything outside your direction's folder, commit, or use `git stash`.
- Copy another product's look. No logos, trade dress or icons from Linear, Stripe, Vercel, Raycast or Sitebulb: take
  their qualities, not their skin.
- Use icons from elsewhere without saying so. Draw simple inline SVG icons, or inline an open-licence set such as
  Lucide (ISC), and say which.

## The three leads

| Direction | Lead reference | In short |
| --- | --- | --- |
| A, `precision` | Linear and Raycast: speed and the keyboard | Compact and dark-first; cool neutrals tinted towards the logo's navy; hairlines, not shadows |
| B, `clarity` | The Stripe and Vercel dashboards: tables and filters | Light-first and comfortable; generous space; a filter bar of chips; figures given room |
| C, `guide` | Sitebulb: explaining issues | Reads like a well-made report; what, why and how up front; a deep navy sidebar frames the content |
