# Step 3 design direction

Stage 3 of [the UI brief](v2-ui-brief.md#how-to-work). Three directions were made on the garden crawl's real data
(`app/design/directions/`, briefed in [BRIEF.md](../app/design/directions/BRIEF.md)). Picked on 2026-11-01, and
confirmed by the owner the same day: "B looks good".

## The pick

**Direction B, clarity, is the base**: its tokens, surfaces, type scale, elevation, filter chips, figures and Home.
A few ideas from A and C join it, redrawn in B's style. Copying a component's look from A or C as it is would bring
back the mixed look the old app had.

Why B:

- **The grid is the product.** Filter chips that read as sentences ("Status is 4xx"), the "x of y URLs" count that
  never leaves the screen, and every number opening its URLs. This is the brief's core interaction, and B does it
  best.
- **Both themes are first class.** The app follows the OS theme. B's light theme is calm and its near-black dark
  theme is just as considered.
- **It's the calmest.** Colour only means something: greys with no hue, blue for "you can act here" or selection,
  and severity and status colours only for their meanings.
- **Home is right for an agency.** It has a large URL field, the running crawl as a card with live figures, and the
  library as a table of sites with issues by severity.

## What joins from A, precision

- **Density in grids.** Rows of 32 px by default, with compact (28 px) and comfortable (40 px) as the density
  setting. Grid screens get a one-line header (title, then "264 of 4,521 URLs") instead of B's large page title, so
  rows get the room. Home and Overview keep B's large titles.
- **Every value on its scale.** In the inspector, a value is drawn against the limit the analysis used: title
  length against 30–60, response time against 1,000 ms, word count against its minimum.
- **"Normal is quiet; anomalies glow."** A 200, an indexable page and a fast response stay plain. A 4xx, a page
  over the response limit and an issue's severity get colour.
- **The keyboard on the surface.** Keys in buttons and tooltips (Pause F7, Stop F8), the grid's key hints in its
  footer, and A's command palette (`palette.html`) as the palette's model.

## What joins from C, guide

- **"Indexable, and why"** in the inspector: the four tests (answered 2xx, allowed by robots.txt, no noindex,
  canonical is itself) as a checklist, with the one that failed marked.
- **Issues counted by severity in the sidebar.** Errors, warnings and notices under Issues, each opening the
  filtered list.
- **The magnified status bar.** The share that didn't answer 2xx is drawn enlarged beneath the full bar, so 0.7%
  is still visible.
- **Answer first, where the engine can say it truthfully.** Overview, Issues and each check open with one sentence
  built from the data ("19 checks found 933 issues. 63 passed and 3 were skipped.").
  - Sentences built from counts only are in scope now.
  - Per-check prose, such as "it's most likely part of the site's template", needs a template per check and
    engine support. See below.

## Rules this changes

- **Skipped checks are always listed**, in their category, with the reason, never behind "Show all checks".
  Hiding them reads as clean, which breaks "it never misleads". All three directions reached this independently.
  Checks that passed stay behind the toggle.
- **URLs on the crawled host show their path** after the site's favicon, with the host named once in the column
  header. Other hosts, and the site's other versions (http, the bare domain), show in full. A "Show full URLs" view
  option restores the brief's rule. Hover and copy always give the full URL.
- **Severity everywhere is a shape and a colour.**
  - The shapes: a red error mark, an amber warning triangle and a quiet notice circle. Skipped is a dashed circle.
  - The word appears wherever there's room: lists, chips, headers, the inspector. In dense grid cells the word is
    the mark's accessible name and its tooltip.
  - Notices are the quietest, since they're most of the issues (829 of 933 on the garden crawl), so errors stand
    out.

## The framed workspace (2026-11-15)

The owner, using the installed app (light theme especially, dark too): the left side and the segmentation were
"wishy washy and difficult to see", there was "no clear segmentation between areas or the navigation", and the accent
rule along the current item's edge looked AI-made: "that's what all AI does and it looks AI made". They wanted hints
from good enterprise apps. Three directions were mocked (scratch `navdesign/`); they chose **B, framed workspace**. Its
colours and spacings are the spec (`styles/tokens.css` holds them as tokens, `light-dark()` as ever):

- **The frame and the panel.** The window is the frame (`--color-frame`: light `#eeeef1`, dark `#09090b`). The sidebar
  sits on it with no edge of its own. Everything right of the sidebar, the top bar and the screen (and the inspector
  docked in it), is **one panel** inset from the window's edge (`--frame-inset`, 8 px top, right and bottom, none on
  the sidebar's side), with a 1 px edge (`--color-border-panel`: `#d6d6dc`, `#26262b`), 10 px corners
  (`--radius-panel`), a faint lift in light (`--shadow-panel`) and none in dark. The canvas is the panel's colour now
  (`#ffffff`, `#101012`); the screen scrolls inside the panel, under the top bar's bottom edge. **Small windows:** the
  inset is 4 px at 800 px wide and under, and none at 640 px and under (1280 px at 200% zoom), where the panel keeps
  only its edge against the sidebar and square corners: at that size every pixel is the screen's, and an 8 px moat
  round a 640 px window reads as waste, not structure.
- **The sidebar.** The brand row; Home; the open crawl as a **switcher** (a bordered button: favicon, site, date, an
  up and down chevron) that opens a menu of the library's eight most recent crawls, the open one ticked, each opening
  on the section you're on, then "All crawls, on Home"; the crawl's sections; Report after a small gap (no rule);
  the live section ("Crawling now", exports) near the bottom; Settings at the very bottom. Group labels are sentence
  case, 12 px, semibold, muted. Items are 32 px, in the sidebar's own text and icon colours, with their own hover.
- **The current place** (`.si-current`, everywhere: the sidebar, Settings' sections, the palette's and a menu's
  highlighted item, a list's chosen row, Simple's row under J and K) is **a solid neutral fill** (the sidebar's
  `#dcdce2` / `#232328` on the frame; `--color-current-inset` on the panel, `--color-current-overlay` in a menu),
  its text and icon near-black or white (`#111113` / `#ffffff`) at 600. **No accent rule, no blue text.** Counts on
  it take its text colour; severity marks keep theirs (they're data). Why this still reads: the fill, the weight and
  the darker text together, aria-current or aria-selected for a screen reader, its text at 4.5:1 on the fill, and a
  Highlight ring under forced colours. The 3:1 the old accent rule kept against its tint is given up on purpose: the
  owner's call, as enterprise apps such as Linear and GitHub draw their current place.
- **The grid's selected rows** lose their accent rule too and keep the selection's tint (`surface-selected`), which
  hover never covers; the keyboard's cursor is the focus outline over it. The tint stays blue-grey rather than a
  neutral so that a selection (data you picked) isn't confused with the place you are.
- **Segmentation in screens.** Cards are the surface with a stronger edge (`--color-border-card`: `#dcdce1`,
  `#2c2c32`), 8 px corners (`--radius-lg`, was 12) and no shadow; a card's header is divided from its body by that
  edge, never a tinted band. Grids' frames take the same edge. The Overview's figures are cards of their own in a row
  with a 12 px gap (`Stats cards`). In dark the surface is `#161619`, a step up from the panel.
- **Focus rings are unchanged** (an outline in `--color-focus`), and reduced motion is respected as before.

## What the engine needs for this design

| Need | Why | When |
| --- | --- | --- |
| A "why it matters" text per check in `IssueCatalog` | Every direction had to split the description at ", so" to show what, why and how | Before the Issues slice |
| Group hints: a group that covers most affected pages is flagged as a likely template fault | B's "one fix clears the group" callout; a simple share threshold, computed in the issues reader | E3 |
| Where the offending element is in the stored HTML, per issue row where it applies | C's evidence snippet ("the link, from the stored HTML") | Later; needs stored HTML |
| Favicons | Every direction uses them in the breadcrumb, sidebar, library and grids | E6 |

## Simple (2026-11-11)

Simple is the same look, calmer and larger: not a second design. Its screens are designed in
[Simple mode](v2-ui-simple.md) and mocked in `app/design/simple/`, on these tokens and components.

- **One reading column**, 1,040 px at most, centred. Advanced's screens fill the window, as grids need.
- **One step up the type scale.** Reading text is 16 px where Advanced's is 14, row titles are 16 px semibold, and
  a problem's row is 72 px tall. Nothing new is added to the scale: the 48 px step, "reserved for a single hero
  figure", is the score.
- **The score is a ring in the accent blue** on a neutral track, with its word beside it. Never red, amber or
  green: those mean severity and status, and nothing else. A crawl that didn't finish draws the ring dashed and
  says "so far". Under forced colours the arc is Highlight.
- **Severity keeps its shapes and colours.** Only the words change: Serious, Worth fixing, Worth a look.
- **No grid, no chips, no inspector.** A list is rows of title, one sentence and a count with its share. At most
  two labels on a row.
- **Messages are the accent's**, as app messages already are: a banner for "stopped early", a callout for "One
  fix". A severity colour is never used for one.
- **The report is paper.** Light in both themes, on the canvas, in a frame. It carries its own small style sheet,
  with the clarity light values written in, as it must stand alone.
- **The switch** is the segmented control the design system has, at the end of the top bar.

## Tokens

`app/design/directions/clarity/tokens.css` is the source. It's ported into `app/src/styles/tokens.css` under the
app's role names, with the density scale above and the brief's high-contrast support added. Components use tokens
only.
