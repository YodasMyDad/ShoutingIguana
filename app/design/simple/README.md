# Simple mode: the mockups

Static mockups of Simple, the switch, and the screens both modes share, on the garden crawl's real numbers. The
design they illustrate is [docs/v2-ui-simple.md](../../../docs/v2-ui-simple.md). They are built on the clarity
direction: every page loads [`../directions/clarity/tokens.css`](../directions/clarity/tokens.css) and `app.css`,
then [`simple.css`](simple.css), which adds only what Simple needs. Nothing here is app code.

Open them straight from disk. They need Google Fonts for Inter and JetBrains Mono, and nothing else. The theme
follows the OS, and `?theme=light` or `?theme=dark` overrides it. The switch in the top bar is drawn, not wired: each
mode's screens are their own files.

## The screens

| File | Mode | What it shows |
| --- | --- | --- |
| [`home.html`](home.html) | Simple | Check a website, and Your sites: a check running, the garden crawl, and five invented sites in every state a row can be in |
| [`home-first.html`](home-first.html) | Simple | A first start, with no crawls: the field, three steps, and where Advanced is |
| [`new-simple.html`](new-simple.html) | Simple | New crawl as Simple shows it: the address, how fast, how much, and two switches |
| [`checking.html`](checking.html) | Simple | The garden crawl at 60%: three steps, three figures, the floor on time left, Pause and Stop in the top bar |
| [`summary.html`](summary.html) | Simple | The Summary of the garden crawl: 85, Good; five fixes first; what's fine; what couldn't be checked; the report |
| [`score.html`](score.html) | Simple | "How the score is worked out", over the Summary: each line that cost points, the sum, what it covers, the points table |
| [`summary-perfect.html`](summary-perfect.html) | Simple | A site with nothing to fix: 100, and three things worth a look |
| [`summary-stopped.html`](summary-stopped.html) | Simple | A crawl stopped early: the banner with Continue, and a dashed score "so far" |
| [`fix.html`](fix.html) | Simple | What to fix: Serious, Worth fixing, Worth a look, Couldn't check, Nothing wrong |
| [`problem.html`](problem.html) | Simple | One problem, "Links that lead to broken pages": what's wrong, how to fix it, the 9 broken addresses and the pages that link to them |
| [`problem-template.html`](problem-template.html) | Simple | A template fault told as one thing to fix: "One fix clears 264 of these 265 pages" |
| [`report-screen.html`](report-screen.html) | Both | The Report screen: the toolbar, and the engine's document in a frame |
| [`report.html`](report.html) | Both | The report itself: one self-contained file, with no script and no web font |
| [`settings-simple.html`](settings-simple.html) | Simple | Settings > General: Mode and Theme, four sections, and where the rest is |
| [`settings-advanced.html`](settings-advanced.html) | Advanced | The same screen in Advanced: Mode, Theme and Density, eight sections |
| [`new-advanced.html`](new-advanced.html) | Advanced | New crawl with every setting: the start URL, the presets, five folded sections |
| [`advanced-overview.html`](advanced-overview.html) | Advanced | Overview as built, with the switch, the score and Report in the sidebar |
| [`advanced-live.html`](advanced-live.html) | Advanced | The top bar at its fullest: a crawl running, Pause, Stop, search and the switch. A shell test, not a new screen |

Screenshots are in `screens/` (gitignored):

- every page in both themes at 1440×900, `<page>-<theme>.png`;
- the shell at 1280×720, at 900×720 with the sidebar as icons, and at 200% zoom (a 1280×720 window),
  `shell-<page>-<size>-<theme>.png`;
- the report as it prints, `report-print-light.png` and two more spreads, one PNG a page, and `report.pdf`, which
  Chromium prints from `report.html`.

## What is real

Every number, address, page title and check on the garden screens comes from [`data/garden.json`](data/garden.json),
which [`build/extract.mjs`](build/extract.mjs) writes from a copy of the real crawl, analysed by this branch's
engine: 265 pages, 4,521 addresses, 82 of 86 checks run, 19 with something found (2 Error, 4 Warning, 13 Notice),
63 passed and 4 skipped.

- **The score** is worked out by [`build/lib/score.mjs`](build/lib/score.mjs), the reference the engine's own is
  tested against. The garden crawl scores 85.
- **The plain words** are in [`build/lib/words.mjs`](build/lib/words.mjs): a plain title and a good-news line for
  all 86 checks, and full texts for the 19 the garden crawl flagged and three more. The design document writes ten
  out as the reference; the rest are drafts for round 4.
- **The checking screen** is the garden crawl 22 min 26 s in, from the clarity live fixture: 2,713 checked, 1,417
  queued, 241 pages.
- **"Now"** is 1 November 2026, 17:42, as in the clarity mockups.

These are **invented**, on the reserved `.example` domain so they can't be real companies:

| Site | Shown as |
| --- | --- |
| kestrel-legal.example | The check running now, on Home and in the sidebar |
| oakmere-cycles.example | Nothing to fix: 100. Also `summary-perfect.html` |
| fernleigh-pottery.example | Nearly clean: 98, one problem worth fixing |
| tidewell-travel.example | Badly broken: 36, the design document's worked example |
| brightwater-dental.example | Stopped early, with a score "so far". Also `summary-stopped.html` |
| hollin-yoga.example | A 5-page site with one page missing its title: 89 |

## Where it bends

1. **A detail reads "404 Not Found"** where today's engine says "HTTP 404". The design asks the engine for the
   change, in both modes.
2. **`advanced-overview.html` redraws the built screen** from its screenshot, to show the switch, the score and
   Report on it. The built Overview is the source for everything else on that screen.
3. **The report's pages are laid out by `build/shoot.mjs`** for the PNGs, from the report's own print styles: it
   cuts where a block doesn't fit and at the forced page break. Chromium's PDF of the same file is the check: 11
   pages, against 12 laid out here, as Chromium fits a little more to a page.
4. **Favicons are letter monograms**, as in the clarity mockups.
5. **A few layout sizes in `simple.css` are plain pixel values** in grid templates (a column's least width). Every
   colour, type size, space and radius is a token.

## Icons

[Lucide](https://lucide.dev) (ISC licence), inlined as an SVG sprite in each page. The severity shapes and the
skipped and passed marks are the clarity direction's, so severity looks the same in both modes.

## Rebuild

Node 22.5 or later, and no npm packages for the build. From the repo root:

```powershell
# 1. The data: copy the crawl, analyse the COPY, extract. Never open the dev crawl itself.
copy "$env:LOCALAPPDATA\ShoutingIguana\dev-crawls\www.gardenfurnituresets.co.uk-20261031.sicrawl" $env:TEMP\garden.sicrawl
dotnet run --project engine/src/ShoutingIguana.Cli -c Release -- analyze $env:TEMP\garden.sicrawl
node app/design/simple/build/extract.mjs $env:TEMP\garden.sicrawl

# 2. The pages
node app/design/simple/build/build.mjs

# 3. The screenshots: Playwright comes from app/spike (run `npm ci` there), or from $env:SI_PLAYWRIGHT
node app/design/simple/build/shoot.mjs            # every page, both themes, 1440x900
node app/design/simple/build/shoot.mjs --shell    # 1280x720, 900 wide, 200% zoom
node app/design/simple/build/shoot.mjs --report   # the report as it prints, and report.pdf

# 4. The owner's gallery: copies the screenshots in reading order and writes index.json beside them
node app/design/simple/build/gallery.mjs app/design/simple/screens <the gallery's simple/shots folder>
```

Edit the templates in `build/pages/` and `build/lib/`, not the generated HTML.
