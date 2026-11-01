# Direction A: precision

Lead references: Linear and Raycast, for speed and the keyboard. Written for [the stage 3 brief](../BRIEF.md).

## The idea

The crawler as a precision instrument: a dense, dark panel in the iguana's navy, where every measured number sits on
its scale and the keyboard is always on the surface. Colour is rationed so it carries meaning: blue marks only you
(your focus, your selection, your next action), severity and status colours appear only where something is off, and
everything normal stays in quiet ink.

## Five principles

1. **Blue is you.** The logo's blue marks where you are and what you'll do next: the focus ring (the one loud use),
   the selection (a tint) and the primary action. Progress, charts and crawl statuses never borrow it.
2. **Normal is quiet; anomalies glow.** A 200, an indexable page and a 117 ms response stay in ink. Colour appears
   where something is off: a 4xx, a 5,476 ms page, an issue's severity.
3. **Every number shows its scale.** A measured value sits on the limit the analysis used: a title's 61 characters
   against 30 to 60, a response against 1,000 ms, the crawl rate against its cap. You see how far off it is, not just
   that it is.
4. **The keyboard is on the surface.** Each action names its keys where you meet it, from the one registry the IA
   describes. Fields and the search trigger show theirs inline (Crawl Enter, Filter Ctrl F, Ctrl K); buttons show
   theirs in their tooltip; menus and the palette list them on the right.
5. **One size, set by weight and ink.** 13 px carries nearly everything. Hierarchy comes from weight and three inks;
   structure from hairlines on a 4 px grid and tone steps of the navy. Only things that float get a shadow.

## The pages

| File | What it shows |
| --- | --- |
| `home.html` | The URL field (focused, so its ring is the page's one loud colour), a running crawl pinned with live figures, and the library grouped by site: both real crawls plus illustrative ones, with Continue, Resume and Analyse again where a crawl needs them |
| `live.html` | The garden crawl at 60% (22 min 26 s in), from `garden/live.json`: crawled as the hero figure on a scale of everything found, the rate against the Polite cap, the status-code breakdown, found and crawled over time with its hover layer shown at 4 minutes, and an honest empty alerts panel |
| `issues.html` | The checks list by category, with two skipped checks shown as skipped with the analysis's reason. `empty-anchor-text` is selected: 265 issues become 2 groups by detail and related URL, the template fault the handoff describes. Export shows its tooltip with its key |
| `urls.html` | The Overview preset on internal HTML (274 of 4,521 URLs), sorted by issues then inlinks, with the product page selected and the inspector's Summary open on it |
| `urls-loading.html` | The same screen while loading. What the route already knows (the preset, the filters, the columns, the inspected URL, the tabs) shows at once; only the data is a skeleton, shaped like it, appearing after 150 ms. Reduced motion removes the shimmer |
| `palette.html` | Extra: the command palette over Issues, searching "brok". It reaches checks with their live counts, URLs and actions, with their keys |

Screenshots of every page in both themes are in `screens/` (gitignored), 1440 × 900 at a device scale factor of 1.

## How it's built

- `lib/tokens.mjs` is the one source of the tokens. `build.mjs` writes `tokens.css` from it, then the pages.
- `build.mjs` reads the real fixtures in `../../data/fixtures/` and writes static HTML. The pages need no build to view;
  rebuild only after changing the fixtures, the tokens or the templates: `node build.mjs`.
- `app.css` holds the components. It uses tokens only. Column widths are the one exception, set on each table as
  `--cols`, because they're view state: resizable and remembered per view. Positions computed from data (a gauge's
  needle, a chart's points) are inline percentages.
- `contrast.mjs` computes the contrast table below from the tokens and writes it, with the token tables, into this
  file. It exits 1 if any pair falls short.
- `shoot.mjs` takes the screenshots with the spike's Playwright: `node shoot.mjs`, or `--size 1280x800 --out <dir>`.
- Icons are drawn for this direction in `lib/icons.mjs` (16 px grid, 1.5 px strokes). No third-party icons.
- The pages load Inter and JetBrains Mono from Google Fonts. The only script in a page is the theme toggle.
- The logo comes from `Assets/logo.png` at the repository root, shown only in the sidebar header.

## Tokens

<!-- generated:tokens -->

### Colour

Brand: `--brand-blue` `#2B93FF` (the hexagon) and `--brand-navy` `#162832` (the iguana). Every neutral is generated
in OKLCH on the iguana's hue, 234, so both themes lean the same way as the logo.

| Token | Dark | Light | Used for |
| --- | --- | --- | --- |
| `--color-bg-chassis` | `#0D1D26` | `#F0F4F7` | Sidebar and top bar |
| `--color-bg-canvas` | `#11242E` | `#FDFEFF` | Where the data sits: main area, inspector |
| `--color-bg-raised` | `#182C37` | `#F4F7F9` | Fields, buttons, key chips, the palette |
| `--color-bg-hover` | `#1A2F3B` | `#ECF1F4` | Hover |
| `--color-bg-active` | `#213643` | `#E3E9EC` | Pressed; favicon tile |
| `--color-bg-tooltip` | `#304450` | `#162832` | Tooltips |
| `--color-line` | `#293B46` | `#DCE2E6` | Hairline dividers |
| `--color-line-subtle` | `#1B2D38` | `#E9EDF0` | Row separators |
| `--color-line-strong` | `#3B4E59` | `#C7CFD4` | Button edges, ticks |
| `--color-line-control` | `#6F8490` | `#818E97` | Field and chip boundaries (3:1) |
| `--color-text-strong` | `#F0F4F7` | `#162832` | Figures, titles, values |
| `--color-text` | `#D8DFE4` | `#283841` | Body text |
| `--color-text-muted` | `#A8B5BD` | `#53616A` | Labels, secondary text |
| `--color-text-faint` | `#94A2AB` | `#616E76` | Row numbers, axis labels, zero counts |
| `--color-text-on-accent` | `#071117` | `#071117` | Text on the primary button |
| `--color-text-tooltip` | `#F0F4F7` | `#F4F7F9` | Tooltip text |
| `--color-accent` | `#2B93FF` | `#2B93FF` | Primary action fill, selected tab |
| `--color-accent-hover` | `#41A1FF` | `#2286EE` | Primary action, hover |
| `--color-accent-text` | `#71B5FF` | `#0C68C2` | Links, the current section's icon |
| `--color-focus` | `#50A8FF` | `#1274D4` | Focus ring |
| `--color-sev-error` | `#F66D67` | `#C92F33` | Error |
| `--color-sev-warning` | `#EFB146` | `#B77610` | Warning |
| `--color-sev-notice` | `#76BECD` | `#2C7E8D` | Notice |
| `--color-http-2xx` | `#39AC6D` | `#1F8D54` | 2xx |
| `--color-http-3xx` | `#A07CDB` | `#7D4DBE` | 3xx |
| `--color-http-4xx` | `#D37B0D` | `#C46D00` | 4xx |
| `--color-http-5xx` | `#E2406F` | `#BE1253` | 5xx |
| `--color-http-none` | `#87959D` | `#6F7D85` | No response |
| `--color-data-1` | `#C9D6DE` | `#304049` | Measured: crawled, gauge needles |
| `--color-data-2` | `#6F8490` | `#7A8A94` | Measured: queued |
| `--color-data-3` | `#465A65` | `#CAD2D8` | A gauge's acceptable band |
| `--color-data-grid` | `#243640` | `#E3E9EC` | Chart gridlines, meter tracks |
| `--color-skeleton` | `#213642` | `#E2E8EC` | Skeleton blocks |
| `--color-skeleton-shine` | `#2C4350` | `#F0F4F6` | Skeleton shimmer |
| `--color-line-inverse` | `#73828A` | `#647078` | Key chips on a tooltip |
| `--color-line-on-accent` | `#206CB9` | `#206CB9` | Key chips on the primary button |
| `--color-selection` | `#15364F` | `#E8F3FF` | Selected row, item or section |
| `--color-selection-hover` | `#163A58` | `#E0EFFF` | Selected and hovered; text selection |
| `--color-tint-error` | `#363037` | `#F8E9EB` | Error background |
| `--color-tint-warning` | `#303831` | `#F5EEE2` | Warning background |
| `--color-tint-notice` | `#1F3A44` | `#E8F1F4` | Notice background |
| `--color-tint-4xx` | `#303229` | `#F6EDE0` | 4xx pill |
| `--color-tint-5xx` | `#322838` | `#F7E6EE` | 5xx pill |

### Type

Inter for text, JetBrains Mono for URLs and code, with ligatures off so a URL never shows a glyph that isn't in it.
Weights: `--weight-regular` 400, `--weight-medium` 500, `--weight-semibold` 600.
Tracking: `--tracking-hero` -0.025em, `--tracking-tight` -0.015em. Measure: `--measure` 68ch.

| Token | Size / line height | Used for |
| --- | --- | --- |
| `--text-micro` | 11 / 16 px | Key chips, gauge scale labels |
| `--text-small` | 12 / 16 px | Column headers, meta, pills, URLs in mono |
| `--text-base` | 13 / 20 px | Everything else: rows, body, buttons, navigation |
| `--text-title` | 16 / 24 px | The one title of a detail pane |
| `--text-field` | 18 / 24 px | The URL field on Home |
| `--text-readout` | 28 / 32 px | Readouts: counters on the live crawl, a check's page count |
| `--text-hero` | 48 / 52 px | The one hero figure per view (crawled, while live) |

### Spacing

A 4 px grid: `--space-0` 0, `--space-0-5` 2, `--space-1` 4, `--space-2` 8, `--space-3` 12, `--space-4` 16, `--space-5` 20, `--space-6` 24, `--space-8` 32, `--space-10` 40, `--space-12` 48, `--space-16` 64.

### Radii

| Token | px | Used for |
| --- | ---: | --- |
| `--radius-xs` | 3 | Key chips, bar segments, small icon buttons |
| `--radius-s` | 4 | Pills, chips, buttons, fields, rows |
| `--radius-m` | 6 | Menus, tooltips, the running crawl panel |
| `--radius-l` | 10 | The command palette |
| `--radius-round` | 999 | Dots and meters |

### Elevation

Surfaces are told apart by tone and hairlines. Only things that float get a shadow.

| Token | Dark | Light |
| --- | --- | --- |
| `--shadow-popover` | `0 0 0 1px rgb(0 0 0 / 0.2), 0 8px 24px rgb(2 9 13 / 0.55)` | `0 1px 2px rgb(22 40 50 / 0.08), 0 8px 24px rgb(22 40 50 / 0.14)` |
| `--shadow-palette` | `0 0 0 1px rgb(0 0 0 / 0.25), 0 24px 64px rgb(2 9 13 / 0.6)` | `0 2px 6px rgb(22 40 50 / 0.08), 0 24px 64px rgb(22 40 50 / 0.22)` |
| `--color-scrim` | `rgb(4 12 17 / 0.55)` | `rgb(22 40 50 / 0.4)` |

### Motion

| Token | Value | Used for |
| --- | --- | --- |
| `--duration-instant` | 80ms | Hover and press |
| `--duration-quick` | 150ms | Menus, tooltips, the palette |
| `--duration-panel` | 200ms | The inspector, folding groups |
| `--duration-skeleton-delay` | 150ms | A skeleton waits this long before it shows |
| `--duration-skeleton-min` | 300ms | Once shown, a skeleton stays at least this long |
| `--duration-shimmer` | 1600ms | One sweep of the skeleton shine (off with reduced motion) |
| `--duration-pulse` | 2000ms | The live dot (off with reduced motion) |
| `--ease-out` | `cubic-bezier(0.16, 1, 0.3, 1)` | Everything that moves |
| `--ease-in-out` | `cubic-bezier(0.65, 0, 0.35, 1)` | The skeleton sweep |

### Sizes

Focus ring: `--focus-width` 2 px, `--focus-offset` 2 px. Washes: `--opacity-wash` 0.22, `--opacity-wash-strong` 0.3.

| Token | px | Used for |
| --- | ---: | --- |
| `--size-topbar` | 44 | Top bar and sidebar header |
| `--size-sidebar` | 232 | Sidebar, expanded |
| `--size-sidebar-collapsed` | 52 | Sidebar, icons only |
| `--size-inspector` | 400 | Inspector, docked right |
| `--size-checks` | 368 | The checks list on Issues |
| `--size-row` | 28 | Grid row, compact (the default) |
| `--size-row-comfortable` | 36 | Grid row, comfortable |
| `--size-row-head` | 32 | Grid header row |
| `--size-control` | 28 | Buttons, fields, chips |
| `--size-control-s` | 24 | Small buttons and filter chips |
| `--size-control-l` | 48 | The URL field on Home |
| `--size-control-xs` | 20 | Status-code pills, key chips, chip buttons |
| `--size-row-list` | 32 | List rows with a figure (status codes) |
| `--size-bar` | 40 | Filter bar, the palette footer |
| `--size-row-two` | 48 | Two-line rows: the library |
| `--size-kbd` | 20 | Key chip height |
| `--size-chart` | 196 | Plot height of a full-width chart |
| `--size-icon` | 16 | Icons |
| `--size-icon-s` | 14 | Severity icons, inline icons |
| `--size-icon-xs` | 12 | Glyphs in pills and chips |
| `--size-dot` | 6 | Status-code dot |
| `--size-swatch` | 8 | Legend swatch, live dot |
| `--size-label` | 112 | Label column of a fact list |
| `--size-meter` | 160 | A stand-alone proportion meter |
| `--size-search` | 272 | The search and command trigger |
| `--size-menu` | 224 | Menu minimum width |
| `--size-palette` | 640 | The command palette |
| `--size-palette-top` | 96 | The palette’s distance from the top |
| `--size-content-max` | 1152 | Home’s content width |

<!-- /generated:tokens -->

## Accessibility

- **axe-core 4.10.2**, run on all six pages in both themes with the WCAG 2.0, 2.1 and 2.2 A and AA rules: no
  violations. (It was injected from a download for the check, not committed.)
- **Keyboard.** Every control is a real button, link or field; the focus ring is `--focus-width` of `--color-focus`
  at an offset, and meets 3:1 on every surface it can sit on, including a selected row. The URL grid is one tab stop
  with `aria-activedescendant` on the active row, as the ARIA grid pattern does for virtualised rows.
- **Live region.** The status pill in the top bar is `role="status"`, so a crawl's state changes are announced. The
  counters aren't: four updates a second would drown a screen reader.
- **Reduced motion.** No shimmer, no pulse, no transitions.
- **Windows high contrast.** `forced-colors` hands focus, selection and lines to the system colours.
- **1280 × 800.** Every page holds: the breadcrumb's site name and the search trigger shorten, the URL grid shows
  fewer columns beside the inspector, and the live overview scrolls by its last few lines.
- **200% zoom is not designed here.** At 200% a 1440 px window is 720 CSS px wide, and these fixed layouts crowd:
  the top bar's parts collide and the Issues detail squeezes. The app needs breakpoints for it: the sidebar collapses
  to icons, the inspector docks at the bottom, and the checks list becomes a drawer, all things the IA already allows.

## Contrast

WCAG 2 ratios, computed by `contrast.mjs` from the tokens above. Text needs 4.5:1; fields, chips, the focus ring,
icons and dots need 3:1. Severity is never told by colour alone: each has its own shape (diamond, triangle, circle)
and its word where there's room for it.

<!-- generated:contrast -->

| Pair | What | Dark | Light | Needs | Result |
| --- | --- | ---: | ---: | ---: | --- |
| `text-strong` on `bg-chassis` | text | 15.55 | 13.72 | 4.5:1 | Pass |
| `text-strong` on `bg-canvas` | text | 14.43 | 15.03 | 4.5:1 | Pass |
| `text-strong` on `bg-raised` | text | 13.06 | 14.11 | 4.5:1 | Pass |
| `text-strong` on `bg-hover` | text | 12.54 | 13.34 | 4.5:1 | Pass |
| `text-strong` on `selection` | text | 11.35 | 13.51 | 4.5:1 | Pass |
| `text` on `bg-chassis` | text | 12.77 | 10.96 | 4.5:1 | Pass |
| `text` on `bg-canvas` | text | 11.85 | 12.01 | 4.5:1 | Pass |
| `text` on `bg-raised` | text | 10.72 | 11.27 | 4.5:1 | Pass |
| `text` on `bg-hover` | text | 10.30 | 10.65 | 4.5:1 | Pass |
| `text` on `selection` | text | 9.32 | 10.79 | 4.5:1 | Pass |
| `text-muted` on `bg-chassis` | text | 8.20 | 5.78 | 4.5:1 | Pass |
| `text-muted` on `bg-canvas` | text | 7.61 | 6.33 | 4.5:1 | Pass |
| `text-muted` on `bg-raised` | text | 6.88 | 5.94 | 4.5:1 | Pass |
| `text-muted` on `bg-hover` | text | 6.61 | 5.62 | 4.5:1 | Pass |
| `text-muted` on `selection` | text | 5.98 | 5.69 | 4.5:1 | Pass |
| `text-faint` on `bg-chassis` | text | 6.57 | 4.75 | 4.5:1 | Pass |
| `text-faint` on `bg-canvas` | text | 6.09 | 5.20 | 4.5:1 | Pass |
| `text-faint` on `bg-raised` | text | 5.51 | 4.88 | 4.5:1 | Pass |
| `text-faint` on `bg-hover` | text | 5.30 | 4.61 | 4.5:1 | Pass |
| `text-faint` on `selection` | text | 4.79 | 4.67 | 4.5:1 | Pass |
| `accent-text` on `bg-chassis` | text | 7.99 | 5.03 | 4.5:1 | Pass |
| `accent-text` on `bg-canvas` | text | 7.42 | 5.51 | 4.5:1 | Pass |
| `accent-text` on `bg-raised` | text | 6.71 | 5.17 | 4.5:1 | Pass |
| `accent-text` on `bg-hover` | text | 6.45 | 4.89 | 4.5:1 | Pass |
| `accent-text` on `selection` | text | 5.83 | 4.95 | 4.5:1 | Pass |
| `text-on-accent` on `accent` | text on the primary button | 6.10 | 6.10 | 4.5:1 | Pass |
| `text-tooltip` on `bg-tooltip` | tooltip text | 9.18 | 14.11 | 4.5:1 | Pass |
| `text-strong` on `tint-error` | text on an error tint | 11.62 | 12.90 | 4.5:1 | Pass |
| `text-strong` on `tint-warning` | text on a warning tint | 10.94 | 13.16 | 4.5:1 | Pass |
| `text-strong` on `tint-4xx` | text on a 4xx pill | 11.77 | 13.09 | 4.5:1 | Pass |
| `text-strong` on `tint-5xx` | text on a 5xx pill | 12.69 | 12.65 | 4.5:1 | Pass |
| `line-control` on `bg-canvas` | field and chip boundary | 4.09 | 3.33 | 3:1 | Pass |
| `line-control` on `bg-chassis` | field and chip boundary | 4.41 | 3.04 | 3:1 | Pass |
| `accent` on `bg-canvas` | primary button boundary | 5.11 | 3.09 | 3:1 | Pass |
| `focus` on `bg-canvas` | focus ring | 6.37 | 4.65 | 3:1 | Pass |
| `focus` on `bg-chassis` | focus ring | 6.87 | 4.24 | 3:1 | Pass |
| `focus` on `selection` | focus ring on a selected row | 5.01 | 4.18 | 3:1 | Pass |
| `data-1` on `bg-canvas` | gauge needle, crawled | 10.76 | 10.64 | 3:1 | Pass |
| `data-2` on `bg-canvas` | queued | 4.09 | 3.53 | 3:1 | Pass |
| `sev-error` on `bg-chassis` | icon or dot | 5.98 | 4.82 | 3:1 | Pass |
| `sev-error` on `bg-canvas` | icon or dot | 5.55 | 5.28 | 3:1 | Pass |
| `sev-error` on `bg-raised` | icon or dot | 5.02 | 4.96 | 3:1 | Pass |
| `sev-error` on `selection` | icon or dot | 4.37 | 4.75 | 3:1 | Pass |
| `sev-warning` on `bg-chassis` | icon or dot | 9.05 | 3.38 | 3:1 | Pass |
| `sev-warning` on `bg-canvas` | icon or dot | 8.40 | 3.71 | 3:1 | Pass |
| `sev-warning` on `bg-raised` | icon or dot | 7.60 | 3.48 | 3:1 | Pass |
| `sev-warning` on `selection` | icon or dot | 6.61 | 3.33 | 3:1 | Pass |
| `sev-notice` on `bg-chassis` | icon or dot | 8.21 | 4.24 | 3:1 | Pass |
| `sev-notice` on `bg-canvas` | icon or dot | 7.61 | 4.65 | 3:1 | Pass |
| `sev-notice` on `bg-raised` | icon or dot | 6.89 | 4.36 | 3:1 | Pass |
| `sev-notice` on `selection` | icon or dot | 5.99 | 4.18 | 3:1 | Pass |
| `http-2xx` on `bg-chassis` | icon or dot | 5.98 | 3.80 | 3:1 | Pass |
| `http-2xx` on `bg-canvas` | icon or dot | 5.55 | 4.16 | 3:1 | Pass |
| `http-2xx` on `bg-raised` | icon or dot | 5.02 | 3.91 | 3:1 | Pass |
| `http-2xx` on `selection` | icon or dot | 4.36 | 3.74 | 3:1 | Pass |
| `http-3xx` on `bg-chassis` | icon or dot | 5.24 | 5.16 | 3:1 | Pass |
| `http-3xx` on `bg-canvas` | icon or dot | 4.87 | 5.66 | 3:1 | Pass |
| `http-3xx` on `bg-raised` | icon or dot | 4.40 | 5.31 | 3:1 | Pass |
| `http-3xx` on `selection` | icon or dot | 3.83 | 5.08 | 3:1 | Pass |
| `http-4xx` on `bg-chassis` | icon or dot | 5.41 | 3.43 | 3:1 | Pass |
| `http-4xx` on `bg-canvas` | icon or dot | 5.02 | 3.76 | 3:1 | Pass |
| `http-4xx` on `bg-raised` | icon or dot | 4.54 | 3.53 | 3:1 | Pass |
| `http-4xx` on `selection` | icon or dot | 3.95 | 3.38 | 3:1 | Pass |
| `http-5xx` on `bg-chassis` | icon or dot | 4.26 | 5.58 | 3:1 | Pass |
| `http-5xx` on `bg-canvas` | icon or dot | 3.95 | 6.12 | 3:1 | Pass |
| `http-5xx` on `bg-raised` | icon or dot | 3.57 | 5.74 | 3:1 | Pass |
| `http-5xx` on `selection` | icon or dot | 3.11 | 5.50 | 3:1 | Pass |
| `http-none` on `bg-chassis` | icon or dot | 5.58 | 3.84 | 3:1 | Pass |
| `http-none` on `bg-canvas` | icon or dot | 5.18 | 4.20 | 3:1 | Pass |
| `http-none` on `bg-raised` | icon or dot | 4.69 | 3.94 | 3:1 | Pass |
| `http-none` on `selection` | icon or dot | 4.08 | 3.78 | 3:1 | Pass |
| `sev-error` on `tint-error` | icon on its tint | 4.47 | 4.53 | 3:1 | Pass |
| `sev-warning` on `tint-warning` | icon on its tint | 6.36 | 3.24 | 3:1 | Pass |
| `http-4xx` on `tint-4xx` | dot on its tint | 4.09 | 3.27 | 3:1 | Pass |
| `http-5xx` on `tint-5xx` | dot on its tint | 3.47 | 5.15 | 3:1 | Pass |

<!-- /generated:contrast -->

The status-code colours also sit side by side (the live breakdown's bar), so they were run through a colour-blind
separation check (OKLab ΔE between neighbours, simulated for deutan, protan and tritan vision). Both themes pass: the
worst pair is 4xx and 5xx, at ΔE 10.3 (dark) and 13.2 (light) under deuteranopia, and 17.1 and 18.9 with normal
vision.

## What sets it apart

- **From B, clarity (light-first, comfortable):** precision is dark-first and compact. Rows are 28 px, so a large
  crawl shows 26 URLs and the inspector at once at 1440 × 900, and the space goes to figures rather than padding.
  Filters are the same chips, but the grid, not the filter bar, is the hero.
- **From C, guide (reads like a report):** precision keeps the chrome flat and quiet, and explains in measurement
  rather than prose. A check still says what's wrong, why and how to fix it, but the evidence is a scale and a count:
  a title's length on its band, a group's share of pages, a rate against its cap.
- **Its own:** the keyboard is visible on every screen, and colour is rationed hard. Nothing is blue unless it's you,
  and nothing is coloured unless it's off.

## Known trade-offs

- **Severe at first sight.** Dark, dense and quiet suits daily use more than a first impression. The light theme is
  the same design, and a comfortable density token (36 px rows) exists, though not shown.
- **Quiet crawl statuses.** Running, Done and Stopped are told by glyph, word and (for running) motion, not colour. In
  a long library, a stopped crawl is less striking than a coloured pill would make it; its action button carries it.
- **Quiet notices.** Notices are a calm teal-slate, below warnings by design. Someone who wants notices to pop won't
  get that.
- **Gauges cost height.** A value on its scale needs a line for the scale. The inspector's Summary scrolls at 900 px.
- **Tabular figures on the live hero number.** The guidance is proportional figures for big standalone numbers; a
  counter updating 4 times a second would jitter, so the live figures stay tabular.
- **Shortened hosts.** In a narrow column, a URL's host shortens to one fixed form (`www.garden…co.uk`), so a column
  lines up; the full URL is on hover, as the brief asks.

## Words

UK English and the glossary throughout. Two words were needed that the glossary doesn't define yet: **Not followed**,
for URLs found through nofollow links and deliberately not crawled ("skipped" is taken: it means a check that didn't
run), and **Found** for discovered, as the IA uses it.

## Where it departs from the brief, and why

- **Skipped checks show in the checks list by default**, in their category, with the reason. The IA puts them behind
  "Show all checks" with the passed ones. A skipped check that's hidden by default reads as clean, which the bar says
  never to do. Passed checks stay behind "Show them".
- **"Why it matters" is split from the catalog's description** at its ", so". `IssueCatalog` has `description` and
  `fix` but no separate `why`. Proposal: add a `why` field, so the three parts never depend on sentence shape.
- **No ETA on the live crawl.** The IA allows one only when a URL limit makes it meaningful; the 10,000 limit is far
  off, so the screen says why there's none and shows how long "found" has held still instead.
- **The inspector puts Headers, Redirects, Structured data, Hreflang and Source under More** at 400 px wide. Docked at
  the bottom, or wider, they come out as tabs.
- **Favicons are a letter tile.** The engine doesn't fetch favicons yet; the tile is the fallback the app will need
  anyway.
- **A sixth page**, `palette.html`, because this direction's lead is the keyboard.

## Real and illustrative data

Everything on the garden screens comes from the fixtures: the counts, URLs, titles, checks and their text, the
grouping, the live replay at its 60% marker, the inspector's product page, the thresholds. The synthetic 100k crawl
appears in the library as itself, `127.0.0.1`. Relative times on Home are from a mockup "now" of 1 Nov 2026, 17:20.

These are illustrative, invented to make Home look like an agency's library. They use the reserved `.example`
top-level domain, so none can be a real site:

| Site | Shown as | Figures |
| --- | --- | --- |
| `larchmere-hotels.example` | Running now, started 16:52, Standard speed | 18,406 crawled, 9,644 queued, 3,932 not followed, 11.6 URLs/s, 28 min 7 s |
| `brackenhurst-dental.example` | Done today at 11:05, 2 older crawls | 1,284 URLs, 212 pages, checks with issues 0, 4, 9; took 4 min 51 s |
| `pennant-cycles.example` | Stopped 30 Oct, partial analysis | 7,932 URLs, 2,118 pages, checks with issues 3, 11, 14 |
| `ashcombe-law.example` | Done 28 Oct, analysed with older limits, 1 older crawl | 642 URLs, 118 pages, checks with issues 1, 3, 7 |
| `marlowe-finch.example` | Interrupted 27 Oct, not analysed, 3 older crawls | 15,227 URLs, 4,980 pages |
