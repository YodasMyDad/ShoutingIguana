# Direction C: guide

Stage 3 of [the UI brief](../../../../docs/v2-ui-brief.md#how-to-work), against [the shared brief](../BRIEF.md). The lead
is Sitebulb's quality: the app explains.

## The idea

Shouting Iguana reads like a well-made audit report: every view opens with its answer in one plain sentence, and every
check says what's wrong, why it matters and how to fix it, with the crawl's own evidence beside it. A deep navy frame
from the logo holds a calm reading surface, and severity is one visual system, louder as it gets worse, from the
sidebar counts to the grid cells.

## Five principles

1. **Answer first, in one sentence.** Each view opens with a lede built from the data: "Every page crawled has an
   internal link without anchor text. On 264 of them it's the same link…". Its figures are links to their URLs. It's
   one sentence, and the data follows at once, so it never stands between an expert and the grid.
2. **Explain with the crawl's own evidence.** What's wrong, why it matters and how to fix it get real space, and beside
   them the proof: the affected pages drawn as a waffle, the offending HTML from the stored source, the indexability
   gates a URL passed. The words come from the catalog and the numbers from the crawl.
3. **Severity is a shape, a word and a colour, and it gets louder as it gets worse.** A red diamond for errors, an
   amber triangle for warnings, a quiet slate circle for notices; a dashed circle for skipped and a ring with a tick
   for passed. The same marks run from the sidebar to the rows. Notices are 829 of the garden crawl's 933 issues, so
   they get the quietest colour and the errors stand out.
4. **Draw it when a picture is faster.** Small, plain diagrams, each with a caption: where the found URLs are, the
   rate against the Polite cap, the status split with its 0.7% magnified, the indexability gates, a title's length
   against its limits, a template fault as one block of identical squares. No decoration and no donuts.
5. **The navy frames, the page reads.** The iguana's navy holds the navigation and the open crawl's health in both
   themes. The content is a calm document: Inter's display cut for titles, 15 px prose, 13 px data, tabular figures,
   and structure carried by type and hairlines rather than boxes.

## Files

| File | What |
| --- | --- |
| `home.html` | The crawl library: the URL field with what Enter will do, the running crawl pinned, sites with their latest crawl |
| `live.html` | The garden crawl's Overview while it runs, 60% of the way (`garden/live.json`, snapshot 1,346) |
| `overview.html` | **Extra.** The same Overview once analysed: the answer, with "Fix first". The lead puts "Fix first" here, so it's shown |
| `issues.html` | Issues, with `empty-anchor-text` selected: 265 issues that group into 2, one of them a template fault on 264 pages |
| `urls.html` | The URL grid (Overview preset, two filters, sorted by inlinks) with the inspector on the home page's Summary |
| `urls-loading.html` | The same screen as a skeleton: shown after 150 ms, shaped like the rows and the Summary, no shimmer with reduced motion |
| `tokens.css` | Every value: colour (light-dark pairs), type, space, radii, elevation, motion, sizes |
| `guide.css` | The components, using tokens only |
| `guide.js` | The theme toggle (sidebar, bottom right) and the expanders. Nothing else |
| `build/build.mjs` | Writes the six pages from the fixtures, so every number is real. `node build/build.mjs` |
| `build/icons.mjs` | The icon sprite, drawn for this direction |
| `build/contrast.mjs` | The contrast check below. `node build/contrast.mjs` exits 1 on any failing pair |
| `build/shoot.mjs` | The screenshots: `node build/shoot.mjs`, or `--size 1280x800` for the smaller check |

The pages are static HTML. They load Inter and JetBrains Mono from Google Fonts and the logo from
`../../assets/logo.png`; everything else is local.

## Real data, and what isn't

Every number, URL, title, check and note comes from `app/design/data/fixtures` through `build/build.mjs`. Some things
worth knowing:

- **Issues.** The check's title, description and fix are the catalog's (`api/checks.json`). The groups come from the
  issue rows' `detail` and `relatedUrl`. The HTML snippet is cut from the stored source of
  `/rattan-furniture/rattan-fire-pit-sets/` (`garden/inspector/category.json`); the build stops if it ever contains
  text, a title or an aria-label. "In the header" comes from the links' stored position.
- **Live.** Snapshot `markers["60%"]`. Which codes were internal or external at that moment comes from the URL rows
  fetched by then, and matches the snapshot's counts exactly.
- **Overview.** Status codes, depth, response times and indexability are counted from `garden/urls.json` and agree with
  `stats.json`. Skipped checks show the analysis notes, once per note.
- **The URL grid** is `garden/urls.json`: pages that are indexable (264), sorted by inlinks, with each row's issues
  counted by severity from `api/checks.json`. The inspector is `garden/inspector/home.json`.

**Illustrative, not real.** Home needs more than two crawls to look like an agency's library. These sites are invented,
on the reserved `.example` domain, and every number on them is made up:

| Site | Shown as |
| --- | --- |
| `kestrel-outdoor.example` | Crawling now (2,051 of 5,000 URLs) and in the sidebar's background crawls; one older crawl |
| `millbrook-tiles.example` | Stopped, with a partial analysis and Continue |
| `northvale-cycles.example` | Done but out of date, with Analyse again; two older crawls, shown expanded |
| `fernwood-legal.example` | Done; three older crawls, folded |

`127.0.0.1` is the real 100k synthetic crawl, and `gardenfurnituresets.co.uk` the real garden crawl.

**Not from the engine yet**, so drawn as placeholders: site favicons (an initial on a tile), alerts (the replay has
none, and the page says so), and per-check reasons for skips (E2; the notes stand in).

## Tokens

All of them are in `tokens.css` as custom properties; `guide.css` uses nothing else. Colours are `light-dark()` pairs:
the theme follows the OS, and `data-theme="light"` or `"dark"` on `<html>` overrides it by setting `color-scheme`. The
sidebar sets `color-scheme: dark`, so every pair resolves to its dark value inside the navy frame in both themes.

### Colour

The one accent is the logo's blue, `#2B93FF`. It fills primary buttons with the logo's dark as their text
(`#0E1B22`, 5.6:1): white on that blue is only 3.1:1. Text-sized blue uses `--accent-text`, and focus rings `--focus`.

| Token | Light | Dark |
| --- | --- | --- |
| `--brand-blue` | #2B93FF | same |
| `--brand-navy` | #162832 | same |
| `--canvas` | #F2F5F7 | #0D1216 |
| `--surface` | #FFFFFF | #12181D |
| `--surface-subtle` | #F7F9FA | #161D23 |
| `--surface-sunken` | #EEF2F5 | #0F1519 |
| `--surface-hover` | #F0F4F7 | #1A2229 |
| `--surface-selected` | #E6F1FF | #15304A |
| `--surface-selected-hover` | #DCECFF | #19385A |
| `--border` | #E1E7EB | #232D35 |
| `--border-strong` | #CAD3D9 | #2F3B45 |
| `--border-control` | #7A8995 | #637380 |
| `--text` | #15222B | #E3EAEE |
| `--text-secondary` | #44535E | #B4C1C9 |
| `--text-muted` | #5A6975 | #8F9EA8 |
| `--text-on-tint` | #15222B | #E3EAEE |
| `--accent` | #2B93FF | same |
| `--accent-hover` | #4DA4FF | same |
| `--accent-press` | #1C86F2 | same |
| `--on-accent` | #0E1B22 | same |
| `--accent-text` | #0A69CC | #63B3FF |
| `--accent-tint` | #E6F1FF | #14304B |
| `--accent-tint-strong` | #C7E0FF | #1E4A73 |
| `--focus` | #1373DB | #63B3FF |
| `--nav-bg` | #162832 | same |
| `--nav-bg-hover` | #1D3440 | same |
| `--nav-bg-active` | #203F52 | same |
| `--nav-line` | #26404D | same |
| `--nav-text` | #E2EBF0 | same |
| `--nav-text-muted` | #9DB1BD | same |
| `--nav-fav-bg` | #2A4554 | same |
| `--error` | #C4262E | #FF7B7B |
| `--error-tint` | #FDEDEE | #3A1D21 |
| `--warning` | #9A5A00 | #F2B04B |
| `--warning-tint` | #FFF3DD | #362A12 |
| `--notice` | #4B5A8F | #AAB6EC |
| `--notice-tint` | #EEF1FA | #1F2642 |
| `--neutral-mark` | #66737D | #98A6AF |
| `--sev-glyph` | #FFFFFF | #12181D |
| `--status-2xx` | #1B7A47 | #5BC98F |
| `--status-3xx` | #6A47BE | #B89EFF |
| `--status-4xx` | #B3470A | #FF9A5A |
| `--status-5xx` | #AE1F63 | #FF85BC |
| `--status-none` | #66737D | #98A6AF |
| `--ink` | #15222B | #E3EAEE |
| `--ink-soft` | #9AA8B2 | #50606C |
| `--ink-faint` | #DCE3E8 | #26313A |
| `--series` | #2B93FF | same |
| `--series-fill` | #2B93FF1F | #2B93FF2E |
| `--skeleton` | #E8EDF1 | #1B232A |
| `--skeleton-shine` | #F5F8FA | #252F38 |

### Type

Inter for text, with optical sizing on, so titles get its display cut. JetBrains Mono for URLs and code. Tabular
figures wherever numbers line up.

| Token | Size | Use |
| --- | ---: | --- |
| `--text-2xs` | 10 px | Favicon initials only |
| `--text-xs` | 12 px | Captions, counts, column headers, meta |
| `--text-code` | 12.5 px | JetBrains Mono, optically matched to 13 px Inter |
| `--text-sm` | 13 px | Data: grids, lists, the inspector |
| `--text-md` | 14 px | Interface: buttons, navigation, fields |
| `--text-prose` | 15 px | Explanations |
| `--text-lg` | 18 px | The lede, each view's one-sentence answer |
| `--text-xl` | 21 px | Pane titles |
| `--text-2xl` | 26 px | Page titles, the live figures |
| `--text-3xl` | 30 px | The Overview's severity figures |

Line heights `--leading-tight` 1.2, `--leading-snug` 1.35, `--leading-normal` 1.5 and `--leading-prose` 1.6. Weights
400, 500, 600 and 700. Tracking −0.018em for display sizes and −0.011em for headings. Prose stops at `--measure`, 68ch.

### Space, radii, elevation, motion

| Group | Tokens |
| --- | --- |
| Space (4 px scale) | `--space-0` 0, `--space-half` 2, `--space-1` 4, `--space-2` 8, `--space-3` 12, `--space-4` 16, `--space-5` 20, `--space-6` 24, `--space-7` 28, `--space-8` 32, `--space-10` 40, `--space-12` 48, `--space-16` 64 px |
| Radii | `--radius-xs` 3 px (bars, marks), `--radius-sm` 4 (kbd, favicons), `--radius-md` 6 (buttons, fields, chips, rows), `--radius-lg` 8 (panels, tables, cards), `--radius-xl` 12, `--radius-pill` (status pill, meters). Smaller things get smaller radii |
| Elevation | `--shadow-1` cards, the library and the URL field; `--shadow-2` menus; `--shadow-3` popovers and the skip link; `--shadow-panel` the inspector's edge. In dark, shadows deepen and surfaces lift instead |
| Motion | `--duration-instant` 80 ms, `--duration-fast` 150 ms, `--duration-base` 200 ms; `--ease-out` cubic-bezier(0.16, 1, 0.3, 1). Skeletons appear after `--skeleton-delay` 150 ms and stay at least `--skeleton-min` 300 ms; the shimmer is one slow sweep, `--shimmer-duration` 1.6 s. With reduced motion: no shimmer, no pulse, no transitions |
| Sizes | Sidebar 232 px, top bar 52, checks pane 352, inspector 384, rows 32 (compact 26), controls 28/32/52. Below 1,360 px wide the panes, the evidence column and three grid columns narrow (the block at the end of `tokens.css`) |

## Contrast

Computed from `tokens.css` by `build/contrast.mjs` (WCAG 2.2 relative luminance, alpha composited over the
background). Text needs 4.5:1; component boundaries, focus indicators and meaningful graphics need 3:1. Every pair
passes.

**Light**

| Foreground | Background | Ratio | Needs | Use |
| --- | --- | ---: | ---: | --- |
| `text` #15222B | `surface` #FFFFFF | 16.21:1 | 4.5:1 | Body text |
| `text` #15222B | `canvas` #F2F5F7 | 14.80:1 | 4.5:1 | Body text on the canvas |
| `text` #15222B | `surface-subtle` #F7F9FA | 15.35:1 | 4.5:1 | Text in panes and grid headers |
| `text` #15222B | `surface-sunken` #EEF2F5 | 14.40:1 | 4.5:1 | Text in wells and code |
| `text` #15222B | `surface-selected` #E6F1FF | 14.19:1 | 4.5:1 | Text in a selected row |
| `text-secondary` #44535E | `surface` #FFFFFF | 7.94:1 | 4.5:1 | Secondary text |
| `text-secondary` #44535E | `surface-subtle` #F7F9FA | 7.52:1 | 4.5:1 | Secondary text in panes |
| `text-secondary` #44535E | `surface-selected` #E6F1FF | 6.95:1 | 4.5:1 | Secondary text in a selected row |
| `text-muted` #5A6975 | `surface` #FFFFFF | 5.66:1 | 4.5:1 | Muted text, captions |
| `text-muted` #5A6975 | `canvas` #F2F5F7 | 5.17:1 | 4.5:1 | Muted text on the canvas |
| `text-muted` #5A6975 | `surface-subtle` #F7F9FA | 5.36:1 | 4.5:1 | Muted text in panes |
| `text-muted` #5A6975 | `surface-sunken` #EEF2F5 | 5.03:1 | 4.5:1 | Placeholder text in fields |
| `text-muted` #5A6975 | `surface-hover` #F0F4F7 | 5.12:1 | 4.5:1 | Muted text in a hovered row |
| `text-muted` #5A6975 | `surface-selected` #E6F1FF | 4.95:1 | 4.5:1 | Muted text in a selected row |
| `accent-text` #0A69CC | `surface` #FFFFFF | 5.38:1 | 4.5:1 | Links |
| `accent-text` #0A69CC | `surface-subtle` #F7F9FA | 5.09:1 | 4.5:1 | Links in panes |
| `accent-text` #0A69CC | `accent-tint` #E6F1FF | 4.71:1 | 4.5:1 | Links on accent tint |
| `text` #15222B | `accent-tint` #E6F1FF | 14.19:1 | 4.5:1 | Filter chip text |
| `on-accent` #0E1B22 | `accent` #2B93FF | 5.61:1 | 4.5:1 | Primary button text |
| `on-accent` #0E1B22 | `accent-hover` #4DA4FF | 6.73:1 | 4.5:1 | Primary button text, hover |
| `on-accent` #0E1B22 | `accent-press` #1C86F2 | 4.79:1 | 4.5:1 | Primary button text, pressed |
| `error` #C4262E | `surface` #FFFFFF | 5.73:1 | 4.5:1 | Error word and icon |
| `error` #C4262E | `error-tint` #FDEDEE | 5.06:1 | 4.5:1 | Error on its tint |
| `warning` #9A5A00 | `surface` #FFFFFF | 5.47:1 | 4.5:1 | Warning word and icon |
| `warning` #9A5A00 | `warning-tint` #FFF3DD | 4.98:1 | 4.5:1 | Warning on its tint |
| `notice` #4B5A8F | `surface` #FFFFFF | 6.65:1 | 4.5:1 | Notice word and icon |
| `notice` #4B5A8F | `notice-tint` #EEF1FA | 5.89:1 | 4.5:1 | Notice on its tint |
| `neutral-mark` #66737D | `surface` #FFFFFF | 4.87:1 | 4.5:1 | Skipped and passed marks |
| `error` #C4262E | `surface-selected` #E6F1FF | 5.02:1 | 4.5:1 | Error in a selected row |
| `warning` #9A5A00 | `surface-selected` #E6F1FF | 4.79:1 | 4.5:1 | Warning in a selected row |
| `notice` #4B5A8F | `surface-selected` #E6F1FF | 5.82:1 | 4.5:1 | Notice in a selected row |
| `sev-glyph` #FFFFFF | `error` #C4262E | 5.73:1 | 3:1 | Glyph inside the error icon |
| `sev-glyph` #FFFFFF | `warning` #9A5A00 | 5.47:1 | 3:1 | Glyph inside the warning icon |
| `sev-glyph` #FFFFFF | `notice` #4B5A8F | 6.65:1 | 3:1 | Glyph inside the notice icon |
| `status-2xx` #1B7A47 | `surface` #FFFFFF | 5.36:1 | 4.5:1 | 2xx |
| `status-3xx` #6A47BE | `surface` #FFFFFF | 6.45:1 | 4.5:1 | 3xx |
| `status-4xx` #B3470A | `surface` #FFFFFF | 5.49:1 | 4.5:1 | 4xx |
| `status-5xx` #AE1F63 | `surface` #FFFFFF | 6.61:1 | 4.5:1 | 5xx |
| `status-none` #66737D | `surface` #FFFFFF | 4.87:1 | 4.5:1 | No response |
| `status-2xx` #1B7A47 | `surface-subtle` #F7F9FA | 5.07:1 | 4.5:1 | 2xx in panes |
| `status-4xx` #B3470A | `surface-subtle` #F7F9FA | 5.20:1 | 4.5:1 | 4xx in panes |
| `border-control` #7A8995 | `surface` #FFFFFF | 3.60:1 | 3:1 | Field and checkbox boundaries |
| `border-control` #7A8995 | `surface-subtle` #F7F9FA | 3.41:1 | 3:1 | Field boundaries in panes |
| `focus` #1373DB | `surface` #FFFFFF | 4.66:1 | 3:1 | Focus ring |
| `focus` #1373DB | `canvas` #F2F5F7 | 4.26:1 | 3:1 | Focus ring on the canvas |
| `focus` #1373DB | `surface-selected` #E6F1FF | 4.08:1 | 3:1 | Focus ring in a selected row |
| `accent` #2B93FF | `surface` #FFFFFF | 3.12:1 | 3:1 | Accent bars and the selected-row edge |
| `series` #2B93FF | `surface` #FFFFFF | 3.12:1 | 3:1 | Live data series |
| `ink-soft` #9AA8B2 | `surface` #FFFFFF | 2.44:1 | none | Decorative: gridlines, connectors (no requirement) |

**Dark**

| Foreground | Background | Ratio | Needs | Use |
| --- | --- | ---: | ---: | --- |
| `text` #E3EAEE | `surface` #12181D | 14.71:1 | 4.5:1 | Body text |
| `text` #E3EAEE | `canvas` #0D1216 | 15.49:1 | 4.5:1 | Body text on the canvas |
| `text` #E3EAEE | `surface-subtle` #161D23 | 13.99:1 | 4.5:1 | Text in panes and grid headers |
| `text` #E3EAEE | `surface-sunken` #0F1519 | 15.13:1 | 4.5:1 | Text in wells and code |
| `text` #E3EAEE | `surface-selected` #15304A | 11.12:1 | 4.5:1 | Text in a selected row |
| `text-secondary` #B4C1C9 | `surface` #12181D | 9.72:1 | 4.5:1 | Secondary text |
| `text-secondary` #B4C1C9 | `surface-subtle` #161D23 | 9.25:1 | 4.5:1 | Secondary text in panes |
| `text-secondary` #B4C1C9 | `surface-selected` #15304A | 7.35:1 | 4.5:1 | Secondary text in a selected row |
| `text-muted` #8F9EA8 | `surface` #12181D | 6.49:1 | 4.5:1 | Muted text, captions |
| `text-muted` #8F9EA8 | `canvas` #0D1216 | 6.84:1 | 4.5:1 | Muted text on the canvas |
| `text-muted` #8F9EA8 | `surface-subtle` #161D23 | 6.18:1 | 4.5:1 | Muted text in panes |
| `text-muted` #8F9EA8 | `surface-sunken` #0F1519 | 6.68:1 | 4.5:1 | Placeholder text in fields |
| `text-muted` #8F9EA8 | `surface-hover` #1A2229 | 5.84:1 | 4.5:1 | Muted text in a hovered row |
| `text-muted` #8F9EA8 | `surface-selected` #15304A | 4.91:1 | 4.5:1 | Muted text in a selected row |
| `accent-text` #63B3FF | `surface` #12181D | 8.03:1 | 4.5:1 | Links |
| `accent-text` #63B3FF | `surface-subtle` #161D23 | 7.63:1 | 4.5:1 | Links in panes |
| `accent-text` #63B3FF | `accent-tint` #14304B | 6.06:1 | 4.5:1 | Links on accent tint |
| `text` #E3EAEE | `accent-tint` #14304B | 11.11:1 | 4.5:1 | Filter chip text |
| `on-accent` #0E1B22 | `accent` #2B93FF | 5.61:1 | 4.5:1 | Primary button text |
| `on-accent` #0E1B22 | `accent-hover` #4DA4FF | 6.73:1 | 4.5:1 | Primary button text, hover |
| `on-accent` #0E1B22 | `accent-press` #1C86F2 | 4.79:1 | 4.5:1 | Primary button text, pressed |
| `error` #FF7B7B | `surface` #12181D | 7.13:1 | 4.5:1 | Error word and icon |
| `error` #FF7B7B | `error-tint` #3A1D21 | 6.08:1 | 4.5:1 | Error on its tint |
| `warning` #F2B04B | `surface` #12181D | 9.44:1 | 4.5:1 | Warning word and icon |
| `warning` #F2B04B | `warning-tint` #362A12 | 7.41:1 | 4.5:1 | Warning on its tint |
| `notice` #AAB6EC | `surface` #12181D | 9.04:1 | 4.5:1 | Notice word and icon |
| `notice` #AAB6EC | `notice-tint` #1F2642 | 7.50:1 | 4.5:1 | Notice on its tint |
| `neutral-mark` #98A6AF | `surface` #12181D | 7.16:1 | 4.5:1 | Skipped and passed marks |
| `error` #FF7B7B | `surface-selected` #15304A | 5.39:1 | 4.5:1 | Error in a selected row |
| `warning` #F2B04B | `surface-selected` #15304A | 7.14:1 | 4.5:1 | Warning in a selected row |
| `notice` #AAB6EC | `surface-selected` #15304A | 6.83:1 | 4.5:1 | Notice in a selected row |
| `sev-glyph` #12181D | `error` #FF7B7B | 7.13:1 | 3:1 | Glyph inside the error icon |
| `sev-glyph` #12181D | `warning` #F2B04B | 9.44:1 | 3:1 | Glyph inside the warning icon |
| `sev-glyph` #12181D | `notice` #AAB6EC | 9.04:1 | 3:1 | Glyph inside the notice icon |
| `status-2xx` #5BC98F | `surface` #12181D | 8.68:1 | 4.5:1 | 2xx |
| `status-3xx` #B89EFF | `surface` #12181D | 7.98:1 | 4.5:1 | 3xx |
| `status-4xx` #FF9A5A | `surface` #12181D | 8.54:1 | 4.5:1 | 4xx |
| `status-5xx` #FF85BC | `surface` #12181D | 7.95:1 | 4.5:1 | 5xx |
| `status-none` #98A6AF | `surface` #12181D | 7.16:1 | 4.5:1 | No response |
| `status-2xx` #5BC98F | `surface-subtle` #161D23 | 8.26:1 | 4.5:1 | 2xx in panes |
| `status-4xx` #FF9A5A | `surface-subtle` #161D23 | 8.12:1 | 4.5:1 | 4xx in panes |
| `border-control` #637380 | `surface` #12181D | 3.66:1 | 3:1 | Field and checkbox boundaries |
| `border-control` #637380 | `surface-subtle` #161D23 | 3.48:1 | 3:1 | Field boundaries in panes |
| `focus` #63B3FF | `surface` #12181D | 8.03:1 | 3:1 | Focus ring |
| `focus` #63B3FF | `canvas` #0D1216 | 8.45:1 | 3:1 | Focus ring on the canvas |
| `focus` #63B3FF | `surface-selected` #15304A | 6.07:1 | 3:1 | Focus ring in a selected row |
| `accent` #2B93FF | `surface` #12181D | 5.72:1 | 3:1 | Accent bars and the selected-row edge |
| `series` #2B93FF | `surface` #12181D | 5.72:1 | 3:1 | Live data series |
| `ink-soft` #50606C | `surface` #12181D | 2.75:1 | none | Decorative: gridlines, connectors (no requirement) |

**Sidebar (both themes)**

| Foreground | Background | Ratio | Needs | Use |
| --- | --- | ---: | ---: | --- |
| `nav-text` #E2EBF0 | `nav-bg` #162832 | 12.56:1 | 4.5:1 | Navigation text |
| `nav-text` #E2EBF0 | `nav-bg-hover` #1D3440 | 10.74:1 | 4.5:1 | Navigation text, hover |
| `nav-text` #E2EBF0 | `nav-bg-active` #203F52 | 9.17:1 | 4.5:1 | Navigation text, current |
| `nav-text-muted` #9DB1BD | `nav-bg` #162832 | 6.83:1 | 4.5:1 | Counts and meta |
| `nav-text-muted` #9DB1BD | `nav-bg-hover` #1D3440 | 5.85:1 | 4.5:1 | Counts, hover |
| `nav-text-muted` #9DB1BD | `nav-bg-active` #203F52 | 4.99:1 | 4.5:1 | Counts, current |
| `error` #FF7B7B | `nav-bg` #162832 | 6.05:1 | 4.5:1 | Error mark in the sidebar |
| `warning` #F2B04B | `nav-bg` #162832 | 8.01:1 | 4.5:1 | Warning mark in the sidebar |
| `notice` #AAB6EC | `nav-bg` #162832 | 7.67:1 | 4.5:1 | Notice mark in the sidebar |
| `accent` #2B93FF | `nav-bg` #162832 | 4.86:1 | 3:1 | Current-section bar |
| `focus` #63B3FF | `nav-bg` #162832 | 6.81:1 | 3:1 | Focus ring in the sidebar |
| `accent` #2B93FF | `nav-line` #26404D | 3.49:1 | 3:1 | Progress bar on its track |

## What sets it apart

- **From A, precision (Linear and Raycast).** A is built for speed and the keyboard, dark-first and dense, saying as
  little as possible. Guide says more on purpose: every view answers in a sentence, every check explains itself, and
  diagrams carry the reasoning. It's light-first in feel, framed by the logo's navy, with medium density.
- **From B, clarity (Stripe and Vercel).** B makes tables and filters calm and roomy, with figures given space. Guide
  shares the calm, but its centre is the explanation rather than the table: evidence beside every explanation, captions under every
  figure, and a severity system that runs through everything. Where B shows the number, guide also says what it means.
- **Its own.** The severity loudness ramp, the lede sentences with linked figures, the evidence panel (waffle and
  stored HTML), "Time left: Not known yet" rather than a guess, and skipped checks shown with their reasons wherever
  checks are listed.

## Trade-offs and compromises

- **Words cost space.** Ledes and captions push data down by a line or two. An expert setting, "Show explanations"
  (on by default), could fold them to the first sentence. The grid itself carries no prose.
- **What's wrong, why it matters.** The catalog has a description and a fix, no separate "why". So "Why it matters"
  shows the description (which often states the problem too), and "What's wrong" is written from the crawl: counts,
  groups and the evidence. The app would need a sentence template per check, or a summary from the engine; this mockup
  writes the one it shows by hand from the real numbers. The Overview's "Fix first" lines use only generic wording
  ("Most often: 1 image over 200 KB (37 of 61)"), which the app can produce for any check.
- **Evidence needs stored HTML.** The snippet depends on the crawl storing HTML (the garden crawl did; the default is
  off) and on the engine pointing at the link's place in the source. Without it, the panel keeps the waffle and the
  groups.
- **URLs in grids.** Internal URLs show their path after the site's favicon, and external URLs show in full, with the
  host. That bends the brief's "the host and the end stay visible": the host is the favicon, because 38 identical
  characters on every row leave no room for the path. Hover and every copy give the full URL.
- **Skipped checks are always listed**, not only under "Show all checks": a skip should never be hidden. Passed checks
  stay behind the toggle.
- **"Crawl settings"** in the sidebar, not "Settings", so it isn't confused with the app's Settings at the bottom.
- **A sixth page.** `overview.html` isn't required, but the lead's "Fix first" lives there.
- **`light-dark()`** needs Chromium 123, Safari 17.5 or Firefox 120. WebView2 is fine; an old WebKitGTK on Linux would
  need the two-block fallback (`@media (prefers-color-scheme)` plus `[data-theme]`).
- **The notice colour** is a quiet slate, close to neutral by design. The circle shape and the word carry it, which
  also holds in forced-colours mode.
- **The live rate** shows bursts above the cap in the first minute. That's the replay's approximation (see the data
  README), shown as it is.

## Icons

Drawn for this direction on a 16 px grid with a 1.5 px stroke (`build/icons.mjs`). No third-party set is used.

## Regenerate

```powershell
node app/design/directions/guide/build/build.mjs      # the six pages, from app/design/data/fixtures
node app/design/directions/guide/build/contrast.mjs   # exits 1 if any pair fails
node app/design/directions/guide/build/shoot.mjs      # screens/<page>-<theme>.png at 1440x900
```

Screenshots are in `screens/` (gitignored), named `<page>-<theme>.png`; `screens/1280/` holds the 1280×800 check.
