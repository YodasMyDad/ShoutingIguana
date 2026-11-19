# UX fix round, 2026-11-19: the five lanes

Every lane reads [`fix-common.md`](fix-common.md) and [`audit-common.md`](audit-common.md) first, then its own audit in
full. Finding ids below are the audit's. "Ruling" is the orchestrator's decision where the audit left a choice.

## issues: Advanced > Issues ([`audit-issues.md`](audit-issues.md), A-01 to A-18)

The screen the owner complained about. Their task, and the bar: **open "Internal URL returns 4xx" and, with no
expanding and at most one click, be on the page that holds each broken link, at the link.**

- **A-01, A-04, A-05 (rules 1 to 4).** Every address in every Issues table, sub-row and banner is a link that
  inspects it, with open-in-browser directly after it. The "Found on" line becomes parts: the page (link + open icon
  that opens `urlAtText(page, anchor)`), the anchor or alt text in quotes, the position, then "+N more" which expands
  the rest in place. The banner's page is the same link, plus "Show its N links" to Links filtered to that source and
  the broken status. "All N in the inspector" becomes a link that opens the inspector on the list it names. "Names"
  becomes "Links to", "Embeds", "Canonical to" and so on by evidence kind.
- **A-06. Ruling: flat by default** for checks whose rows are one fault each. Link faults read as columns, left to
  right in the order you fix: for a check whose subject is the broken URL (4xx, 5xx, redirects): `URL · Status ·
  Linked from (page) · Link text · Where on the page · +N`; for a check whose subject is the linking page (links to
  broken internal or external URLs, links to redirects): `Page · Links to (URL, status) · Link text · Where on the
  page`. One page with five broken links is five rows that sort together, or one row group headed by the page: pick
  what reads best on the garden crawl's `/blog/best-rattan-patio-sets/` and say why. Keep a "Group by …" control
  where grouping still earns its place (images by image, canonical loops, robots.txt rules), and where a tree stays:
  groups open when there are 20 or fewer, a group that is a URL is the standard link with a row menu that acts on it.
- **A-03. Ruling:** a row's single click selects (and the open inspector follows, as now); the address is the link
  that inspects. Remove the far-right `>`; no control may share the expand chevron's glyph. Give Issues the key
  legend strip the other grids have.
- **A-02** (170 px too wide on a compared crawl): fix, with a test that the columns never sum past the frame, compare
  on and off, inspector open and closed.
- **A-09, A-10 (rule 7).** One compact header: severity, title, the counts in one line ("8 URLs · 0.2% of 4,362 ·
  same as 17 Nov"), the actions. Drop the "Groups" figure. "What's wrong" in a readable measure; "Why it matters" and
  "How to fix it" beside it at wide sizes and folded at narrow ones, never both a jump link and a block. The table
  gets the rest of the pane's height with one scroller, the same for tree and flat tables, at 2560 × 1380 and at
  1280 × 720 (where rows must be visible on arrival).
- **A-07 (rule 9)** with the data lane, which owns `routing/inspector.ts`: you own keeping the expanded rows, the
  selected row and the scroll in Issues across Back. Tell the orchestrator if you need the inspector's history change
  before you can test yours.
- **A-11, A-12, A-13, A-14, A-15, A-16, A-17, A-18**: as the audit proposes. A-12: each "Since" chip gets words a
  first-time reader understands and using one keeps the others visible. A-13: the bottom strip's chips either become
  the real link they describe or go.
- **A-08** belongs to the engine lane (`app/src/changes/recheckWords.ts`).

## data: URLs, Links, Sitemaps, robots.txt, the inspector, the grid ([`audit-data.md`](audit-data.md), D-02 to D-20)

- **D-02, D-07, D-18 (rules 1, 2).** You own `Url`: make rule 1 and rule 2 true in every grid and list you own, and
  give `Url` the optional `openUrl` / `openHint` props described in `fix-common.md`. Sitemap and robots.txt rows:
  the name is the link that opens it; every origin's row opens something.
- **D-03.** Links grouped by target: the linking pages are visible links (rule 3), not a count behind a chevron.
  Links > Broken links is the pattern the audit praised: keep it, and make its source page open at the link (rule 4).
- **D-04, D-17 (rule 8).** Column sizing in `GridCore`: spare width goes to the columns people read; URL columns stop
  growing at their longest value; nothing is cut beside blank space; at 1280 the URL keeps a usable width.
- **D-05, D-08, D-09, D-13, D-15.** The inspector: a sensible default width for a wide window (ruling: about a third
  of the workspace, clamped, remembered once the user drags it), a visible resize edge, tabs that fit before "N more",
  lists that use the width, a back control after a hop, the grid's highlight following the walk, the link you came for
  marked and first when you arrive from a "linked from" row, counts that are links (rule 5), the sitemap named, the
  image shown. **A-07's history half is yours**: opening the inspector pushes one history entry and hops inside
  replace it, so Back closes the inspector and returns to the same check or grid state.
- **D-06** (click, Shift+click, Ctrl+C copies the browser's selection): fix with a test.
- **D-10, D-11, D-12.** Facets take several values; "Not indexable" can be asked and answers with pages; "Indexable"
  doesn't answer with images; missing and duplicate titles, descriptions and H1s can be listed from URLs in two clicks
  (a preset or a facet), and Title sorts; a redirect's row says where it goes. If one of these needs a sidecar query
  that doesn't exist, add it in `engine/src/ShoutingIguana.Sidecar` in a `Shared:` commit with its tests, and the read
  must stay inside readbench's gates.
- **D-14 (rule 9).** "Clear filters" clears search and sort too and says what it will clear; Back restores the
  selected row and scroll.
- **D-16, D-19, D-20**: as the audit proposes.
- **D-01 is the engine lane's.**

## shell: Home, New crawl, Overview, the frame, the palette, Settings ([`audit-shell.md`](audit-shell.md), A-01 to A-24)

Call your findings `H-01`… in commits and your report, to keep them apart from the issues lane's.

- **A-01** (the sidebar tooltip eats the next click): fix, with a test. No tooltip may cover a neighbouring control,
  and a tooltip closes on click.
- **A-02, A-16 (rule 9)**: scroll restoration on Back; the crawl switcher keeps the screen and, where the filter
  still makes sense on the other crawl, the filter.
- **A-03, A-10. Rulings:** the mode switch keeps your place wherever the other mode has that screen, and where it
  doesn't it says where it took you and brings you back to the exact place (filters too) when you flip again. The
  two "Settings" get distinct names: the crawl's is "This crawl's settings" (it is a record of how it was crawled:
  A-20), and the app's stays "Settings" and keeps the crawl's navigation and switcher on screen.
- **A-04**: "Crawl again" is a button wherever the text recommends it, and the palette finds it.
- **A-05**: a library row opens its crawl on click anywhere; its counts are links to Issues at that severity; columns
  sort.
- **A-06, A-07, A-19 (rule 5)**: every figure that names a list is a link to it; the comparison card says what got
  worse as plainly as what got better (a check going 18 → 257 is the headline, not "2 more") and its figures open the
  checks. A per-URL new/gone list is a feature: leave it and report it.
- **A-08**: the palette finds actions (crawl again, recheck, compare, export, switch mode) and settings by their
  words, and "404" finds the 4xx check.
- **A-09**: features, not this round. Do add one honest line to New crawl saying whether pages' JavaScript is run
  (read the engine to find out), and report the three for the owner.
- **A-11, A-12 (the Advanced report tab only: `screens/report/**` is the simple lane's, so send them what you'd
  change), A-13, A-14, A-15, A-17, A-18, A-21, A-22, A-23, A-24**: as the audit proposes. A-15: sizes are typed in KB
  or MB, never bytes or bits.

## simple: Simple mode and the report ([`audit-simple.md`](audit-simple.md), S-01 to S-21)

The owner on Simple: "it shows the broken link which people will click, but the people are going to be probably more
interested as to where the broken link is."

- **S-01** (the way back is dead on a problem's page): fix first, with a test for the link, the sidebar item and the
  shortcut.
- **S-02, S-03, S-04, S-07 (rules 1 to 4).** Build section 4 of the audit, "A broken link: what the card should lead
  with": the card is "Where to fix it", grouped by the page to edit (its title and path one link, "Open page" always
  visible, opening at the link's words), each broken link a row under it (the link's words, where on the page, where
  it leads, what's wrong, open). A link in the header or footer of many pages is one row. What nothing links to goes
  in its own short list. Apply the same thinking to every "Where it is" shape in the audit's section 3 table: lead
  with the page to open and the thing on it to change; no line that says nothing ("It's a page"); nothing useful
  behind "Show them" until the list is long. The first job on the Summary and What to fix leads to this view, with
  one job for "broken links", not two that mirror each other.
- **S-05.** "Copy the list" and the spreadsheet hold what a developer needs to fix from the list alone: one row per
  fault, the page that holds it first, then the element, the target and the status, in columns with headings.
- **S-06. Ruling:** add a search on What to fix that finds a page by its address or title and shows that page's
  problems, and let the top-bar search find pages in Simple. A "worst pages" list is a feature: report it.
- **S-09** (a false "one fix"): the strip appears only when the rows really share one cause; otherwise it's gone.
- **S-10, S-12 (rules 5, 9)**: your half of keeping your place across the mode switch and Back (the shell lane owns
  `modeRoutes.ts`: tell the orchestrator what you need from it); what got worse since the last check is shown as
  plainly as what was fixed.
- **S-11, and the shell audit's A-12.** The report: its links work in the app (and are plain text on paper), its rows
  use the same words as the screens (no "not indexable", "H1 → H4", "Inlinks: 1" in the Simple report; a working
  SEO's terms in the Advanced one), each problem says where to fix it, and "2 on more" becomes English.
- **S-13 to S-21 and the section 5 words list**: as the audit proposes. S-13: a title or description that's too long
  shows the text, where it's cut, and by how much.
- **S-08, S-15** (recheck numbers and result words) are the engine lane's where they come from
  `app/src/changes/**`; the words on your screens are yours.

## engine: the sitemap list lost on a recheck, and the recheck's numbers

- **D-01 ([`audit-data.md`](audit-data.md)), a data bug and the first job.** On the garden crawl the sitemap row says
  it listed 240 URLs and `sitemap_urls` is empty; the copy made before the 18 Nov recheck (`audit/data/garden-a.sicrawl`)
  has all 240. After it "Indexable page not in a sitemap" went 18 → 257, two real sitemap warnings vanished and the
  score rose 83 → 85. Find the statement that loses the rows (the recheck of a sitemap or robots.txt check, a refused
  or failed recheck, the re-analysis: reproduce it on the synthetic test site with `si testsite` and a recheck, never
  on a real site), fix it, and add the test that fails first. Then make a damaged file honest: when a crawl file says
  a sitemap listed N URLs and holds none of them, the analysis must not report sitemap findings from the empty list;
  the sitemap checks are skipped with a reason that tells the owner to crawl again, the Sitemaps screen says the same,
  and `AnalysisFreshness.VERSION` goes up so existing analyses are redone. Check the other tables a recheck rewrites
  for the same loss (links, images, hreflang, structured data, robots rules), and say what you checked.
- **A-08 / S-08 / S-15** (`app/src/changes/recheckWords.ts` and whatever feeds it): "Recheck 1,000 of 240 URLs",
  "Fetches 999 URLs … 166 past the cap", "Fetches 1 URLs", "It rechecks the 1,000 most important of 240. The other 90
  stay…". Trace what each number counts, make the button and its line say one true thing in one unit, with singulars,
  in Advanced's words and Simple's, and pin them with tests on the real shapes (a check on pages with many images, a
  check with one URL, a check past the cap).
- The shell audit's **A-23** (the Rechecks card and dialog) words that come from `changes/**`.
- Run the whole `verify-engine.sh` (bench and readbench included) before you report.
