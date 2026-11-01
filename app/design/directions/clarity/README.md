# Direction B: clarity

**The idea.** The grid is the product: every screen ends in a table or leads to one, every number opens its URLs,
and the filter that got you there stays on screen as chips that read like a sentence, "Indexable is yes". It's
light-first and comfortable, white sheets on a quiet grey canvas with soft layered elevation and large, calm figures,
so 100k rows feel manageable rather than dense.

Lead reference: the Stripe and Vercel dashboards, for their tables and filters. Their qualities, not their look: no
logos, trade dress or icons from either.

## The screens

| File | What it shows |
| --- | --- |
| [`home.html`](home.html) | The URL field, a crawl running now, and the library grouped by site |
| [`live.html`](live.html) | The garden crawl's Overview at 60%: counters, URLs a second, found and crawled, status codes, no alerts |
| [`issues.html`](issues.html) | The checks by category, with skipped checks and their reasons; "Internal links without anchor text" selected, its 265 rows grouped into 2 |
| [`urls.html`](urls.html) | The grid with the Overview columns, three filter chips, "264 of 4,521 URLs", sorted by issues, row 12 selected, the inspector on its Summary |
| [`urls-loading.html`](urls-loading.html) | The same screen as a skeleton: it appears after 150 ms, it's shaped like the rows, and reduced motion stops the shimmer |

Open them straight from disk. They need Google Fonts for Inter and JetBrains Mono, and nothing else. The theme follows
the OS; the switch at the bottom of the sidebar overrides it, and so does `?theme=light` or `?theme=dark`. The density
switch on the URL grid, or `?density=compact`, shows compact rows.

Screenshots are in `screens/` (gitignored): every page in both themes at 1440×900, `<page>-<theme>.png`. Also
`urls-compact-<theme>.png` for compact density and `<page>-1280-light.png` for the 1280×800 check.

## Five principles

1. **The table is the product.** Every screen ends in a grid or leads to one. Counts, bars and status rows are links
   into URLs that carry their filter with them, so the answer to "which ones?" is always one click.
2. **Filters read as sentences.** A chip is a field, an operator and a value, "Status is 4xx", and weight does the
   work: the field is secondary, the value is strong. The same chip appears wherever a number opens URLs: in the
   filter bar, on a hovered status row, beside "Show all 264 pages in URLs". The count, "264 of 4,521 URLs", is never
   off screen.
3. **Colour only means something.** The neutrals carry no hue in either theme. Blue means you can act here, or that
   this is selected or live. Red, amber and slate mean severity, each with its own shape (octagon, triangle, circle),
   and the status colours mark status classes. A normal 200 stays quiet, so a 404 stands out.
4. **Figures get room.** Large, medium-weight, tabular numbers, with units set smaller and muted (22 min 26 s,
   117 ms). Faint gridlines, direct labels instead of legends, and a context line that explains each number ("1,592
   nofollow, not followed", "Down from 3,410 at 3 min 47 s").
5. **Lift means focus.** Elevation is a z-order, not decoration. Chrome sits flat on the canvas, content sits on white
   sheets, the inspector floats above the grid and popovers float above that; the current nav item lifts too. In dark,
   a surface gets lighter as it rises and carries a faint top highlight, because shadows don't read on near-black.

## What sets it apart

- **From A, precision** (Linear and Raycast; compact, dark-first, navy-tinted neutrals, hairlines). Clarity is
  light-first and comfortable: 40 px rows by default and 28 px compact, neutrals with no hue, and soft layered shadows
  to separate the layers. It trades some rows per screen for faster scanning and a calmer read. Its keyboard story is
  the same (shortcut hints on every control, the key legend under the grid), but it isn't the lead idea.
- **From C, guide** (Sitebulb; reads like a report, a deep navy sidebar). Clarity keeps the explanation short (what,
  why and how in one three-column band) and gives the evidence table the space. Its sidebar is quiet chrome on the
  canvas rather than a coloured frame, so the eye goes to the content.

## Trade-offs

- Comfortable rows show about 14 grid rows at 1440×900 with the inspector open; compact shows about 20. Heavy users
  will switch to compact, which is one click and is remembered.
- The whitespace costs scrolling: at 1280×800 the live Overview's alerts card and the library's lower rows need a
  scroll.
- Shadows are subtler in dark. The dark theme leans on lighter surfaces as things rise, and only cards, the inspector
  and popovers have shadows, never grid rows, so big grids pay nothing for them.
- White surfaces are bright for long sessions. The dark theme answers that, and it follows the OS.
- It belongs to a familiar family of SaaS dashboards. Its identity comes from the chips, the figures and the colour
  discipline, not from a bold palette, which is right for a tool but less memorable at first sight.

## Where it bends the brief

1. **URLs on the crawled host show their path.** The column header names the host once ("URL on
   www.gardenfurnituresets.co.uk"), and long paths truncate in the middle so the end stays visible. Any other host,
   and the site's other versions (http, or the bare domain), show in full; the inspector, hover and copy always use
   the full URL. The brief says long URLs truncate in the middle so the host and the end stay visible. With a
   29-character host in every row, that left about 11 characters for the path in a 280 px column. A "Show full URLs"
   option in the column menu would restore the letter of the rule.
2. **Skipped checks always show in the checks list,** in their category with "Skipped" and the reason. The IA shows
   them only under "Show all checks"; hiding them reads as clean, which the brief's "It never misleads" rules out.
   Passed checks stay behind the switch.
3. **What's wrong and why it matters are split** from the catalog's one description at ", so". The real app needs
   `IssueCatalog` to give the why its own field, or it shows the sentence whole.
4. **Severity words.** Every severity mark has its own shape and an accessible name, as this stage's brief asks. The
   word is visible where there's room: the selected check's badge, the library's counts, the inspector's issue summary
   ("1 warning, 4 notices"). Dense rows (the checks list, the inspector's issue lines, the severity filter) show shape
   and colour, with the word as the accessible name, which is short of the UI brief's "icon plus colour plus word".
5. **The crawl running on Home is illustrative.** The brief asks for the garden crawl's real, completed status in the
   library, so the pinned running crawl is a fictional site. The live screen is the real garden crawl at 60%.
6. **The live ETA is an upper bound:** "At this pace, done within 1 h 1 min", from the 10,000-URL limit and the
   Polite cap of 2 a second. The queue is still being fed, so no closer estimate is honest.
7. **A hover state is frozen into `live.html`:** the 4xx row shows the chip it opens ("Status is 4xx") and its key.
8. **Favicons are letter monograms.** The fixtures have no favicons; a fetched favicon would replace the letter.
9. **Grid column widths are inline,** not tokens: they're view state, resizable and remembered per view.

## Tokens

All values live in [`tokens.css`](tokens.css) as custom properties, and [`app.css`](app.css) uses nothing else.
Each colour token holds both themes, `light-dark(<light>, <dark>)`. `color-scheme: light dark` makes the page follow
`prefers-color-scheme`, and `data-theme="light"` or `"dark"` on `<html>` sets `color-scheme` to override it. The
tables below are generated from `tokens.css` by `build/tokens-doc.mjs`.

### Colour

The accent is the logo's blue, `#2B93FF`. In light it's a shade deeper for marks (`#1F86F5`, so the selected-row rule
and bars hold 3:1 on the canvas) and deeper again for text and fills (`#0B6BD3`, 5:1 with white). In dark the brand
blue itself marks things (6:1 on the surfaces), a lighter tint (`#4FA6FF`) carries text, and primary buttons use
`#1A6ED3` under white. Severity and status colours are used for nothing else.

<!-- colours:start -->
| Token | Light | Dark | Use |
| --- | --- | --- | --- |
| `--color-canvas` | #F5F5F7 | #0A0A0B | The app frame: sidebar, top bar, page background |
| `--color-surface` | #FFFFFF | #121214 | Cards, grids, inputs |
| `--color-surface-raised` | #FFFFFF | #18181B | The inspector |
| `--color-surface-overlay` | #FFFFFF | #1E1E22 | Popovers, menus, the palette |
| `--color-surface-sunken` | #FAFAFB | #0E0E10 | Table headers in groups, the explanation band, keycaps |
| `--color-surface-hover` | #F2F2F4 | #1C1C20 | Hovered rows and controls; tracks of segmented controls |
| `--color-surface-active` | #EAEAED | #242428 | Pressed controls |
| `--color-surface-selected` | #EBF4FF | #132538 | Selected rows and list items |
| `--color-border-subtle` | #EEEEF1 | #1D1D21 | Row rules, dividers inside cards |
| `--color-border` | #E3E3E7 | #28282D | Card and button edges |
| `--color-border-strong` | #CFCFD5 | #3A3A40 | Hovered edges, dashed "add" chips |
| `--color-border-control` | #8A8A93 | #6E6E77 | Inputs, toggles, the selected segment (3:1) |
| `--color-text` | #18181B | #EDEDEF | Primary text and figures |
| `--color-text-secondary` | #46464D | #B4B4BB | Labels, supporting text |
| `--color-text-muted` | #68686F | #8E8E96 | Meta text, units, placeholders |
| `--color-text-on-accent` | #FFFFFF | #FFFFFF | Text on primary buttons |
| `--color-brand` | #2B93FF | #2B93FF | The logo blue, as sampled |
| `--color-iguana` | #162832 | #162832 | The logo dark, as sampled |
| `--color-accent` | #1F86F5 | #2B93FF | Selected-row rule, tab underline, bars, chart lines |
| `--color-accent-text` | #0B6BD3 | #4FA6FF | Links, the current nav icon, the running pill |
| `--color-accent-fill` | #0B6BD3 | #1A6ED3 | Primary buttons |
| `--color-accent-fill-hover` | #095DB8 | #1F74D9 | Primary buttons, hovered |
| `--color-accent-subtle` | #EBF4FF | #132538 | Tinted backgrounds for live and selected things |
| `--color-accent-border` | #A8CFFF | #1F4C7C | Edges of tinted accent things; queued bars |
| `--color-focus` | #1A7DEF | #4FA6FF | Focus rings |
| `--color-error` | #D93636 | #F26161 | Severity, only |
| `--color-error-text` | #C22828 | #FF8A84 | Severity, only |
| `--color-error-subtle` | #FDEEEE | #2B1416 | Severity, only |
| `--color-warning` | #F5A70A | #F5B53A | Severity, only |
| `--color-warning-edge` | #B06C00 | #F5B53A | Severity, only |
| `--color-warning-glyph` | #3A2600 | #2A1C00 | Severity, only |
| `--color-warning-text` | #8C5400 | #F7C25E | Severity, only |
| `--color-warning-subtle` | #FFF5DF | #2A2010 | Severity, only |
| `--color-notice` | #5F6B79 | #95A1AF | Severity, only |
| `--color-notice-text` | #545F6C | #A9B3BF | Severity, only |
| `--color-notice-subtle` | #F0F2F5 | #1B1E23 | Severity, only |
| `--color-2xx` | #1F9D68 | #3CC48A | Status-code class, only |
| `--color-2xx-text` | #13774A | #5FD3A1 | Status-code class, only |
| `--color-2xx-subtle` | #E7F6EE | #11251B | Status-code class, only |
| `--color-3xx` | #7F55E6 | #A889FF | Status-code class, only |
| `--color-3xx-text` | #6538D2 | #BBA3FF | Status-code class, only |
| `--color-3xx-subtle` | #F2EDFD | #1F1932 | Status-code class, only |
| `--color-4xx` | #DD6510 | #F58B3D | Status-code class, only |
| `--color-4xx-text` | #AE4E08 | #FFA468 | Status-code class, only |
| `--color-4xx-subtle` | #FDF0E6 | #2B1C10 | Status-code class, only |
| `--color-5xx` | #D93636 | #F26161 | Status-code class, only |
| `--color-5xx-text` | #C22828 | #FF8A84 | Status-code class, only |
| `--color-5xx-subtle` | #FDEEEE | #2B1416 | Status-code class, only |
| `--color-no-response` | #85858E | #74747D | Status-code class, only |
| `--color-no-response-text` | #5E5E66 | #A3A3AB | Status-code class, only |
| `--color-no-response-subtle` | #F1F1F3 | #1D1D21 | Status-code class, only |
| `--color-chart-grid` | #EDEDF0 | #1F1F23 | Charts |
| `--color-chart-axis` | #68686F | #8E8E96 | Charts |
| `--color-chart-primary` | #1F86F5 | #2B93FF | Charts |
| `--color-chart-primary-fill` | rgb(31 134 245 / 0.09) | rgb(43 147 255 / 0.09) | Charts |
| `--color-chart-secondary` | #8A8A93 | #7A7A84 | Charts |
| `--color-chart-limit` | #68686F | #8E8E96 | Charts |
| `--color-chart-track` | #EEEEF1 | #222226 | Charts |
| `--color-skeleton` | #EDEDF0 | #1E1E22 | Skeletons |
| `--color-skeleton-shine` | #F7F7F9 | #27272C | Skeletons |
| `--color-scrim` | rgb(24 24 27 / 0.32) | rgb(0 0 0 / 0.6) |  |
| `--color-text-selection` | #CFE5FF | #1F4C7C |  |
<!-- colours:end -->

### Type

Inter for text, JetBrains Mono for URLs and code, both from Google Fonts here and bundled in the app. The scale
follows the classic steps (11, 12, 13, 14, 16, 18, 21, 28, 36, 48).

<!-- type:start -->
| Token | Size | Line height | Used for |
| --- | --- | --- | --- |
| `--text-11` | 11px | 14px | Chart axes, tab counts, keycaps |
| `--text-12` | 12px | 16px | Meta, captions, column headers, chips (small) |
| `--text-13` | 13px | 18px | Secondary UI, buttons, chips, lists |
| `--text-14` | 14px | 20px | Body and grid cells (comfortable) |
| `--text-16` | 16px | 24px | Running crawl name, the URL field |
| `--text-18` | 18px | 26px | Section titles, the result count |
| `--text-21` | 21px | 28px | Check titles, inspector figures |
| `--text-28` | 28px | 34px | Page titles; figures at 1280 wide |
| `--text-36` | 36px | 40px | KPI figures |
| `--text-48` | 48px | 52px | Reserved for a single hero figure |

Weights: `--weight-regular` 400, `--weight-medium` 500, `--weight-semibold` 600. Tracking: `--tracking-tight` -0.022em, `--tracking-figure` -0.03em, `--tracking-snug` -0.011em, `--tracking-normal` 0, `--tracking-wide` 0.01em. Inter's optical sizes are on (`font-optical-sizing: auto`), so titles and figures get its display cut. Every number column and figure uses tabular, lining figures.
<!-- type:end -->

### Spacing

A 4 px scale.

<!-- spacing:start -->
| Token | Value |
| --- | --- |
| `--space-0-5` | 2px |
| `--space-1` | 4px |
| `--space-1-5` | 6px |
| `--space-2` | 8px |
| `--space-2-5` | 10px |
| `--space-3` | 12px |
| `--space-4` | 16px |
| `--space-5` | 20px |
| `--space-6` | 24px |
| `--space-7` | 28px |
| `--space-8` | 32px |
| `--space-10` | 40px |
| `--space-12` | 48px |
| `--space-16` | 64px |
<!-- spacing:end -->

### Radii

Corners grow with the size of the thing, so the hierarchy shows in them too.

<!-- radii:start -->
| Token | Value |
| --- | --- |
| `--radius-xs` | 4px |
| `--radius-sm` | 6px |
| `--radius-md` | 8px |
| `--radius-lg` | 12px |
| `--radius-xl` | 14px |
| `--radius-full` | 999px |
<!-- radii:end -->

### Elevation

<!-- elevation:start -->
| Token | Light and dark (light-dark() per colour) | Level |
| --- | --- | --- |
| `--elevation-0` | `none` | Chrome on the canvas |
| `--elevation-1` | `0 1px 2px 0 light-dark(rgb(16 16 20 / 0.04), rgb(0 0 0 / 0.40)), 0 1px 1px 0 light-dark(rgb(16 16 20 / 0.03), rgb(0 0 0 / 0.30))` | Cards, grids, the current nav item |
| `--elevation-2` | `0 2px 4px -1px light-dark(rgb(16 16 20 / 0.04), rgb(0 0 0 / 0.40)), 0 12px 32px -8px light-dark(rgb(16 16 20 / 0.12), rgb(0 0 0 / 0.60))` | The inspector, above the grid; the URL field |
| `--elevation-3` | `0 4px 8px -2px light-dark(rgb(16 16 20 / 0.06), rgb(0 0 0 / 0.45)), 0 16px 40px -10px light-dark(rgb(16 16 20 / 0.18), rgb(0 0 0 / 0.70))` | Popovers and menus |
| `--elevation-4` | `0 8px 16px -4px light-dark(rgb(16 16 20 / 0.08), rgb(0 0 0 / 0.50)), 0 32px 64px -16px light-dark(rgb(16 16 20 / 0.24), rgb(0 0 0 / 0.80))` | The command palette and dialogs |
| `--elevation-control` | `0 1px 1px 0 light-dark(rgb(16 16 20 / 0.05), rgb(0 0 0 / 0.35))` | Buttons and chips |
| `--highlight-top` | `inset 0 1px 0 0 light-dark(rgb(255 255 255 / 0), rgb(255 255 255 / 0.05))` | A top highlight: on in dark, off in light (accent: both) |
| `--highlight-accent` | `inset 0 1px 0 0 light-dark(rgb(255 255 255 / 0.16), rgb(255 255 255 / 0.14))` | A top highlight: on in dark, off in light (accent: both) |
<!-- elevation:end -->

### Motion

150 to 200 ms, easing out, for orientation. With reduced motion there's no shimmer, no pulse and no transitions.

<!-- motion:start -->
| Token | Value | Use |
| --- | --- | --- |
| `--duration-instant` | 100ms | Hover and press feedback |
| `--duration-quick` | 150ms | Popovers, menus, tabs |
| `--duration-panel` | 200ms | The inspector and drawers |
| `--duration-skeleton-delay` | 150ms | A skeleton appears only after this |
| `--duration-shimmer` | 1600ms | One shimmer sweep (off with reduced motion) |
| `--duration-pulse` | 2000ms | The live dot (off with reduced motion) |
| `--ease-out` | cubic-bezier(0.22, 1, 0.36, 1) | Everything that moves |
| `--ease-in-out` | cubic-bezier(0.65, 0, 0.35, 1) | Reserved for things that move and come back |
<!-- motion:end -->

Layout and component sizes (sidebar 240 px, inspector 400 px, top bar 52 px, controls 32 px, rows 40 px or 28 px,
and so on) are tokens too; see `tokens.css`. At 1360 px and narrower the frame tightens: sidebar 224 px, inspector
368 px, figures 28 px.

## Contrast

WCAG 2.2 AA, computed by `build/contrast.mjs` from `tokens.css` (192 pairs, none failing; run it to check). Text needs
4.5:1; boundaries, focus indicators, marks and chart lines need 3:1.

<!-- contrast:start -->
#### Light

Text needs 4.5:1.

| Text | `canvas` | `surface` | `surface-raised` | `surface-overlay` | `surface-sunken` | `surface-hover` | `surface-selected` |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `text` | 16.27 | 17.72 | 17.72 | 17.72 | 16.98 | 15.85 | 15.96 |
| `text-secondary` | 8.60 | 9.36 | 9.36 | 9.36 | 8.97 | 8.37 | 8.43 |
| `text-muted` | 5.08 | 5.53 | 5.53 | 5.53 | 5.30 | 4.95 | 4.98 |
| `accent-text` | 4.75 | 5.18 | 5.18 | 5.18 | 4.96 | 4.63 | 4.66 |

On the primary button: `text-on-accent` on `accent-fill` 5.18, on `accent-fill-hover` 6.42.

Severity and status colours: their text shade needs 4.5:1, their mark or icon 3:1.

| Colour | Text on `surface` | on `surface-raised` | on `surface-selected` | on its own `-subtle` | Mark or icon on `surface` (3:1) |
| --- | ---: | ---: | ---: | ---: | ---: |
| `error` | 5.79 | 5.79 | 5.21 | 5.14 | 4.63 |
| `warning` | 6.21 | 6.21 | 5.59 | 5.73 | 4.21 |
| `notice` | 6.50 | 6.50 | 5.86 | 5.80 | 5.43 |
| `2xx` | 5.58 | 5.58 | 5.02 | 5.00 | 3.45 |
| `3xx` | 6.89 | 6.89 | 6.20 | 6.00 | 4.83 |
| `4xx` | 5.39 | 5.39 | 4.86 | 4.82 | 3.53 |
| `5xx` | 5.79 | 5.79 | 5.21 | 5.14 | 4.63 |
| `no-response` | 6.42 | 6.42 | 5.79 | 5.70 | 3.66 |

The warning triangle's amber fill is 2.02:1 on `surface`; its darker edge (`warning-edge`, above) carries the 3:1, and its glyph is 7.15:1 on the fill. The error and notice glyphs are 4.63:1 and 5.43:1 on their shapes.

Boundaries and indicators need 3:1.

| Non-text | `canvas` | `surface` | `surface-raised` | `surface-sunken` | `surface-hover` | `surface-selected` |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `border-control` | 3.14 | 3.42 | 3.42 | 3.28 | 3.06 | 3.08 |
| `focus` | 3.69 | 4.02 | 4.02 | 3.85 | 3.60 | 3.62 |
| `accent` | 3.33 | 3.63 | 3.63 | 3.48 | 3.25 | 3.27 |
| `chart-secondary` | 3.14 | 3.42 | 3.42 | 3.28 | 3.06 | 3.08 |

#### Dark

Text needs 4.5:1.

| Text | `canvas` | `surface` | `surface-raised` | `surface-overlay` | `surface-sunken` | `surface-hover` | `surface-selected` |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `text` | 16.93 | 16.00 | 15.15 | 14.21 | 16.49 | 14.53 | 13.31 |
| `text-secondary` | 9.60 | 9.08 | 8.59 | 8.06 | 9.35 | 8.24 | 7.55 |
| `text-muted` | 6.09 | 5.76 | 5.45 | 5.11 | 5.93 | 5.22 | 4.79 |
| `accent-text` | 7.76 | 7.33 | 6.94 | 6.51 | 7.56 | 6.66 | 6.10 |

On the primary button: `text-on-accent` on `accent-fill` 4.98, on `accent-fill-hover` 4.61.

Severity and status colours: their text shade needs 4.5:1, their mark or icon 3:1.

| Colour | Text on `surface` | on `surface-raised` | on `surface-selected` | on its own `-subtle` | Mark or icon on `surface` (3:1) |
| --- | ---: | ---: | ---: | ---: | ---: |
| `error` | 8.22 | 7.78 | 6.83 | 7.59 | 5.93 |
| `warning` | 11.43 | 10.83 | 9.51 | 9.78 | 10.29 |
| `notice` | 8.81 | 8.34 | 7.33 | 7.87 | 7.12 |
| `2xx` | 10.09 | 9.55 | 8.39 | 8.68 | 8.42 |
| `3xx` | 8.73 | 8.26 | 7.26 | 7.88 | 6.85 |
| `4xx` | 9.59 | 9.08 | 7.98 | 8.43 | 7.70 |
| `5xx` | 8.22 | 7.78 | 6.83 | 7.59 | 5.93 |
| `no-response` | 7.47 | 7.07 | 6.21 | 6.71 | 4.04 |

The warning triangle's amber fill is 10.29:1 on `surface`, and its glyph is 9.14:1 on the fill. The error and notice glyphs are 5.93:1 and 7.12:1 on their shapes.

Boundaries and indicators need 3:1.

| Non-text | `canvas` | `surface` | `surface-raised` | `surface-sunken` | `surface-hover` | `surface-selected` |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `border-control` | 3.92 | 3.71 | 3.51 | 3.82 | 3.36 | 3.08 |
| `focus` | 7.76 | 7.33 | 6.94 | 7.56 | 6.66 | 6.10 |
| `accent` | 6.33 | 5.99 | 5.67 | 6.17 | 5.44 | 4.98 |
| `chart-secondary` | 4.66 | 4.41 | 4.17 | 4.54 | 4.00 | 3.66 |

<!-- contrast:end -->

Also:

- **Focus.** A 2 px `--color-focus` outline, 2 px outside the control, on every focusable thing.
- **Inputs** have a 3:1 boundary (`--color-border-control`). Buttons and chips are identified by their text, so their
  edges are free to be soft.
- **Selected and current states** carry a 3:1 mark: the 2 px accent rule on selected rows and checks, the accent
  underline on tabs, the 3:1 edge on the selected segment, the accent icon on the current nav item.
- **High contrast.** Under `forced-colors`, cards and controls get system-colour borders, selected things a Highlight
  outline, and the severity shapes stay distinct without colour.
- **Grids** use the grid roles with `aria-selected`, `aria-sort` and `aria-rowcount`; the loading grid is `aria-busy`.

## Data

Every number, URL, title and check comes from `app/design/data/fixtures/`: the garden crawl (`garden/urls.json`,
`garden/live.json`, `garden/inspector/product.json`, `api/garden/*`) and, in the library, the 100k synthetic crawl
(`api/crawls.json`). The checks' titles, descriptions and fixes come from `api/checks.json`.

These library entries are **illustrative**, on the reserved `.example` domain so they can't be real companies:

| Site | Shown as |
| --- | --- |
| kestrel-legal.example | The crawl running now on Home (Standard speed, 2,388 URLs crawled), and an older finished crawl |
| oakmere-cycles.example | Done, out of date, with 2 older crawls |
| fernleigh-pottery.example | Done, with 1 older crawl |
| brightwater-dental.example | Stopped, with a partial analysis and Continue |
| tidewell-travel.example | Done, but its file has moved: "File not found" with "Locate the file…" |

"Now" is 1 Nov 2026, 17:42 on Home, Issues and URLs, after both real crawls were analysed. The live screen is
31 Oct 2026, 14:36:24, 60% of the way through the garden crawl. UK time is GMT on both dates.

## Icons

[Lucide](https://lucide.dev) (ISC licence), inlined as an SVG sprite in each page. The severity shapes, the skipped and
passed marks, and the square status-code mark are drawn for this direction.

## Rebuild

The pages are static HTML, generated from the fixtures so every number is real. From this folder:

```powershell
node build/build.mjs          # writes the five pages from ../../data/fixtures
node build/contrast.mjs       # checks every colour pair; exits 1 on a failure
node build/tokens-doc.mjs     # rewrites the tables in this README from tokens.css
node build/shoot.mjs          # screenshots into screens/, with the spike's Playwright
node build/shoot.mjs urls.html --density=compact
node build/shoot.mjs --size=1280x800 --suffix=-1280 --themes=light
```

The fixtures are gitignored; regenerate them with `app/design/data/extract/run-all.mjs` before building. Edit the
templates in `build/pages/` and `build/lib/`, not the generated HTML.
